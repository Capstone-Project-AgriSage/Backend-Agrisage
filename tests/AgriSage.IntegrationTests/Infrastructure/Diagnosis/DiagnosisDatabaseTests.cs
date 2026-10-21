using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Diagnosis;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Products.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Diagnosis;

// The AI diagnosis use cases against the real PostgreSQL development database, inside a transaction that is always
// rolled back (AGRISAGE_DB_TESTS=1). The AI service and the storage are fakes; everything else is the production code.
// AI_DIAGNOSIS.md 2.0: no Human Review, the Farmer reads the AI result at once.
public sealed class DiagnosisDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string[] Codes = ["LEAF_BLAST", "BACTERIAL_LEAF_BLIGHT", "BROWN_SPOT", "SHEATH_BLIGHT", "HEALTHY"];

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    // Both the public-bucket interface and the private store, over one dictionary of uploaded bytes.
    private sealed class FakeFiles : IFileStorageService, IPrivateFileStore
    {
        public Dictionary<string, byte[]> Objects { get; } = [];

        public List<string> Deleted { get; } = [];

        public bool SigningFails { get; set; }

        public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await request.Content.CopyToAsync(copy, cancellationToken);
            var key = $"{request.Folder}/{request.FileName}";
            Objects[key] = copy.ToArray();

            return new StoredFileResult(key, $"https://storage.test/private/{key}", copy.Length);
        }

        public Task DeleteAsync(string storageKey, StorageArea area, CancellationToken cancellationToken)
        {
            Deleted.Add(storageKey);
            Objects.Remove(storageKey);

            return Task.CompletedTask;
        }

        public string? KeyFromPublicUrl(string url, StorageArea area) => null;

        public Task<string> CreateSignedUrlAsync(string storageKey, StorageArea area, TimeSpan lifetime, CancellationToken cancellationToken) =>
            SigningFails
                ? throw new StorageUnavailableException()
                : Task.FromResult($"https://storage.test/signed/{storageKey}?token=t");

        public Task<byte[]> ReadAsync(string storageKey, StorageArea area, CancellationToken cancellationToken) =>
            Objects.TryGetValue(storageKey, out var bytes) ? Task.FromResult(bytes) : throw new StorageUnavailableException();
    }

    private sealed class FakeAi : IAiDiagnosisClient
    {
        public Func<AiPredictionRequest, AiPredictionResult>? Answer { get; set; }

        public int Calls { get; private set; }

        public Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken)
        {
            Calls++;

            return Answer is null
                ? throw new AiServiceUnavailableException()
                : Task.FromResult(Answer(request));
        }
    }

    private sealed record Env(
        RealDb.Session Session,
        FakeFiles Files,
        FakeAi Ai,
        Guid FarmerUserId,
        Guid FarmerProfileId,
        Guid OtherFarmerUserId,
        Guid StaffUserId,
        Guid StaffMemberId,
        Guid StoreId,
        Guid ModelId,
        Guid PolicyId,
        Dictionary<string, Guid> Diseases,
        string Tag)
    {
        public IOptions<AiDiagnosisOptions> Options { get; set; } = Microsoft.Extensions.Options.Options.Create(new AiDiagnosisOptions());

        public void As(Guid userId, string role)
        {
            Session.CurrentUser.UserId = userId;
            Session.CurrentUser.Role = role;
        }

        public async Task<T> Farmer<T>(Func<IMyDiagnosisCaseService, Task<T>> call, Guid? userId = null)
        {
            As(userId ?? FarmerUserId, "FARMER");
            await using var db = Session.NewContext();
            var clock = new Clock();
            var audit = new AuditTrail(db, Session.CurrentUser, clock);
            var results = new DiagnosisResultBuilder(db);
            var runner = new DiagnosisAiRunner(
                db, Ai, clock, new RowLockService(db), results, audit, Options, NullLogger<DiagnosisAiRunner>.Instance);
            var urls = new DiagnosisImageUrls(Files, clock, NullLogger<DiagnosisImageUrls>.Instance);
            var service = new MyDiagnosisCaseService(
                db, new CurrentFarmer(db, Session.CurrentUser), Files, Files, clock, runner, results, urls, audit,
                NullLogger<MyDiagnosisCaseService>.Instance);

            return await call(service);
        }

        public async Task<T> Staff<T>(Func<IDiagnosisCaseService, Task<T>> call, Guid? userId = null, string role = "SALES_STAFF")
        {
            As(userId ?? StaffUserId, role);
            await using var db = Session.NewContext();
            var clock = new Clock();
            var urls = new DiagnosisImageUrls(Files, clock, NullLogger<DiagnosisImageUrls>.Instance);

            return await call(new DiagnosisCaseService(db, new DiagnosisResultBuilder(db), urls));
        }

        public async Task<T> Models<T>(Func<IAiModelService, Task<T>> call)
        {
            As(StaffUserId, "STORE_OWNER");
            await using var db = Session.NewContext();
            var clock = new Clock();

            return await call(new AiModelService(db, Session.CurrentUser, clock, new AuditTrail(db, Session.CurrentUser, clock)));
        }

        public Task<MyDiagnosisCaseResponse> Submit(string? note = "Lá vàng từ chóp") =>
            Farmer(s => s.CreateAsync(new MemoryStream(Photo()), "leaf.jpg", note, Token));
    }

    // Leaf Blast guidance and products of this test only (the development database may hold other content).
    private sealed record Catalog(
        Guid ChemicalId,
        Guid CulturalId,
        Guid Suggested,
        Guid NotSellable,
        Guid InactiveProduct,
        Guid OtherStore,
        Guid DeletedLink,
        Guid NoIngredient)
    {
        public Guid[] All => [Suggested, NotSellable, InactiveProduct, OtherStore, DeletedLink, NoIngredient];
    }

    private static byte[] Photo() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 1, 2, 3, 4, 5, 6, 7, 8];

    private static AiPredictionResult Answer(
        string label, decimal confidence, string version = "0.1.0", bool stub = false, params (string, decimal)[] others)
    {
        var scores = new List<AiClassScore> { new(label, confidence) };
        scores.AddRange(others.Select(o => new AiClassScore(o.Item1, o.Item2)));

        return new AiPredictionResult(label, confidence, scores, version, 12, confidence, "agrisage-rice-classifier", stub);
    }

    private static async Task<Env> PrepareAsync(RealDb.Session session, bool withModel = true)
    {
        await using var db = session.NewContext();
        var tag = Guid.NewGuid().ToString("N")[..10];

        async Task<User> Actor(RoleCode code, string name)
        {
            var role = await db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == code, Token);
            if (role is null)
            {
                role = new Role(code, code.ToString());
                db.Roles.Add(role);
            }

            var user = new User(role.Id, name, "hash", $"{tag}-{name.Replace(' ', '-')}@example.test", null);
            db.Users.Add(user);

            return user;
        }

        var farmerUser = await Actor(RoleCode.Farmer, "Farmer One");
        var otherFarmerUser = await Actor(RoleCode.Farmer, "Farmer Two");
        var staffUser = await Actor(RoleCode.SalesStaff, "Staff");
        var farmer = new FarmerProfile(farmerUser.Id);
        var otherFarmer = new FarmerProfile(otherFarmerUser.Id);
        db.FarmerProfiles.AddRange(farmer, otherFarmer);

        var store = await db.Stores.SingleOrDefaultAsync(s => s.Status == StoreStatus.Active, Token);
        if (store is null)
        {
            store = new Store($"S-{tag}", "Test", "Address", "Province");
            db.Stores.Add(store);
        }

        // A sales member WITHOUT the old review flag: since 2.0 nobody needs it.
        var staff = new StoreMember(store.Id, staffUser.Id);
        db.StoreMembers.Add(staff);

        var diseases = new Dictionary<string, Guid>();
        foreach (var code in Codes)
        {
            var disease = await db.Diseases.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Code == code, Token);
            if (disease is null)
            {
                disease = new Disease(code, code, isHealthyClass: code == "HEALTHY");
                db.Diseases.Add(disease);
            }

            diseases[code] = disease.Id;
        }

        // Only the model of this test may be ACTIVE: any other one on the development database is retired in the
        // (rolled-back) transaction.
        foreach (var active in await db.AiModels.Where(m => m.Status == AiModelStatus.Active).ToListAsync(Token))
        {
            active.Retire(DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        Guid modelId = Guid.Empty, policyId = Guid.Empty;
        if (withModel)
        {
            var model = new AiModel(
                $"test-model-{tag}", "0.1.0", "pytorch", "https://example.test/model", JsonSerializer.Serialize(Codes), staffUser.Id,
                "mobilenet_v2", 224, 224);
            model.Activate(DateTimeOffset.UtcNow.AddMinutes(-5));
            var policy = new AiPolicyConfig(
                model.Id, "p1", 0.80m, DateTimeOffset.UtcNow.AddDays(-1), staffUser.Id, topK: 3, minimumMargin: 0.15m);
            policy.Activate();
            db.AiModels.Add(model);
            db.AiPolicyConfigs.Add(policy);
            modelId = model.Id;
            policyId = policy.Id;
        }

        await db.SaveChangesAsync(Token);

        return new Env(
            session, new FakeFiles(), new FakeAi(), farmerUser.Id, farmer.Id, otherFarmerUser.Id, staffUser.Id, staff.Id,
            store.Id, modelId, policyId, diseases, tag);
    }

    // A chemical treatment of Leaf Blast with a new active ingredient and products around it: one the Farmer can buy, and
    // five that must never be suggested.
    private static async Task<Catalog> LeafBlastCatalogAsync(Env env)
    {
        await using var db = env.Session.NewContext();
        var ingredient = new ActiveIngredient($"Tricyclazole {env.Tag}", $"I-{env.Tag}");
        var category = new Category($"C-{env.Tag}", "Test");
        db.AddRange(ingredient, category);

        // Priority 1000 (the highest a treatment may have) keeps these first among any real content of the disease.
        var chemical = new DiseaseTreatment(
            env.Diseases["LEAF_BLAST"], TreatmentType.Chemical, $"Phun thuốc {env.Tag}", "Theo nhãn.", ingredient.Id,
            "Đeo khẩu trang.", priority: 1000);
        var cultural = new DiseaseTreatment(
            env.Diseases["LEAF_BLAST"], TreatmentType.Cultural, $"Giảm đạm {env.Tag}", "Bón cân đối.", priority: 999);
        db.AddRange(chemical, cultural);

        StoreProduct Sold(string name, Guid storeId, ProductStatus status = ProductStatus.Active, bool withIngredient = true)
        {
            var product = new Product(category.Id, $"P-{env.Tag}-{name}", $"{name} {env.Tag}", status: status);
            var storeProduct = new StoreProduct(storeId, product.Id);
            db.AddRange(product, storeProduct);
            if (withIngredient)
            {
                db.Add(new ProductActiveIngredient(product.Id, ingredient.Id, "75WP"));
            }

            return storeProduct;
        }

        var suggested = Sold("Beam", env.StoreId);
        var notSellable = Sold("NotSellable", env.StoreId);
        notSellable.MarkNotSellable();
        var inactiveProduct = Sold("Inactive", env.StoreId, ProductStatus.Inactive);
        var otherStore = new Store($"S2-{env.Tag}", "Other", "Address", "Province", status: StoreStatus.Inactive);
        db.Add(otherStore);
        var elsewhere = Sold("Elsewhere", otherStore.Id);
        var deletedLink = Sold("DeletedLink", env.StoreId);
        var noIngredient = Sold("NoIngredient", env.StoreId, withIngredient: false);
        await db.SaveChangesAsync(Token);

        var link = await db.ProductActiveIngredients.SingleAsync(l => l.ProductId == deletedLink.ProductId, Token);
        db.Remove(link); // soft delete
        await db.SaveChangesAsync(Token);

        return new Catalog(
            chemical.Id, cultural.Id, suggested.Id, notSellable.Id, inactiveProduct.Id, elsewhere.Id, deletedLink.Id, noIngredient.Id);
    }

    [RealDbFact]
    public async Task A_confident_result_is_shown_at_once_with_treatments_and_products_matched_by_active_ingredient()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        var catalog = await LeafBlastCatalogAsync(env);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.912345m, others: [("BROWN_SPOT", 0.06m), ("HEALTHY", 0.02m)]);

        // 1. The Farmer sends a photo: the answer already carries the result.
        var created = await env.Submit();
        Assert.Matches(@"^DG-\d{8}-\d{4}$", created.CaseNumber);
        Assert.Equal("AI_COMPLETED", created.Status);
        Assert.False(created.CanRetry);
        Assert.Null(created.Inconclusive);
        Assert.StartsWith("https://storage.test/signed/", created.Image!.ImageUrl);
        Assert.Equal(1, env.Ai.Calls);

        var result = created.Result!;
        Assert.Equal("CONFIDENT", result.Certainty);
        Assert.Null(result.Hint);
        Assert.Null(result.ReviewerComment);
        Assert.Equal("LEAF_BLAST", result.Disease.Code);
        var chemical = Assert.Single(result.Treatments, t => t.Id == catalog.ChemicalId);
        Assert.Equal("CHEMICAL", chemical.Type);
        Assert.Equal($"Tricyclazole {env.Tag}", chemical.ActiveIngredientName);
        Assert.Contains(result.Treatments, t => t.Id == catalog.CulturalId);

        // Only the product the Farmer can buy in this store, and its reason names the ingredient.
        var mine = result.Products.Where(p => catalog.All.Contains(p.StoreProductId)).ToList();
        var product = Assert.Single(mine);
        Assert.Equal(catalog.Suggested, product.StoreProductId);
        Assert.Equal($"Chứa hoạt chất Tricyclazole {env.Tag}", product.Reason);
        Assert.True(result.Products.Count <= 5);

        // 2. The store sees the evidence and exactly what the Farmer saw.
        var detail = await env.Staff(s => s.GetAsync(created.Id, Token));
        var inference = Assert.Single(detail.Inferences);
        Assert.Equal(0.912345m, inference.Confidence);
        Assert.Equal(0.852345m, inference.Margin);
        Assert.True(inference.PassedPolicy);
        Assert.Equal(3, inference.TopPredictions.Count);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(detail.FarmerResult));
        Assert.Null(detail.CurrentReview);
        Assert.Empty(detail.Recommendations);

        // 3. The list shows the disease and the certainty.
        var item = Assert.Single((await env.Farmer(s => s.ListAsync(new MyDiagnosisCaseListRequest(), Token))).Items);
        Assert.Equal("LEAF_BLAST", item.DiseaseCode);
        Assert.Equal("CONFIDENT", item.Certainty);

        // 4. What was suggested is in the audit; the Farmer gets one alert, however often the worker runs.
        await using var db = session.NewContext();
        var audit = await db.AuditLogs.SingleAsync(a => a.EntityId == created.Id && a.Action == "DIAGNOSIS_AI_RESULT", Token);
        Assert.Contains(catalog.Suggested.ToString(), audit.NewValues);
        Assert.Contains("CONFIDENT", audit.NewValues);

        var collector = new CommittedNotificationService(db, new NotificationWriter(db));
        await collector.DiagnosisAsync(100, Token);
        await collector.DiagnosisAsync(100, Token);
        var alert = await db.Notifications.SingleAsync(
            n => n.UserId == env.FarmerUserId && n.DeduplicationKey!.StartsWith("diagnosis"), Token);
        Assert.Equal("AI_DIAGNOSIS_COMPLETED", alert.NotificationType);
        Assert.Contains("xem bệnh và cách xử lý", alert.Message);
    }

    [RealDbFact]
    public async Task The_farmer_never_sees_a_probability_or_a_model_and_not_other_farmers_cases()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("BROWN_SPOT", 0.951234m, others: [("LEAF_BLAST", 0.03m)]);

        var created = await env.Submit();

        var json = JsonSerializer.Serialize(created);
        Assert.Contains("BROWN_SPOT", json);
        Assert.DoesNotContain("0.95", json);
        Assert.DoesNotContain("agrisage-rice-classifier", json);
        Assert.DoesNotContain("test-model", json);

        await Assert.ThrowsAsync<NotFoundException>(() => env.Farmer(s => s.GetAsync(created.Id, Token), env.OtherFarmerUserId));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Farmer(
            s => s.CancelAsync(created.Id, new CancelDiagnosisRequest(), Token), env.OtherFarmerUserId));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Farmer(s => s.RetryAsync(created.Id, Token), env.OtherFarmerUserId));
        Assert.Empty((await env.Farmer(s => s.ListAsync(new MyDiagnosisCaseListRequest(), Token), env.OtherFarmerUserId)).Items);
    }

    [RealDbFact]
    public async Task Below_the_policy_the_result_is_uncertain_without_chemical_treatment_or_products()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        var catalog = await LeafBlastCatalogAsync(env);

        // Confident but too close to the second class: margin 0.05 < 0.15.
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.85m, others: [("BROWN_SPOT", 0.80m)]);
        var narrow = await env.Submit();
        // Not confident enough: 0.60 < 0.80.
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.60m, others: [("BROWN_SPOT", 0.10m)]);
        var weak = await env.Submit();

        foreach (var created in new[] { narrow, weak })
        {
            Assert.Equal("AI_COMPLETED", created.Status);
            var result = created.Result!;
            Assert.Equal("UNCERTAIN", result.Certainty);
            Assert.Equal("RETAKE_PHOTO_OR_ASK_STORE", result.Hint);
            Assert.Equal("LEAF_BLAST", result.Disease.Code);
            Assert.DoesNotContain(result.Treatments, t => t.Type == "CHEMICAL");
            Assert.Contains(result.Treatments, t => t.Id == catalog.CulturalId);
            Assert.Empty(result.Products);
        }

        var failed = await env.Staff(s => s.ListAsync(new DiagnosisCaseListRequest { AiPassed = false }, Token));
        Assert.Contains(failed.Items, i => i.Id == narrow.Id);
        Assert.Contains(failed.Items, i => i.Id == weak.Id);
        var passed = await env.Staff(s => s.ListAsync(new DiagnosisCaseListRequest { AiPassed = true }, Token));
        Assert.DoesNotContain(passed.Items, i => i.Id == narrow.Id || i.Id == weak.Id);

        var item = (await env.Farmer(s => s.ListAsync(new MyDiagnosisCaseListRequest(), Token))).Items.First(i => i.Id == weak.Id);
        Assert.Equal("UNCERTAIN", item.Certainty);

        await using var db = session.NewContext();
        await new CommittedNotificationService(db, new NotificationWriter(db)).DiagnosisAsync(100, Token);
        Assert.All(
            await db.Notifications.Where(n => n.UserId == env.FarmerUserId).ToListAsync(Token),
            n => Assert.Contains("AI chưa chắc chắn", n.Message));
    }

    [RealDbFact]
    public async Task A_healthy_leaf_gets_care_tips_but_no_product()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("HEALTHY", 0.97m, others: [("BROWN_SPOT", 0.01m)]);

        var created = await env.Submit();

        Assert.Equal("CONFIDENT", created.Result!.Certainty);
        Assert.True(created.Result.Disease.IsHealthy);
        Assert.Empty(created.Result.Products);
    }

    [RealDbFact]
    public async Task When_the_ai_is_down_the_case_fails_and_the_farmer_retries_it()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = null; // unreachable

        var created = await env.Submit();

        Assert.Equal("FAILED", created.Status);
        Assert.Null(created.Result);
        Assert.True(created.CanRetry);
        var detail = await env.Staff(s => s.GetAsync(created.Id, Token));
        var inference = Assert.Single(detail.Inferences);
        Assert.Equal("FAILED", inference.Status);
        Assert.Equal("AI_SERVICE_UNAVAILABLE", inference.Error);
        Assert.Null(detail.FarmerResult);

        // Still down: the retry reports it (503) and records nothing, so the failed attempts do not pile up.
        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => env.Farmer(s => s.RetryAsync(created.Id, Token)));
        var unchanged = await env.Staff(s => s.GetAsync(created.Id, Token));
        Assert.Equal("FAILED", unchanged.Status);
        Assert.Single(unchanged.Inferences);

        // The service is back: the retry completes the case; the failed attempt stays as history.
        env.Ai.Answer = _ => Answer("SHEATH_BLIGHT", 0.9m, others: [("HEALTHY", 0.05m)]);
        var again = await env.Farmer(s => s.RetryAsync(created.Id, Token));

        Assert.Equal("AI_COMPLETED", again.Status);
        Assert.Equal("CONFIDENT", again.Result!.Certainty);
        Assert.Equal("SHEATH_BLIGHT", again.Result.Disease.Code);
        Assert.False(again.CanRetry);
        Assert.Equal(["FAILED", "SUCCESS"], (await env.Staff(s => s.GetAsync(created.Id, Token))).Inferences.Select(i => i.Status));

        // A case that has a result cannot be retried.
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Farmer(s => s.RetryAsync(created.Id, Token)));

        await using var db = session.NewContext();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == created.Id && a.Action == "DIAGNOSIS_AI_RETRIED", Token));
    }

    [RealDbFact]
    public async Task Without_an_active_model_the_case_fails_and_nothing_is_recorded_against_a_model()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session, withModel: false);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m);

        var created = await env.Submit();

        Assert.Equal("FAILED", created.Status);
        Assert.True(created.CanRetry);
        Assert.Equal(0, env.Ai.Calls);
        var detail = await env.Staff(s => s.GetAsync(created.Id, Token));
        Assert.Empty(detail.Inferences);
    }

    [RealDbFact]
    public async Task An_answer_from_another_model_version_or_a_stub_or_an_unknown_class_is_not_a_result()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);

        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, version: "9.9.9");
        var mismatch = await env.Submit();

        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, stub: true);
        var stub = await env.Submit();

        env.Ai.Answer = _ => Answer("RICE_HISPA", 0.9m);
        var unknown = await env.Submit();

        foreach (var (created, error) in new[]
                 {
                     (mismatch, "MODEL_VERSION_MISMATCH"), (stub, "STUB_REFUSED"), (unknown, "UNKNOWN_CLASS_LABEL")
                 })
        {
            Assert.Equal("FAILED", created.Status);
            Assert.Null(created.Result);
            var detail = await env.Staff(s => s.GetAsync(created.Id, Token));
            var inference = Assert.Single(detail.Inferences);
            Assert.Equal("FAILED", inference.Status);
            Assert.Equal(error, inference.Error);
        }

        // In Development a simulated answer is accepted.
        env.Options = Options.Create(new AiDiagnosisOptions { AllowStubResults = true });
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, stub: true);
        var allowed = await env.Submit();
        Assert.Equal("AI_COMPLETED", allowed.Status);
    }

    [RealDbFact]
    public async Task Store_staff_read_cases_newest_first_without_any_review_right()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, others: [("BROWN_SPOT", 0.05m)]);
        var first = await env.Submit("TEST- first");
        var second = await env.Submit("TEST- second");

        var page = await env.Staff(s => s.ListAsync(new DiagnosisCaseListRequest(), Token));
        var ours = page.Items.Where(i => i.Id == first.Id || i.Id == second.Id).Select(i => i.Id).ToList();
        Assert.Equal([second.Id, first.Id], ours);

        var detail = await env.Staff(s => s.GetAsync(first.Id, Token), role: "STORE_OWNER");
        Assert.Equal("AI_COMPLETED", detail.Status);
        Assert.Equal("CONFIDENT", detail.FarmerResult!.Certainty);
    }

    [RealDbFact]
    public async Task Cases_decided_before_human_review_was_removed_stay_readable()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, others: [("BROWN_SPOT", 0.05m)]);
        var verified = await env.Submit();
        var inconclusive = await env.Submit();
        var underReview = await env.Submit();

        // The 1.0 flow, through the Domain methods that are kept for these cases.
        await using (var db = session.NewContext())
        {
            async Task<DiagnosisCase> Load(Guid id) => await db.DiagnosisCases
                .Include(c => c.Images).Include(c => c.Inferences).Include(c => c.Reviews).Include(c => c.Recommendations)
                .SingleAsync(c => c.Id == id, Token);

            var brownSpot = await db.Diseases.SingleAsync(d => d.Id == env.Diseases["BROWN_SPOT"], Token);
            var treatment = new DiseaseTreatment(brownSpot.Id, TreatmentType.Cultural, $"Old guidance {env.Tag}", "Kept.");
            db.Add(treatment);

            var a = await Load(verified.Id);
            a.Review(env.StaffMemberId, AgentReviewDecision.Corrected, DateTimeOffset.UtcNow, brownSpot,
                a.Inferences.Single().Id, "Chẩn đoán của chuyên gia.");
            await db.SaveChangesAsync(Token);
            a.AddRecommendation(RecommendationType.Treatment, brownSpot, env.StaffUserId, DateTimeOffset.UtcNow, treatment, rankOrder: 1);

            var b = await Load(inconclusive.Id);
            b.Review(env.StaffMemberId, AgentReviewDecision.Inconclusive, DateTimeOffset.UtcNow, comment: "Ảnh bị mờ.");

            (await Load(underReview.Id)).StartReview();
            await db.SaveChangesAsync(Token);
        }

        var a2 = await env.Farmer(s => s.GetAsync(verified.Id, Token));
        Assert.Equal("VERIFIED", a2.Status);
        Assert.Equal("VERIFIED", a2.Result!.Certainty);
        Assert.Equal("BROWN_SPOT", a2.Result.Disease.Code);
        Assert.Equal("Chẩn đoán của chuyên gia.", a2.Result.ReviewerComment);
        Assert.Single(a2.Result.Treatments);

        var b2 = await env.Farmer(s => s.GetAsync(inconclusive.Id, Token));
        Assert.Equal("INCONCLUSIVE", b2.Status);
        Assert.Null(b2.Result);
        Assert.Equal("RETAKE_PHOTO", b2.Inconclusive!.Hint);
        Assert.Equal("Ảnh bị mờ.", b2.Inconclusive.Comment);

        var c2 = await env.Farmer(s => s.GetAsync(underReview.Id, Token));
        Assert.Equal("UNDER_REVIEW", c2.Status);
        Assert.Equal("CONFIDENT", c2.Result!.Certainty);

        var list = (await env.Farmer(s => s.ListAsync(new MyDiagnosisCaseListRequest(), Token))).Items;
        Assert.Equal("BROWN_SPOT", list.Single(i => i.Id == verified.Id).DiseaseCode);
        Assert.Equal("VERIFIED", list.Single(i => i.Id == verified.Id).Certainty);
        Assert.Null(list.Single(i => i.Id == inconclusive.Id).DiseaseCode);

        var detail = await env.Staff(s => s.GetAsync(verified.Id, Token));
        Assert.Equal("CORRECTED", detail.CurrentReview!.Decision);
        Assert.Single(detail.Recommendations);
    }

    [RealDbFact]
    public async Task A_farmer_can_cancel_a_case_and_bad_photos_are_refused()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, others: [("BROWN_SPOT", 0.05m)]);
        var created = await env.Submit();

        var cancelled = await env.Farmer(s => s.CancelAsync(created.Id, new CancelDiagnosisRequest("Gửi nhầm ảnh"), Token));
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Null(cancelled.Result);
        await Assert.ThrowsAsync<DomainException>(() => env.Farmer(
            s => s.CancelAsync(created.Id, new CancelDiagnosisRequest(), Token)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Farmer(s => s.RetryAsync(created.Id, Token)));

        // Not an image, too large and empty files are refused before anything is stored.
        await Assert.ThrowsAsync<ValidationException>(() => env.Farmer(
            s => s.CreateAsync(new MemoryStream("not an image"u8.ToArray()), "x.txt", null, Token)));
        await Assert.ThrowsAsync<ValidationException>(() => env.Farmer(
            s => s.CreateAsync(new MemoryStream([]), "x.jpg", null, Token)));
        await Assert.ThrowsAsync<ValidationException>(() => env.Farmer(
            s => s.CreateAsync(new MemoryStream(Photo()), "x.jpg", new string('a', 1001), Token)));
        Assert.Single(env.Files.Objects);
    }

    [RealDbFact]
    public async Task A_photo_that_cannot_be_signed_is_left_out_instead_of_failing_the_case()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, others: [("BROWN_SPOT", 0.05m)]);
        var created = await env.Submit();

        env.Files.SigningFails = true;
        var mine = await env.Farmer(s => s.GetAsync(created.Id, Token));
        var detail = await env.Staff(s => s.GetAsync(created.Id, Token));

        Assert.Null(mine.Image);
        Assert.Empty(detail.Images);
        Assert.Equal("AI_COMPLETED", mine.Status);
        Assert.NotNull(mine.Result);
    }

    [RealDbFact]
    public async Task Models_have_one_active_version_and_policies_never_overlap()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session, withModel: false);
        var manifest = new AiModelRequest(
            $"m-{env.Tag}", "1.0.0", "pytorch", "https://example.test/m1", Codes, "mobilenet_v2", 224, 224,
            JsonDocument.Parse("""{"testMacroF1":0.92}""").RootElement.Clone());

        // The class labels must be exactly the five disease codes.
        await Assert.ThrowsAsync<ValidationException>(() => env.Models(s => s.CreateAsync(
            manifest with { ClassLabels = Codes.Take(4).ToList() }, Token)));
        await Assert.ThrowsAsync<ValidationException>(() => env.Models(s => s.CreateAsync(
            manifest with { ClassLabels = [.. Codes.Take(4), "RICE_HISPA"] }, Token)));

        var first = await env.Models(s => s.CreateAsync(manifest, Token));
        Assert.Equal("DRAFT", first.Status);
        Assert.Equal(0.92, first.Metrics!.Value.GetProperty("testMacroF1").GetDouble());
        await Assert.ThrowsAsync<ConflictException>(() => env.Models(s => s.CreateAsync(manifest, Token)));
        var second = await env.Models(s => s.CreateAsync(manifest with { Version = "1.1.0" }, Token));

        // A policy needs an ACTIVE model.
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var policy1 = await env.Models(s => s.CreatePolicyAsync(first.Id, new AiPolicyRequest("a", 0.8m, from), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Models(s => s.ActivatePolicyAsync(policy1.Id, Token)));

        Assert.Equal("ACTIVE", (await env.Models(s => s.ActivateAsync(first.Id, Token))).Status);
        Assert.Equal("ACTIVE", (await env.Models(s => s.ActivatePolicyAsync(policy1.Id, Token))).Status);

        // Overlapping periods are refused; a period that starts when the other one ends is fine.
        var overlapping = await env.Models(s => s.CreatePolicyAsync(first.Id, new AiPolicyRequest("b", 0.7m, from.AddDays(1)), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Models(s => s.ActivatePolicyAsync(overlapping.Id, Token)));
        await Assert.ThrowsAsync<ConflictException>(() => env.Models(s => s.CreatePolicyAsync(first.Id, new AiPolicyRequest("a", 0.8m, from), Token)));
        await env.Models(s => s.DeactivatePolicyAsync(policy1.Id, Token));
        Assert.Equal("ACTIVE", (await env.Models(s => s.ActivatePolicyAsync(overlapping.Id, Token))).Status);

        // Activating another model retires the first: one model answers at a time.
        Assert.Equal("ACTIVE", (await env.Models(s => s.ActivateAsync(second.Id, Token))).Status);
        Assert.Equal("RETIRED", (await env.Models(s => s.GetAsync(first.Id, Token))).Status);
        var active = await env.Models(s => s.ListAsync(new AiModelListRequest { Status = "ACTIVE" }, Token));
        Assert.Equal(second.Id, Assert.Single(active.Items).Id);
    }

    [RealDbFact]
    public async Task Disease_content_and_treatments_can_be_edited_and_deactivated()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.As(env.StaffUserId, "STORE_OWNER");
        await using var db = session.NewContext();
        var service = new DiseaseService(db);
        var id = env.Diseases["LEAF_BLAST"];

        var updated = await service.UpdateAsync(
            id, new DiseaseUpdateRequest("Bệnh đạo ôn", "Pyricularia oryzae", Symptoms: "Vết hình thoi."), Token);
        Assert.Equal("Bệnh đạo ôn", updated.Name);
        Assert.Equal("LEAF_BLAST", updated.Code);

        var treatment = await service.CreateTreatmentAsync(
            id, new TreatmentRequest("CHEMICAL", "Phun thuốc", "Theo nhãn.", Priority: 3), Token);
        Assert.True(treatment.IsActive);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTreatmentAsync(
            id, new TreatmentRequest("MAGIC", "x", "y"), Token));

        // An inactive ingredient would never match a product, so it is refused.
        var inactive = new ActiveIngredient($"Old {env.Tag}");
        inactive.Deactivate();
        db.Add(inactive);
        await db.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTreatmentAsync(
            id, new TreatmentRequest("CHEMICAL", "x", "y", inactive.Id), Token));

        var off = await service.SetTreatmentActiveAsync(treatment.Id, false, Token);
        Assert.False(off.IsActive);
        var listed = (await service.ListAsync(true, Token)).Single(d => d.Id == id);
        Assert.Contains(listed.Treatments, t => t.Id == treatment.Id && !t.IsActive);
    }
}
