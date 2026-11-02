using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Advisory;
using AgriSage.Application.Features.Carts;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Diagnosis;
using AgriSage.Application.Features.Notifications;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.OnlineOrders;
using AgriSage.IntegrationTests.Infrastructure.Payments;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using AgriSage.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Advisory;

// api-flows/ADVISORY.md against the real PostgreSQL schema, inside a transaction that is always rolled back
// (AGRISAGE_DB_TESTS=1): a problem, its reviewed links, the advice for a field, the cart, the order, the staff view.
[Collection(RealDb.WalkInPriceListCollection)]
public class AdvisoryDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private sealed class Env(OnlineOrderTestData data)
    {
        private readonly DateTimeProvider _clock = new();
        private readonly NpgsqlErrorClassifier _errors = new();

        public OnlineOrderTestData Data { get; } = data;

        private AuditTrail Audit => new(Data.Context, Data.User, _clock);

        public AdvisoryEngine Engine => new(Data.Context, new PriceResolver(Data.Context), _clock);

        public MyAdvisoryService Farmer => new(Data.Context, new CurrentFarmer(Data.Context, Data.User), Engine, _clock, Audit);

        public DiseaseProductService Links => new(Data.Context, Data.User, _clock, Audit);

        public DiseaseService Diseases => new(Data.Context, Audit);

        public AdvisorySessionService Sessions => new(Data.Context);

        public CartService Carts => new(Data.Context, new CurrentFarmer(Data.Context, Data.User), new PriceResolver(Data.Context),
            new RowLockService(Data.Context), _errors, Data.User, _clock);

        public MeOrderService Orders
        {
            get
            {
                var locks = new RowLockService(Data.Context);
                var builder = new OrderBuilder(Data.Context, new PriceResolver(Data.Context), Data.User, _clock, Audit,
                    new AgriSage.Application.Features.Credit.CreditEligibilityService(
                        Data.Context, _clock));
                var queries = new OrderQueries(Data.Context);

                return new MeOrderService(Data.Context, new CurrentFarmer(Data.Context, Data.User), builder, queries,
                    new OrderService(Data.Context, builder, queries, Data.User, _clock, _errors, Audit),
                    new OrderCanceller(Data.Context, locks, new StubOrderSettlementGuard(),
                        new OrderPaymentCancellation(Data.Context, locks, new FakePaymentGateway(), _clock, Audit)),
                    locks, _clock, _errors, Audit);
            }
        }
    }

    // A problem created by the owner (so the create route is part of every test) and a store member for ordering.
    private static async Task<(Env Env, DiseaseResponse Problem)> PrepareAsync(RealDb.Session session)
    {
        var data = await OnlineOrderTestData.PrepareAsync(session);
        var env = new Env(data);
        data.Context.StoreMembers.Add(new StoreMember(data.StoreId, data.Staff.Id));
        await data.Context.SaveChangesAsync(Token);

        data.ActAsStaff();
        var tag = Tag();
        var problem = await env.Diseases.CreateAsync(
            new DiseaseCreateRequest(
                $"T{tag.ToUpperInvariant()}", $"Bệnh thử nghiệm đạo ôn {tag}", "DISEASE",
                Symptoms: "Vết hình thoi, tâm xám trắng.", SearchKeywords: $"cháy lá thử{tag}"),
            Token);

        return (env, problem);
    }

    private static CreateDiseaseProductRequest Protection(Guid productId, int priority = 0, decimal? dose = 2m, IReadOnlyList<string>? stages = null) =>
        new(productId, "PROTECTION", "Pha 2 chai cho 1 ha", RegistrationSource: "TT 75/2025/TT-BNNMT, Phụ lục I, tr. 1",
            DosePerHaBase: dose, Applications: 1, PreHarvestDays: 14, GrowthStages: stages, Priority: priority);

    private static async Task<DiseaseProductResponse> ApprovedAsync(Env env, Guid diseaseId, CreateDiseaseProductRequest request)
    {
        var link = await env.Links.CreateAsync(diseaseId, request, Token);

        return await env.Links.ApproveAsync(link.Id, Token);
    }

    private static AdvisoryRecommendationRequest For(DiseaseResponse problem, decimal? area = null, string? stage = null,
        Guid? caseId = null) => new(problem.Id, area, stage, caseId, "dao on");

    [RealDbFact]
    public async Task A_problem_is_created_found_without_accents_and_the_five_ai_classes_stay_active()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, problem) = await PrepareAsync(session);
        var tag = problem.Name[^10..];

        Assert.Equal("DISEASE", problem.ProblemType);
        await Assert.ThrowsAsync<ConflictException>(() => env.Diseases.CreateAsync(
            new DiseaseCreateRequest(problem.Code, "Trùng mã", "PEST"), Token));

        await env.Data.ActAsNewFarmerAsync();
        var byName = await env.Farmer.ProblemsAsync(new AdvisoryProblemListRequest { Search = $"benh thu nghiem dao on {tag}" }, Token);
        var found = Assert.Single(byName.Items);
        Assert.Equal(problem.Id, found.Id);
        Assert.Equal("NAME", found.MatchedBy);
        Assert.Equal("Vết hình thoi, tâm xám trắng.", found.Summary);

        var byKeyword = await env.Farmer.ProblemsAsync(new AdvisoryProblemListRequest { Search = $"chay la thu{tag}" }, Token);
        Assert.Equal("KEYWORD", Assert.Single(byKeyword.Items).MatchedBy);
        var byType = await env.Farmer.ProblemsAsync(new AdvisoryProblemListRequest { Search = tag, Type = "PEST" }, Token);
        Assert.Empty(byType.Items);

        // One of the five classes the AI predicts cannot be switched off; a new problem can, and then it is not listed.
        var context = env.Data.Context;
        var seeded = await context.Diseases.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Code == "LEAF_BLAST", Token);
        if (seeded is null)
        {
            seeded = new Disease("LEAF_BLAST", "Leaf Blast");
            context.Diseases.Add(seeded);
            await context.SaveChangesAsync(Token);
        }

        env.Data.ActAsStaff();
        await Assert.ThrowsAsync<DomainException>(() => env.Diseases.SetActiveAsync(seeded.Id, false, Token));
        await env.Diseases.SetActiveAsync(problem.Id, false, Token);

        await env.Data.ActAsNewFarmerAsync();
        Assert.Empty((await env.Farmer.ProblemsAsync(new AdvisoryProblemListRequest { Search = tag }, Token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => env.Farmer.RecommendAsync(For(problem), Token));
    }

    [RealDbFact]
    public async Task Only_approved_links_of_products_the_farmer_can_buy_are_offered_with_quantity_price_and_stock_status()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, problem) = await PrepareAsync(session);
        var data = env.Data;
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300);

        var inStock = await data.NewProductAsync();
        await data.NewLotAsync(inStock.StoreProductId, "L-" + Tag(), 100, expiry);
        var soldOut = await data.NewProductAsync();
        var draftOnly = await data.NewProductAsync();
        await data.NewLotAsync(draftOnly.StoreProductId, "L-" + Tag(), 100, expiry);
        var withdrawn = await data.NewProductAsync();
        await data.NewLotAsync(withdrawn.StoreProductId, "L-" + Tag(), 100, expiry);
        var fertilizer = await data.NewProductAsync();
        await data.NewLotAsync(fertilizer.StoreProductId, "L-" + Tag(), 100, expiry);

        // Nothing is approved yet: the farmer is told there is no approved product.
        var draft = await env.Links.CreateAsync(problem.Id, Protection(draftOnly.ProductId), Token);
        Assert.Equal("DRAFT", draft.Status);
        await data.ActAsNewFarmerAsync();
        var empty = await env.Farmer.RecommendAsync(For(problem, 10_000m), Token);
        Assert.Equal(AdvisoryText.NoApprovedProduct, empty.EmptyReason);
        Assert.Empty(empty.Protection);

        data.ActAsStaff();
        await ApprovedAsync(env, problem.Id, Protection(inStock.ProductId, priority: 10));
        await ApprovedAsync(env, problem.Id, Protection(soldOut.ProductId, priority: 99));
        await ApprovedAsync(env, problem.Id, Protection(withdrawn.ProductId, priority: 50));
        await ApprovedAsync(env, problem.Id, new CreateDiseaseProductRequest(
            fertilizer.ProductId, "NUTRITION", "Bón 12 đơn vị cho 1 ha", Reason: "Bổ sung kali cho lúa", DosePerHaBase: 12m));
        await data.StoreProducts.SetSellableAsync(withdrawn.StoreProductId, false, Token);

        await data.ActAsNewFarmerAsync();
        var oneHectare = await env.Farmer.RecommendAsync(For(problem, 10_000m), Token);

        // Out of stock goes last although its priority is higher; the draft and the withdrawn product are not offered.
        Assert.Equal([inStock.StoreProductId, soldOut.StoreProductId], oneHectare.Protection.Select(p => p.StoreProductId).ToList());
        Assert.Null(oneHectare.EmptyReason);
        var first = oneHectare.Protection[0];
        Assert.Equal(1, first.RankOrder);
        Assert.Equal("ENOUGH", first.StockStatus);
        Assert.Equal("TT 75/2025/TT-BNNMT, Phụ lục I, tr. 1", first.RegistrationSource);
        Assert.Equal(14, first.PreHarvestDays);
        Assert.StartsWith("Đăng ký phòng trừ:", first.Reason);

        // 2 bottles per hectare on 1 ha: two bottles at 10 000.
        var plan = first.Plan!;
        Assert.Equal(2, plan.BaseQuantity);
        Assert.Equal(20_000m, plan.TotalAmount);
        var line = Assert.Single(plan.Lines);
        Assert.Equal(inStock.BottleId, line.ProductPackagingId);
        Assert.Equal(2, line.Quantity);
        Assert.Equal("OUT_OF_STOCK", oneHectare.Protection[1].StockStatus);

        var nutrition = Assert.Single(oneHectare.Nutrition);
        Assert.Equal(fertilizer.StoreProductId, nutrition.StoreProductId);
        Assert.Equal("Bổ sung kali cho lúa", nutrition.Reason);
        Assert.Null(nutrition.RegistrationSource);
        Assert.Equal(110_000m, nutrition.Plan!.TotalAmount); // 12 base units = two boxes of 6 at 55 000

        // Three hectares: six bottles are one whole box (55 000), cheaper than six bottles.
        var threeHectares = await env.Farmer.RecommendAsync(For(problem, 30_000m), Token);
        var box = Assert.Single(threeHectares.Protection[0].Plan!.Lines);
        Assert.Equal(inStock.BoxId, box.ProductPackagingId);
        Assert.Equal(1, box.Quantity);
        Assert.Equal(55_000m, threeHectares.Protection[0].Plan!.TotalAmount);

        // Without an area the products are listed with their dose and no quantity.
        var noArea = await env.Farmer.RecommendAsync(For(problem), Token);
        Assert.All(noArea.Protection, p => Assert.Null(p.Plan));
        Assert.Equal("Pha 2 chai cho 1 ha", noArea.Protection[0].DoseText);

        // Editing an approved link sends it back to review: the product disappears until it is approved again.
        data.ActAsStaff();
        var approvedLink = (await env.Links.ListAsync(problem.Id, Token)).Single(l => l.ProductId == inStock.ProductId);
        var edited = await env.Links.UpdateAsync(
            approvedLink.Id, new DiseaseProductRequest("PROTECTION", "Liều mới", "TT 75/2025", DosePerHaBase: 3m), Token);
        Assert.Equal("DRAFT", edited.Status);
        Assert.Null(edited.ApprovedBy);
        await data.ActAsNewFarmerAsync();
        var afterEdit = await env.Farmer.RecommendAsync(For(problem, 10_000m), Token);
        Assert.DoesNotContain(afterEdit.Protection, p => p.StoreProductId == inStock.StoreProductId);

        data.ActAsStaff();
        await env.Links.DeactivateAsync(approvedLink.Id, Token);
        Assert.Equal("INACTIVE", (await env.Links.ListAsync(problem.Id, Token)).Single(l => l.Id == approvedLink.Id).Status);
        await Assert.ThrowsAsync<ConflictException>(() => env.Links.CreateAsync(problem.Id, Protection(inStock.ProductId), Token));
    }

    [RealDbFact]
    public async Task The_growth_stage_filters_the_links_a_healthy_leaf_gets_no_advice_and_another_farmers_case_is_refused()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, problem) = await PrepareAsync(session);
        var data = env.Data;
        var product = await data.NewProductAsync();
        await data.NewLotAsync(product.StoreProductId, "L-" + Tag(), 100, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300));
        await ApprovedAsync(env, problem.Id, Protection(product.ProductId, stages: ["HEADING", "BOOTING"]));

        var owner = await data.ActAsNewFarmerAsync();
        var heading = await env.Farmer.RecommendAsync(For(problem, 10_000m, "HEADING"), Token);
        Assert.Single(heading.Protection);
        Assert.Equal(["BOOTING", "HEADING"], heading.Protection[0].GrowthStages);
        Assert.Equal("HEADING", heading.GrowthStage);

        var seedling = await env.Farmer.RecommendAsync(For(problem, 10_000m, "SEEDLING"), Token);
        Assert.Empty(seedling.Protection);
        Assert.Equal(AdvisoryText.NoProductForStage, seedling.EmptyReason);
        Assert.Single((await env.Farmer.RecommendAsync(For(problem, 10_000m), Token)).Protection); // no stage: every link

        // A healthy leaf has nothing to advise.
        var healthy = new Disease($"H{Tag().ToUpperInvariant()}", "Lá khỏe", isHealthyClass: true);
        data.Context.Diseases.Add(healthy);
        await data.Context.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Farmer.RecommendAsync(new(healthy.Id), Token));

        // The case of the AI diagnosis must be the Farmer's own.
        var ownCase = new DiagnosisCase(owner.ProfileId, "DG-T-" + Tag(), DateTimeOffset.UtcNow, null);
        data.Context.DiagnosisCases.Add(ownCase);
        await data.Context.SaveChangesAsync(Token);
        var fromCase = await env.Farmer.RecommendAsync(For(problem, 10_000m, caseId: ownCase.Id), Token);
        Assert.NotEqual(Guid.Empty, fromCase.SessionId);

        await data.ActAsNewFarmerAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => env.Farmer.RecommendAsync(For(problem, caseId: ownCase.Id), Token));
    }

    [RealDbFact]
    public async Task Advice_is_logged_reused_within_half_an_hour_and_staff_see_whether_it_led_to_a_purchase()
    {
        await using var session = await RealDb.Session.StartAsync();
        var (env, problem) = await PrepareAsync(session);
        var data = env.Data;
        var product = await data.NewProductAsync();
        await data.NewLotAsync(product.StoreProductId, "L-" + Tag(), 100, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300));
        await ApprovedAsync(env, problem.Id, Protection(product.ProductId));

        var farmer = await data.ActAsNewFarmerAsync();
        var first = await env.Farmer.RecommendAsync(For(problem, 10_000m), Token);
        var again = await env.Farmer.RecommendAsync(For(problem, 30_000m, "TILLERING"), Token);
        Assert.Equal(first.SessionId, again.SessionId);

        var context = data.Context;
        var session1 = await context.AdvisorySessions.AsNoTracking().SingleAsync(s => s.Id == first.SessionId, Token);
        Assert.Equal(30_000m, session1.AreaSqm);
        Assert.Equal(AdvisorySource.Text, session1.Source);
        Assert.Equal("dao on", session1.QueryText);
        var items = await context.AdvisorySessionItems.AsNoTracking().Where(i => i.AdvisorySessionId == first.SessionId).ToListAsync(Token);
        var item = Assert.Single(items); // replaced, not appended
        Assert.Equal(product.StoreProductId, item.StoreProductId);
        Assert.Equal(6, item.SuggestedBaseQuantity);
        Assert.Equal(55_000m, item.EstimatedAmount);
        Assert.Contains(
            await context.AuditLogs.AsNoTracking().Where(a => a.EntityId == first.SessionId).Select(a => a.Action).ToListAsync(Token),
            action => action == "ADVISORY_RECOMMENDED");

        // The advice shows no purchase yet; the Farmer then orders the advised product from it.
        data.ActAsStaff();
        var listed = await env.Sessions.ListAsync(new AdvisorySessionListRequest { DiseaseId = problem.Id }, Token);
        var row = Assert.Single(listed.Items);
        Assert.Equal(farmer.Name, row.Farmer.Name);
        Assert.Equal(problem.Name, row.Problem.Name);
        Assert.Equal("TEXT", row.Source);
        Assert.Equal("TILLERING", row.GrowthStage);
        Assert.Equal(product.StoreProductId, Assert.Single(row.Items).StoreProductId);
        Assert.False(row.BoughtWithin7Days);

        data.ActAs(farmer.UserId, "FARMER");
        var line = again.Protection[0].Plan!.Lines[0];
        await env.Carts.AddItemAsync(new(product.StoreProductId, line.ProductPackagingId, line.Quantity), Token);
        var order = await env.Orders.CheckoutAsync(new("FARMER_WEB", "FULL_PAYMENT", "PICKUP"), Token);
        Assert.Equal(55_000m, order.TotalAmount);

        data.ActAsStaff();
        Assert.True(Assert.Single((await env.Sessions.ListAsync(new AdvisorySessionListRequest { DiseaseId = problem.Id }, Token)).Items)
            .BoughtWithin7Days);

        // Another problem's sessions and other dates do not match it.
        var other = await env.Sessions.ListAsync(
            new AdvisorySessionListRequest { DiseaseId = problem.Id, To = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30) }, Token);
        Assert.Empty(other.Items);
    }
}
