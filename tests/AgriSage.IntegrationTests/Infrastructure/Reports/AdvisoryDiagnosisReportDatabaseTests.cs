using AgriSage.Application.Common;
using AgriSage.Application.Features.Advisory;
using AgriSage.Application.Features.Carts;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Diagnosis;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Reports;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.OnlineOrders;
using AgriSage.IntegrationTests.Infrastructure.Payments;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using AgriSage.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Reports;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1), always rolled back: GET /api/reports/advisory-diagnosis (REPORTS.md "Advisory
// and diagnosis"). The rows are moved to April 2033 so the report of that period holds only this test's rows whatever the
// database contains; created_at is system-managed, so the test moves it with SQL inside its own transaction.
[Collection(RealDb.WalkInPriceListCollection)]
public class AdvisoryDiagnosisReportDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private static readonly DateOnly April1 = new(2033, 4, 1);

    // 09:00 in Vietnam on 1 April 2033, as a UTC instant.
    private static readonly DateTimeOffset T0 = BusinessCalendar.StartOfDay(April1).AddHours(9);

    private static readonly AdvisoryDiagnosisReportRequest April = new() { FromDate = April1, ToDate = new(2033, 4, 30) };

    private sealed class Env(OnlineOrderTestData data)
    {
        private readonly DateTimeProvider _clock = new();
        private readonly NpgsqlErrorClassifier _errors = new();

        public OnlineOrderTestData Data { get; } = data;

        private AgriSageDbContext Context => Data.Context;

        private AuditTrail Audit => new(Context, Data.User, _clock);

        public MyAdvisoryService Advice => new(Context, new CurrentFarmer(Context, Data.User),
            new AdvisoryEngine(Context, new PriceResolver(Context), _clock), _clock, Audit);

        public DiseaseProductService Links => new(Context, Data.User, _clock, Audit);

        public DiseaseService Diseases => new(Context, Audit);

        public CartService Carts => new(Context, new CurrentFarmer(Context, Data.User), new PriceResolver(Context),
            new RowLockService(Context), _errors, Data.User, _clock);

        public MeOrderService Orders
        {
            get
            {
                var locks = new RowLockService(Context);
                var builder = new OrderBuilder(Context, new PriceResolver(Context), Data.User, _clock, Audit,
                    new AgriSage.Application.Features.Credit.CreditEligibilityService(Context, _clock));
                var queries = new OrderQueries(Context);

                return new MeOrderService(Context, new CurrentFarmer(Context, Data.User), builder, queries,
                    new OrderService(Context, builder, queries, Data.User, _clock, _errors, Audit),
                    new OrderCanceller(Context, locks, new StubOrderSettlementGuard(),
                        new OrderPaymentCancellation(Context, locks, new FakePaymentGateway(), _clock, Audit)),
                    locks, _clock, _errors, Audit);
            }
        }

        public AdvisoryDiagnosisReportService Report => new(Context, new ReportScope(Context, Data.User));

        public async Task<DiseaseResponse> ProblemAsync()
        {
            var tag = Tag();
            return await Diseases.CreateAsync(new DiseaseCreateRequest($"T{tag.ToUpperInvariant()}", $"Bệnh thử báo cáo {tag}", "DISEASE"), Token);
        }

        // A new Farmer asks for advice on the problem; the session is moved to `at`.
        public async Task<(OnlineOrderTestData.Farmer Farmer, Guid SessionId)> AdviceAsync(DiseaseResponse problem, DateTimeOffset at)
        {
            var farmer = await Data.ActAsNewFarmerAsync();
            var advice = await Advice.RecommendAsync(new AdvisoryRecommendationRequest(problem.Id, 10_000m, null, null, "bao cao"), Token);
            await Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE advisory_sessions SET created_at = {at} WHERE id = {advice.SessionId}", Token);

            return (farmer, advice.SessionId);
        }

        // The Farmer orders `bottles` bottles of a product; the order is moved to `at`.
        public async Task<OrderResponse> OrderAsync(OnlineOrderTestData.Farmer farmer, OnlineOrderTestData.Priced product, long bottles, DateTimeOffset at)
        {
            Data.ActAs(farmer.UserId, "FARMER");
            await Carts.AddItemAsync(new(product.StoreProductId, product.BottleId, bottles), Token);
            var order = await Orders.CheckoutAsync(new("FARMER_WEB", "FULL_PAYMENT", "PICKUP"), Token);
            await Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders SET created_at = {at} WHERE id = {order.Id}", Token);

            return order;
        }
    }

    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var data = await OnlineOrderTestData.PrepareAsync(session);
        data.Context.StoreMembers.Add(new StoreMember(data.StoreId, data.Staff.Id));
        await data.Context.SaveChangesAsync(Token);
        data.ActAsStaff();

        return new Env(data);
    }

    [RealDbFact]
    public async Task Advice_converts_only_for_the_same_farmer_within_seven_days_with_a_suggested_product()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        var data = env.Data;
        var advised = await data.NewProductAsync();
        var other = await data.NewProductAsync();
        await data.NewLotAsync(advised.StoreProductId, "L-" + Tag(), 100, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300));
        await data.NewLotAsync(other.StoreProductId, "L-" + Tag(), 100, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300));
        var problem = await env.ProblemAsync();
        var bare = await env.ProblemAsync(); // no approved product: its advice suggests nothing
        var link = await env.Links.CreateAsync(problem.Id, new CreateDiseaseProductRequest(advised.ProductId, "PROTECTION", "Pha 2 chai cho 1 ha",
            RegistrationSource: "TT 75/2025/TT-BNNMT, Phụ lục I, tr. 1", DosePerHaBase: 2m, Applications: 1, PreHarvestDays: 14), Token);
        await env.Links.ApproveAsync(link.Id, Token);
        var day = TimeSpan.FromDays(1);

        // A orders the advised product a day later and again exactly seven days later: converted, both lines count.
        var (a, sessionA) = await env.AdviceAsync(problem, T0);
        await env.OrderAsync(a, advised, 1, T0 + day);
        await env.OrderAsync(a, advised, 2, T0 + 7 * day);
        // B orders it one minute after the seven days; C cancels the order; D buys another product; E bought it before.
        var (b, _) = await env.AdviceAsync(problem, T0.AddHours(1));
        await env.OrderAsync(b, advised, 1, T0.AddHours(1) + 7 * day + TimeSpan.FromMinutes(1));
        var (c, _) = await env.AdviceAsync(problem, T0.AddHours(2));
        var cancelled = await env.OrderAsync(c, advised, 1, T0.AddHours(3));
        await env.Orders.CancelAsync(cancelled.Id, new CancelMyOrderRequest("Đổi ý"), Token);
        var (d, _) = await env.AdviceAsync(problem, T0.AddHours(3));
        await env.OrderAsync(d, other, 1, T0.AddHours(4));
        var (e, _) = await env.AdviceAsync(problem, T0.AddHours(4));
        await env.OrderAsync(e, advised, 1, T0.AddHours(3));
        // G asks about a problem with no product; F's advice is the day before the period.
        await env.AdviceAsync(bare, T0.AddHours(5));
        await env.AdviceAsync(problem, T0 - day);

        data.ActAsStaff();
        var report = await env.Report.GetAsync(April, Token);

        Assert.Equal(7, report.AttributionDays);
        var advisory = report.Advisory;
        Assert.Equal(6, advisory.SessionCount);
        Assert.Equal(6, advisory.FarmerCount);
        Assert.Equal((6, 0), (advisory.TextSessionCount, advisory.DiagnosisSessionCount));
        Assert.Equal(5, advisory.SessionsWithProducts);
        Assert.Equal(1, advisory.ConvertedSessions);
        Assert.Equal(20.00m, advisory.ConversionPercent);
        Assert.Equal(30_000m, advisory.ConvertedOrderValue); // 1 + 2 bottles at 10 000, suggested lines only

        var inPeriod = await data.Context.AdvisorySessions.AsNoTracking()
            .Where(s => s.CreatedAt >= T0 && s.CreatedAt < T0 + day).Select(s => s.Id).ToListAsync(Token);
        var estimated = await data.Context.AdvisorySessionItems.AsNoTracking()
            .Where(i => inPeriod.Contains(i.AdvisorySessionId)).SumAsync(i => i.EstimatedAmount ?? 0m, Token);
        Assert.True(estimated > 0);
        Assert.Equal(estimated, advisory.EstimatedValue);

        Assert.Equal(new[] { (problem.Id, 5), (bare.Id, 1) }, report.TopProblems.Select(p => (p.DiseaseId, p.SessionCount)));
        Assert.Equal((problem.Code, problem.Name, CropCatalog.Rice), (report.TopProblems[0].Code, report.TopProblems[0].Name, report.TopProblems[0].CropType));
        Assert.Contains(sessionA, inPeriod);

        // Another period sees none of it.
        var march = await env.Report.GetAsync(new AdvisoryDiagnosisReportRequest { FromDate = new(2033, 3, 1), ToDate = new(2033, 3, 30) }, Token);
        Assert.Equal((0, 0, (decimal?)null), (march.Advisory.SessionCount, march.Advisory.SessionsWithProducts, march.Advisory.ConversionPercent));
        Assert.Empty(march.TopProblems);
    }

    [RealDbFact]
    public async Task Diagnosis_cases_count_per_crop_by_the_latest_successful_inference()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        var data = env.Data;
        var context = data.Context;
        var farmer = await data.ActAsNewFarmerAsync();
        data.ActAsStaff();
        var now = DateTimeOffset.UtcNow;
        var disease = await context.Diseases.SingleAsync(x => x.Code == "LEAF_BLAST", Token);
        var model = new AiModel("report-test-" + Tag(), "1", "PyTorch", "https://example.test/model", "[]", data.Staff.Id);
        model.Activate(now.AddDays(-1));
        var policy = new AiPolicyConfig(model.Id, "1", .7m, now.AddDays(-1), data.Staff.Id);
        policy.Activate();

        DiagnosisCase Case(string crop, DateTimeOffset submittedAt, params bool[] passed)
        {
            var c = new DiagnosisCase(farmer.ProfileId, "DG-R-" + Tag(), submittedAt, null, crop);
            if (passed.Length == 0) return c; // still SUBMITTED
            var image = c.AddImage("cases/report.jpg", "https://example.test/report.jpg", now, isPrimary: true);
            c.StartProcessing();
            for (var i = 0; i < passed.Length; i++)
                c.RecordInference(image.Id, model, policy, disease.Code, passed[i] ? .9m : .5m, passed[i],
                    AiInferenceStatus.Success, now.AddMinutes(i), disease.Id);
            c.CompleteAi();
            return c;
        }

        var failed = new DiagnosisCase(farmer.ProfileId, "DG-R-" + Tag(), T0, null, CropCatalog.Durian);
        failed.AddImage("cases/report.jpg", "https://example.test/report.jpg", now, isPrimary: true);
        failed.StartProcessing();
        failed.FailAi();
        context.AddRange(model, policy, failed,
            Case(CropCatalog.Rice, T0, true),
            Case(CropCatalog.Rice, T0, false),
            Case(CropCatalog.Rice, T0, true, false), // the latest SUCCESS inference decides: uncertain
            Case(CropCatalog.Durian, T0),
            Case(CropCatalog.Durian, T0.AddDays(29), true),
            Case(CropCatalog.Rice, T0.AddDays(-1), true)); // the day before the period
        await context.SaveChangesAsync(Token);

        var diagnosis = (await env.Report.GetAsync(April, Token)).Diagnosis;

        Assert.Equal(6, diagnosis.CaseCount);
        Assert.Equal(
            new[] { new DiagnosisCropRow(CropCatalog.Rice, "Lúa", 3, 1, 2, 0, 0), new DiagnosisCropRow(CropCatalog.Durian, "Sầu riêng", 3, 1, 0, 0, 2) },
            diagnosis.ByCrop);
    }
}
