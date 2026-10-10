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
        Guid ReviewerUserId,
        Guid ReviewerMemberId,
        Guid OutsiderUserId,
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
            var runner = new DiagnosisAiRunner(db, Ai, clock, Options, NullLogger<DiagnosisAiRunner>.Instance);
            var urls = new DiagnosisImageUrls(Files, clock, NullLogger<DiagnosisImageUrls>.Instance);
            var service = new MyDiagnosisCaseService(
                db, new CurrentFarmer(db, Session.CurrentUser), Files, clock, runner, urls,
                new AuditTrail(db, Session.CurrentUser, clock), NullLogger<MyDiagnosisCaseService>.Instance);

            return await call(service);
        }

        public async Task<T> Reviewer<T>(Func<IDiagnosisCaseService, Task<T>> call, Guid? userId = null, string role = "SALES_STAFF")
        {
            As(userId ?? ReviewerUserId, role);
            await using var db = Session.NewContext();
            var clock = new Clock();
            var runner = new DiagnosisAiRunner(db, Ai, clock, Options, NullLogger<DiagnosisAiRunner>.Instance);
            var urls = new DiagnosisImageUrls(Files, clock, NullLogger<DiagnosisImageUrls>.Instance);
            var service = new DiagnosisCaseService(
                db, Session.CurrentUser, clock, new AiReviewer(db, Session.CurrentUser), runner, urls, Files,
                new AuditTrail(db, Session.CurrentUser, clock));

            return await call(service);
        }

        public async Task<T> Models<T>(Func<IAiModelService, Task<T>> call)
        {
            As(ReviewerUserId, "STORE_OWNER");
            await using var db = Session.NewContext();
            var clock = new Clock();

            return await call(new AiModelService(db, Session.CurrentUser, clock, new AuditTrail(db, Session.CurrentUser, clock)));
        }

        public Task<MyDiagnosisCaseResponse> Submit(string? note = "Lá vàng từ chóp") =>
            Farmer(s => s.CreateAsync(new MemoryStream(Photo()), "leaf.jpg", note, Token));
    }

    private static byte[] Photo() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 1, 2, 3, 4, 5, 6, 7, 8];

    private static AiPredictionResult Answer(
        string label, decimal confidence, string version = "0.1.0", bool stub = false, params (string, decimal)[] others)
    {
        var scores = new List<AiClassScore> { new(label, confidence) };
        scores.AddRange(others.Select(o => new AiClassScore(o.Item1, o.Item2)));

        return new AiPredictionResult(label, confidence, scores, version, 12, confidence, "agrisage-rice-classifier", stub);
    }

    private static async Task<Env> PrepareAsync(RealDb.Session session, bool withModel = true, bool reviewerMayReview = true)
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
        var reviewerUser = await Actor(RoleCode.SalesStaff, "Reviewer");
        var outsiderUser = await Actor(RoleCode.SalesStaff, "Outsider");
        var farmer = new FarmerProfile(farmerUser.Id);
        var otherFarmer = new FarmerProfile(otherFarmerUser.Id);
        db.FarmerProfiles.AddRange(farmer, otherFarmer);

        var store = await db.Stores.SingleOrDefaultAsync(s => s.Status == StoreStatus.Active, Token);
        if (store is null)
        {
            store = new Store($"S-{tag}", "Test", "Address", "Province");
            db.Stores.Add(store);
        }

        var reviewer = new StoreMember(store.Id, reviewerUser.Id);
        if (reviewerMayReview)
        {
            reviewer.GrantAiReview();
        }

        db.StoreMembers.AddRange(reviewer, new StoreMember(store.Id, outsiderUser.Id));

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
                $"test-model-{tag}", "0.1.0", "pytorch", "https://example.test/model", JsonSerializer.Serialize(Codes), reviewerUser.Id,
                "mobilenet_v2", 224, 224);
            model.Activate(DateTimeOffset.UtcNow.AddMinutes(-5));
            var policy = new AiPolicyConfig(
                model.Id, "p1", 0.80m, DateTimeOffset.UtcNow.AddDays(-1), reviewerUser.Id, topK: 3, minimumMargin: 0.15m);
            policy.Activate();
            db.AiModels.Add(model);
            db.AiPolicyConfigs.Add(policy);
            modelId = model.Id;
            policyId = policy.Id;
        }

        await db.SaveChangesAsync(Token);

        return new Env(
            session, new FakeFiles(), new FakeAi(), farmerUser.Id, farmer.Id, otherFarmerUser.Id, reviewerUser.Id, reviewer.Id,
            outsiderUser.Id, store.Id, modelId, policyId, diseases, tag);
    }

    [RealDbFact]
    public async Task A_case_goes_from_the_photo_to_a_verified_result_with_recommendations()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.912345m, others: [("BROWN_SPOT", 0.06m), ("HEALTHY", 0.02m)]);

        // 1. The Farmer sends a photo: the AI runs, the case waits for a reviewer, nothing is revealed.
        var created = await env.Submit();
        Assert.Matches(@"^DG-\d{8}-\d{4}$", created.CaseNumber);
        Assert.Equal("AI_COMPLETED", created.Status);
        Assert.Null(created.Result);
        Assert.Null(created.Inconclusive);
        Assert.NotNull(created.Image);
        Assert.StartsWith("https://storage.test/signed/", created.Image!.ImageUrl);
        Assert.Equal(1, env.Ai.Calls);
        Assert.Single(env.Files.Objects);

        // 2. The reviewer sees the evidence the Farmer does not.
        var detail = await env.Reviewer(s => s.GetAsync(created.Id, Token));
        var inference = Assert.Single(detail.Inferences);
        Assert.Equal("SUCCESS", inference.Status);
        Assert.Equal("LEAF_BLAST", inference.PredictedClassLabel);
        Assert.Equal(0.912345m, inference.Confidence);
        Assert.Equal(0.852345m, inference.Margin);
        Assert.True(inference.PassedPolicy);
        Assert.Equal(0.80m, inference.Policy.MinimumConfidence);
        Assert.Equal(3, inference.TopPredictions.Count);
        Assert.Equal(env.Diseases["LEAF_BLAST"], inference.PredictedDiseaseId);
        Assert.Equal($"test-model-{env.Tag}", inference.Model.Name);
        Assert.Equal("0.1.0", inference.Model.Version);

        // 3. Review: the queue shows it, then CONFIRMED verifies it.
        var queue = await env.Reviewer(s => s.ListAsync(new DiagnosisCaseListRequest { Status = "AI_COMPLETED", Search = created.CaseNumber }, Token));
        var row = Assert.Single(queue.Items);
        Assert.Equal(created.Id, row.Id);
        Assert.True(row.PassedPolicy);

        var started = await env.Reviewer(s => s.StartReviewAsync(created.Id, Token));
        Assert.Equal("UNDER_REVIEW", started.Status);

        var reviewed = await env.Reviewer(s => s.ReviewAsync(
            created.Id, new ReviewRequest("CONFIRMED", env.Diseases["LEAF_BLAST"], Comment: "Vết hình thoi điển hình."), Token));
        Assert.Equal("VERIFIED", reviewed.Status);
        Assert.Equal("CONFIRMED", reviewed.CurrentReview!.Decision);
        Assert.Equal("LEAF_BLAST", reviewed.FinalDisease!.Code);
        Assert.Equal(inference.Id, reviewed.CurrentReview.PrimaryAiInferenceId);
        Assert.Equal(env.ReviewerMemberId, reviewed.CurrentReview.ReviewerMemberId);

        // 4. Recommendations: a treatment and a product.
        await using (var db = session.NewContext())
        {
            var category = new Category($"C-{env.Tag}", "Test");
            var product = new Product(category.Id, $"P-{env.Tag}", "Thuốc đạo ôn thử nghiệm");
            var storeProduct = new StoreProduct(env.StoreId, product.Id);
            var treatment = new DiseaseTreatment(
                env.Diseases["LEAF_BLAST"], TreatmentType.Cultural, "Giảm đạm", "Bón cân đối.", precautions: "Đeo găng.", priority: 5);
            db.AddRange(category, product, storeProduct, treatment);
            await db.SaveChangesAsync(Token);

            var treatmentRec = await env.Reviewer(s => s.RecommendAsync(
                created.Id, new RecommendationRequest("TREATMENT", DiseaseTreatmentId: treatment.Id, RankOrder: 1, Reason: "Bước đầu"), Token));
            var productRec = await env.Reviewer(s => s.RecommendAsync(
                created.Id, new RecommendationRequest("PRODUCT", StoreProductId: storeProduct.Id, RankOrder: 2), Token));
            Assert.Equal("TREATMENT", treatmentRec.Type);
            Assert.Equal("Thuốc đạo ôn thử nghiệm", productRec.ProductName);

            await Assert.ThrowsAsync<ConflictException>(() => env.Reviewer(s => s.RecommendAsync(
                created.Id, new RecommendationRequest("PRODUCT", StoreProductId: storeProduct.Id), Token)));
        }

        // 5. The review writes no notification of its own; the committed-notification worker tells the Farmer once, and
        //    running it again changes nothing. Then the Farmer sees the verified result with the guidance.
        await using (var db = session.NewContext())
        {
            Assert.False(await db.Notifications.AnyAsync(
                n => n.UserId == env.FarmerUserId && n.DeduplicationKey!.StartsWith("diagnosis"), Token));

            var collector = new CommittedNotificationService(db, new NotificationWriter(db));
            await collector.DiagnosisAsync(100, Token);
            await collector.DiagnosisAsync(100, Token);

            var types = await db.Notifications
                .Where(n => n.UserId == env.FarmerUserId && n.DeduplicationKey!.StartsWith("diagnosis"))
                .Select(n => n.NotificationType).ToListAsync(Token);
            Assert.Equal(["AI_DIAGNOSIS_COMPLETED", "DIAGNOSIS_RECOMMENDATIONS", "DIAGNOSIS_REVIEWED"], types.Order().ToArray());
        }

        var mine = await env.Farmer(s => s.GetAsync(created.Id, Token));
        Assert.Equal("VERIFIED", mine.Status);
        Assert.Equal("LEAF_BLAST", mine.Result!.Disease.Code);
        Assert.Equal("Vết hình thoi điển hình.", mine.Result.ReviewerComment);
        Assert.Single(mine.Result.Treatments);
        Assert.Single(mine.Result.Products);

        var list = await env.Farmer(s => s.ListAsync(new MyDiagnosisCaseListRequest(), Token));
        Assert.Equal("LEAF_BLAST", Assert.Single(list.Items).DiseaseCode);
    }

    [RealDbFact]
    public async Task The_farmer_never_sees_the_ai_result_before_a_review_and_not_other_farmers_cases()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("BROWN_SPOT", 0.95m, others: [("LEAF_BLAST", 0.03m)]);

        var created = await env.Submit();

        var json = JsonSerializer.Serialize(created);
        Assert.DoesNotContain("0.95", json);
        Assert.DoesNotContain("BROWN_SPOT", json);
        Assert.DoesNotContain("agrisage-rice-classifier", json);
        var list = await env.Farmer(s => s.ListAsync(new MyDiagnosisCaseListRequest(), Token));
        Assert.Null(Assert.Single(list.Items).DiseaseCode);

        await Assert.ThrowsAsync<NotFoundException>(() => env.Farmer(s => s.GetAsync(created.Id, Token), env.OtherFarmerUserId));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Farmer(
            s => s.CancelAsync(created.Id, new CancelDiagnosisRequest(), Token), env.OtherFarmerUserId));
        Assert.Empty((await env.Farmer(s => s.ListAsync(new MyDiagnosisCaseListRequest(), Token), env.OtherFarmerUserId)).Items);
    }

    [RealDbFact]
    public async Task A_confidence_or_margin_below_the_policy_still_goes_to_review_without_passing()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);

        // Confident but too close to the second class: margin 0.05 < 0.15.
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.85m, others: [("BROWN_SPOT", 0.80m)]);
        var narrow = await env.Submit();
        // Not confident enough: 0.60 < 0.80.
        env.Ai.Answer = _ => Answer("HEALTHY", 0.60m, others: [("BROWN_SPOT", 0.10m)]);
        var weak = await env.Submit();

        foreach (var created in new[] { narrow, weak })
        {
            Assert.Equal("AI_COMPLETED", created.Status);
            var detail = await env.Reviewer(s => s.GetAsync(created.Id, Token));
            Assert.False(Assert.Single(detail.Inferences).PassedPolicy);
        }

        var failed = await env.Reviewer(s => s.ListAsync(new DiagnosisCaseListRequest { AiPassed = false }, Token));
        Assert.Contains(failed.Items, i => i.Id == narrow.Id);
        Assert.Contains(failed.Items, i => i.Id == weak.Id);
        var passed = await env.Reviewer(s => s.ListAsync(new DiagnosisCaseListRequest { AiPassed = true }, Token));
        Assert.DoesNotContain(passed.Items, i => i.Id == narrow.Id || i.Id == weak.Id);
    }

    [RealDbFact]
    public async Task When_the_ai_is_down_the_case_fails_and_is_run_again_by_a_reviewer()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = null; // unreachable

        var created = await env.Submit();

        Assert.Equal("FAILED", created.Status);
        Assert.Null(created.Result);
        var detail = await env.Reviewer(s => s.GetAsync(created.Id, Token));
        var inference = Assert.Single(detail.Inferences);
        Assert.Equal("FAILED", inference.Status);
        Assert.Equal("AI_SERVICE_UNAVAILABLE", inference.Error);
        Assert.False(inference.PassedPolicy);

        // Still down: the re-run reports it (503) and records nothing, so the failed attempts do not pile up.
        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => env.Reviewer(s => s.RerunAiAsync(created.Id, Token)));
        var unchanged = await env.Reviewer(s => s.GetAsync(created.Id, Token));
        Assert.Equal("FAILED", unchanged.Status);
        Assert.Single(unchanged.Inferences);

        // The service is back: run again; the failed attempt stays as history, the case continues.
        env.Ai.Answer = _ => Answer("SHEATH_BLIGHT", 0.9m, others: [("HEALTHY", 0.05m)]);
        var again = await env.Reviewer(s => s.RerunAiAsync(created.Id, Token));

        Assert.Equal("AI_COMPLETED", again.Status);
        Assert.Equal(2, again.Inferences.Count);
        Assert.Equal(["FAILED", "SUCCESS"], again.Inferences.Select(i => i.Status));

        // A case that is no longer submitted or failed cannot be run again.
        await Assert.ThrowsAsync<BusinessRuleException>(() => env.Reviewer(s => s.RerunAiAsync(created.Id, Token)));
    }

    [RealDbFact]
    public async Task A_failed_case_can_be_diagnosed_by_hand_but_not_confirmed()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        var created = await env.Submit(); // Answer is null: the AI service is unreachable
        Assert.Equal("FAILED", created.Status);

        await Assert.ThrowsAsync<DomainException>(() => env.Reviewer(s => s.ReviewAsync(
            created.Id, new ReviewRequest("CONFIRMED", env.Diseases["LEAF_BLAST"]), Token)));

        var corrected = await env.Reviewer(s => s.ReviewAsync(
            created.Id, new ReviewRequest("CORRECTED", env.Diseases["BROWN_SPOT"], Comment: "Chẩn đoán tay."), Token));
        Assert.Equal("VERIFIED", corrected.Status);
        Assert.Equal("BROWN_SPOT", corrected.FinalDisease!.Code);
        Assert.Null(corrected.CurrentReview!.PrimaryAiInferenceId);
    }

    [RealDbFact]
    public async Task Without_an_active_model_the_case_fails_and_nothing_is_recorded_against_a_model()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session, withModel: false);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m);

        var created = await env.Submit();

        Assert.Equal("FAILED", created.Status);
        Assert.Equal(0, env.Ai.Calls);
        var detail = await env.Reviewer(s => s.GetAsync(created.Id, Token));
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
            var detail = await env.Reviewer(s => s.GetAsync(created.Id, Token));
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
    public async Task Only_a_member_with_the_review_right_can_decide()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, others: [("BROWN_SPOT", 0.05m)]);
        var created = await env.Submit();

        // A sales member without the flag, and a Farmer, are refused; reading the case is not decided here.
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Reviewer(
            s => s.ReviewAsync(created.Id, new ReviewRequest("INCONCLUSIVE"), Token), env.OutsiderUserId));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Reviewer(
            s => s.StartReviewAsync(created.Id, Token), env.OutsiderUserId));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Reviewer(
            s => s.ReviewAsync(created.Id, new ReviewRequest("INCONCLUSIVE"), Token), env.FarmerUserId, "FARMER"));
        Assert.Equal("AI_COMPLETED", (await env.Reviewer(s => s.GetAsync(created.Id, Token), env.OutsiderUserId)).Status);

        // Withdrawing the flag takes effect on the next call, with the same session.
        await using (var db = session.NewContext())
        {
            var member = await db.StoreMembers.SingleAsync(m => m.Id == env.ReviewerMemberId, Token);
            member.RevokeAiReview();
            await db.SaveChangesAsync(Token);
        }

        await Assert.ThrowsAsync<ForbiddenException>(() => env.Reviewer(
            s => s.ReviewAsync(created.Id, new ReviewRequest("INCONCLUSIVE"), Token)));
    }

    [RealDbFact]
    public async Task An_inconclusive_review_asks_for_a_retake_and_a_new_review_supersedes_the_old_one()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, others: [("BROWN_SPOT", 0.05m)]);
        var created = await env.Submit();

        var inconclusive = await env.Reviewer(s => s.ReviewAsync(
            created.Id, new ReviewRequest("INCONCLUSIVE", Comment: "Ảnh bị mờ."), Token));
        Assert.Equal("INCONCLUSIVE", inconclusive.Status);
        Assert.Null(inconclusive.FinalDisease);

        var mine = await env.Farmer(s => s.GetAsync(created.Id, Token));
        Assert.Equal("INCONCLUSIVE", mine.Status);
        Assert.Null(mine.Result);
        Assert.Equal("RETAKE_PHOTO", mine.Inconclusive!.Hint);
        Assert.Equal("Ảnh bị mờ.", mine.Inconclusive.Comment);

        // No recommendation without a verified diagnosis.
        await Assert.ThrowsAnyAsync<Exception>(() => env.Reviewer(s => s.RecommendAsync(
            created.Id, new RecommendationRequest("TREATMENT", DiseaseTreatmentId: Guid.NewGuid()), Token)));

        var corrected = await env.Reviewer(s => s.ReviewAsync(
            created.Id, new ReviewRequest("CORRECTED", env.Diseases["BROWN_SPOT"]), Token));
        Assert.Equal("VERIFIED", corrected.Status);
        Assert.Single(corrected.ReviewHistory);
        Assert.Equal("INCONCLUSIVE", corrected.ReviewHistory[0].Decision);
        Assert.False(corrected.ReviewHistory[0].IsCurrent);
    }

    [RealDbFact]
    public async Task A_farmer_can_cancel_until_a_reviewer_decided_and_the_photo_is_deleted_when_saving_fails()
    {
        await using var session = await RealDb.Session.StartAsync();
        var env = await PrepareAsync(session);
        env.Ai.Answer = _ => Answer("LEAF_BLAST", 0.9m, others: [("BROWN_SPOT", 0.05m)]);
        var created = await env.Submit();

        var cancelled = await env.Farmer(s => s.CancelAsync(created.Id, new CancelDiagnosisRequest("Gửi nhầm ảnh"), Token));
        Assert.Equal("CANCELLED", cancelled.Status);
        await Assert.ThrowsAsync<DomainException>(() => env.Farmer(
            s => s.CancelAsync(created.Id, new CancelDiagnosisRequest(), Token)));

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
        var detail = await env.Reviewer(s => s.GetAsync(created.Id, Token));

        Assert.Null(mine.Image);
        Assert.Empty(detail.Images);
        Assert.Equal("AI_COMPLETED", mine.Status);
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
        env.As(env.ReviewerUserId, "STORE_OWNER");
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

        var off = await service.SetTreatmentActiveAsync(treatment.Id, false, Token);
        Assert.False(off.IsActive);
        var listed = (await service.ListAsync(true, Token)).Single(d => d.Id == id);
        Assert.Contains(listed.Treatments, t => t.Id == treatment.Id && !t.IsActive);
    }
}
