using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static AgriSage.IntegrationTests.Api.EndToEndHost;

namespace AgriSage.IntegrationTests.Api;

// AI diagnosis to advice end to end (secondary flow of the demo; AI_DIAGNOSIS.md 2.0, no human review): the store has an ACTIVE
// model and policy, care steps for Leaf Blast and one approved product link. A Farmer sends a photo; the AI is sure of Leaf
// Blast: the Farmer sees CONFIDENT (never a probability), the chemical step and the linked product, and goes on to the advice
// for 1 ha (quantity and amount), which the store sees as an advisory session from the case. An unsure answer shows UNCERTAIN
// with no chemical step and no product; a healthy leaf shows no product. Every call goes through HTTP with the real services;
// only the AI service and the private photo store are replaced.
public sealed class DiagnosisToAdvisoryEndToEndTests(StaffHttpTests.StaffApiFactory factory)
    : IClassFixture<StaffHttpTests.StaffApiFactory>
{
    private static readonly string[] Classes = ["LEAF_BLAST", "BACTERIAL_LEAF_BLIGHT", "BROWN_SPOT", "SHEATH_BLIGHT", "HEALTHY"];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private const string ModelVersion = "0.2.0";

    [CreditDbFact]
    public async Task A_confident_photo_leads_to_the_linked_product_and_advice_an_unsure_or_healthy_one_to_no_product()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        var ai = new FakeAi();
        await using var host = Start(factory, e, services =>
        {
            services.RemoveAll<IAiDiagnosisClient>();
            services.AddSingleton<IAiDiagnosisClient>(ai);
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(new FakePrivateStore());
        });
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);

        // Reference data, as the seed and the admin's model registration leave it: the classes, one ACTIVE model and policy.
        var diseases = new Dictionary<string, Guid>();
        Guid productId;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            foreach (var code in Classes)
            {
                var disease = new Disease(code, code == "LEAF_BLAST" ? "Đạo ôn" : code, isHealthyClass: code == "HEALTHY");
                db.Diseases.Add(disease);
                diseases[code] = disease.Id;
            }
            var model = new AiModel("agrisage-rice-classifier", ModelVersion, "pytorch", "https://example.test/model",
                JsonSerializer.Serialize(Classes), e.User.UserId!.Value, "mobilenet_v2", 224, 224);
            model.Activate(DateTimeOffset.UtcNow.AddMinutes(-5));
            var policy = new AiPolicyConfig(model.Id, "p1", 0.80m, DateTimeOffset.UtcNow.AddDays(-1), e.User.UserId!.Value,
                topK: 3, minimumMargin: 0.15m);
            policy.Activate();
            db.AddRange(model, policy);
            await db.SaveChangesAsync(token);
            productId = (await db.StoreProducts.AsNoTracking().SingleAsync(x => x.Id == e.StoreProductId, token)).ProductId;
        }

        // The store's content for Leaf Blast: a cultural and a chemical step, and one approved product link.
        var leafBlast = diseases["LEAF_BLAST"];
        await OkAsync(await owner.PostAsJsonAsync($"/api/diseases/{leafBlast}/treatments",
            new { treatmentType = "CULTURAL", title = "Giảm bón đạm", instructions = "Ngưng bón đạm khi bệnh mới xuất hiện." }, token), token);
        await OkAsync(await owner.PostAsJsonAsync($"/api/diseases/{leafBlast}/treatments",
            new { treatmentType = "CHEMICAL", title = "Phun thuốc đặc trị", instructions = "Phun theo nhãn khi bệnh chớm.", precautions = "Đeo khẩu trang." }, token), token);
        var link = await OkAsync(await owner.PostAsJsonAsync($"/api/diseases/{leafBlast}/products", new
        {
            productId, recommendationGroup = "PROTECTION", doseText = "2 chai/ha", registrationSource = "TT 75/2025 Phụ lục I",
            dosePerHaBase = 2m, applications = 1, preHarvestDays = 14,
        }, token), token);
        await OkAsync(await owner.PostAsync($"/api/disease-products/{link.GetProperty("id").GetGuid()}/approve", null, token), token);

        // 1. The AI is sure of Leaf Blast: CONFIDENT, the chemical step and the linked product; no probability anywhere.
        ai.Next = Answer(("LEAF_BLAST", 0.93m), ("BROWN_SPOT", 0.04m), ("SHEATH_BLIGHT", 0.02m));
        var (confidentCase, confidentJson) = await SendPhotoAsync(farmer, token);
        var result = confidentCase.GetProperty("result");
        Assert.Equal("CONFIDENT", result.GetProperty("certainty").GetString());
        Assert.Equal("LEAF_BLAST", result.GetProperty("disease").GetProperty("code").GetString());
        Assert.Contains(result.GetProperty("treatments").EnumerateArray(), t => t.GetProperty("type").GetString() == "CHEMICAL");
        Assert.Equal(e.StoreProductId, Assert.Single(result.GetProperty("products").EnumerateArray()).GetProperty("storeProductId").GetGuid());
        Assert.DoesNotContain("confidence", confidentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0.93", confidentJson);

        // From the case to the advice for 1 ha: 2 bottles, 2,000,000; the store sees a session that came from the case.
        var advice = await OkAsync(await farmer.PostAsJsonAsync("/api/me/advisory/recommendations",
            new { diseaseId = leafBlast, areaSqm = 10_000m, diagnosisCaseId = confidentCase.GetProperty("id").GetGuid() }, token), token);
        var plan = Assert.Single(advice.GetProperty("protection").EnumerateArray()).GetProperty("plan");
        Assert.Equal(2, plan.GetProperty("baseQuantity").GetInt64());
        Assert.Equal(2_000_000m, plan.GetProperty("totalAmount").GetDecimal());
        var sessions = await OkAsync(await owner.GetAsync("/api/advisory/sessions", token), token);
        var session = Assert.Single(sessions.GetProperty("items").EnumerateArray());
        Assert.Equal("DIAGNOSIS_CASE", session.GetProperty("source").GetString());

        // 2. The AI is unsure (below the policy): UNCERTAIN with a hint, no chemical step, no product.
        ai.Next = Answer(("LEAF_BLAST", 0.55m), ("BROWN_SPOT", 0.40m), ("SHEATH_BLIGHT", 0.03m));
        var unsure = (await SendPhotoAsync(farmer, token)).Case.GetProperty("result");
        Assert.Equal("UNCERTAIN", unsure.GetProperty("certainty").GetString());
        Assert.False(string.IsNullOrEmpty(unsure.GetProperty("hint").GetString()));
        Assert.Empty(unsure.GetProperty("products").EnumerateArray());
        Assert.DoesNotContain(unsure.GetProperty("treatments").EnumerateArray(), t => t.GetProperty("type").GetString() == "CHEMICAL");

        // 3. A healthy leaf, even when the AI is sure: no product.
        ai.Next = Answer(("HEALTHY", 0.97m), ("LEAF_BLAST", 0.02m), ("BROWN_SPOT", 0.01m));
        var healthy = (await SendPhotoAsync(farmer, token)).Case.GetProperty("result");
        Assert.True(healthy.GetProperty("disease").GetProperty("isHealthy").GetBoolean());
        Assert.Empty(healthy.GetProperty("products").EnumerateArray());

        // The Farmer's list shows the three cases with their certainty.
        var mine = await OkAsync(await farmer.GetAsync("/api/me/diagnosis-cases", token), token);
        Assert.Equal(3, mine.GetProperty("items").GetArrayLength());
    }

    // AI_DIAGNOSIS.md 2.2: with a general vision model (framework ANTHROPIC), the Farmer picks the crop first; the model is only
    // offered that crop's diseases and healthy, and an answer naming another crop's disease is refused.
    [CreditDbFact]
    public async Task A_crop_chosen_before_the_photo_narrows_a_general_model_to_that_crops_diseases()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        var ai = new FakeAi();
        await using var host = Start(factory, e, services =>
        {
            services.RemoveAll<IAiDiagnosisClient>();
            services.AddSingleton<IAiDiagnosisClient>(ai);
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(new FakePrivateStore());
        });
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);

        Guid productId;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            foreach (var code in Classes)
                db.Diseases.Add(new Disease(code, code, isHealthyClass: code == "HEALTHY"));
            await db.SaveChangesAsync(token);
            productId = (await db.StoreProducts.AsNoTracking().SingleAsync(x => x.Id == e.StoreProductId, token)).ProductId;
        }

        // The store adds two durian diseases through the API (the crop is new in the create request).
        var rot = await OkAsync(await owner.PostAsJsonAsync("/api/diseases", new
        {
            code = "DURIAN_PHYTOPHTHORA", name = "Thối gốc chảy nhựa", problemType = "DISEASE", cropType = "durian",
            symptoms = "Vỏ thân nứt, chảy nhựa nâu đỏ",
        }, token), token);
        Assert.Equal("DURIAN", rot.GetProperty("cropType").GetString());
        await OkAsync(await owner.PostAsJsonAsync("/api/diseases", new
        {
            code = "DURIAN_ANTHRACNOSE", name = "Thán thư", problemType = "DISEASE", cropType = "DURIAN",
        }, token), token);
        // A third durian disease the general model below is NOT registered for.
        await OkAsync(await owner.PostAsJsonAsync("/api/diseases", new
        {
            code = "DURIAN_ALGAL_SPOT", name = "Đốm rong", problemType = "DISEASE", cropType = "DURIAN",
        }, token), token);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/diseases",
            new { code = "PEPPER_ROT", name = "Thối rễ", problemType = "DISEASE", cropType = "PEPPER" }, token)).StatusCode);
        var rotId = rot.GetProperty("id").GetGuid();
        var link = await OkAsync(await owner.PostAsJsonAsync($"/api/diseases/{rotId}/products", new
        {
            productId, recommendationGroup = "PROTECTION", doseText = "Theo nhãn", registrationSource = "TT 75/2025 Phụ lục I",
        }, token), token);
        await OkAsync(await owner.PostAsync($"/api/disease-products/{link.GetProperty("id").GetGuid()}/approve", null, token), token);

        // A general model is registered with the codes it may answer: unknown codes are refused, HEALTHY is required.
        string[] general = [.. Classes, "DURIAN_PHYTOPHTHORA", "DURIAN_ANTHRACNOSE"];
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/ai-models", new
        {
            name = "claude", version = "claude-opus-5-5", framework = "ANTHROPIC", modelStorageUrl = "https://api.anthropic.com",
            classLabels = new[] { "DURIAN_PHYTOPHTHORA", "PEPPER_ROT", "HEALTHY" },
        }, token)).StatusCode);
        var registered = await OkAsync(await owner.PostAsJsonAsync("/api/ai-models", new
        {
            name = "claude", version = "claude-opus-5-5", framework = "ANTHROPIC", modelStorageUrl = "https://api.anthropic.com",
            classLabels = general,
        }, token), token);
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var model = await db.AiModels.SingleAsync(m => m.Id == registered.GetProperty("id").GetGuid(), token);
            model.Activate(DateTimeOffset.UtcNow.AddMinutes(-5));
            var policy = new AiPolicyConfig(model.Id, "p1", 0.70m, DateTimeOffset.UtcNow.AddDays(-1), e.User.UserId!.Value, topK: 3);
            policy.Activate();
            db.Add(policy);
            await db.SaveChangesAsync(token);
        }

        // The Farmer's crop picker: rice and durian (they have diseases the model knows), not coffee (no disease yet).
        var crops = await OkAsync(await farmer.GetAsync("/api/me/diagnosis/crops", token), token);
        Assert.Equal(["RICE", "DURIAN"], crops.EnumerateArray().Select(c => c.GetProperty("code").GetString()));
        var durianCrop = crops.EnumerateArray().Single(c => c.GetProperty("code").GetString() == "DURIAN");
        Assert.Equal("Sầu riêng", durianCrop.GetProperty("name").GetString());
        // The app lists what the AI can name: not the disease the model was not registered for, which still counts.
        Assert.Equal(["DURIAN_ANTHRACNOSE", "DURIAN_PHYTOPHTHORA"],
            durianCrop.GetProperty("diseases").EnumerateArray().Select(d => d.GetProperty("code").GetString()).Order());
        Assert.Equal(3, durianCrop.GetProperty("diseaseCount").GetInt32());
        var all = await OkAsync(await owner.GetAsync("/api/diagnosis/crops", token), token);
        Assert.Equal(6, all.GetArrayLength());

        // 1. A durian photo: the model is offered only durian diseases and healthy, and is sure of the rot.
        ai.Next = Answer("claude-opus-5-5", ("DURIAN_PHYTOPHTHORA", 0.88m), ("DURIAN_ANTHRACNOSE", 0.06m));
        var sure = (await SendPhotoAsync(farmer, token, "DURIAN")).Case.GetProperty("result");
        Assert.Equal("CONFIDENT", sure.GetProperty("certainty").GetString());
        Assert.Equal("DURIAN_PHYTOPHTHORA", sure.GetProperty("disease").GetProperty("code").GetString());
        Assert.Equal(e.StoreProductId, Assert.Single(sure.GetProperty("products").EnumerateArray()).GetProperty("storeProductId").GetGuid());
        var request = ai.Last!;
        Assert.Equal("ANTHROPIC", request.Framework);
        Assert.Equal("DURIAN", request.CropType);
        Assert.Equal("Sầu riêng", request.CropName);
        Assert.Equal("Lá có vết hình thoi", request.FarmerNote);
        Assert.Equal(["DURIAN_ANTHRACNOSE", "DURIAN_PHYTOPHTHORA", "HEALTHY"], request.Candidates!.Select(c => c.Code));
        Assert.Equal("Vỏ thân nứt, chảy nhựa nâu đỏ", request.Candidates!.Single(c => c.Code == "DURIAN_PHYTOPHTHORA").Symptoms);

        // 2. An answer naming a rice disease for a durian photo is not a result: the case fails and can be retried.
        ai.Next = Answer("claude-opus-5-5", ("LEAF_BLAST", 0.95m));
        var wrongCrop = (await SendPhotoAsync(farmer, token, "DURIAN")).Case;
        Assert.Equal("FAILED", wrongCrop.GetProperty("status").GetString());

        // 3. A crop without diseases, or an unknown crop, is refused before any upload; no crop means rice.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostPhotoAsync(farmer, token, "COFFEE")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostPhotoAsync(farmer, token, "PEPPER")).StatusCode);
        ai.Next = Answer("claude-opus-5-5", ("BROWN_SPOT", 0.9m));
        await SendPhotoAsync(farmer, token);
        Assert.Equal("RICE", ai.Last!.CropType);
        Assert.DoesNotContain(ai.Last!.Candidates!, c => c.Code.StartsWith("DURIAN", StringComparison.Ordinal));

        // The Farmer's history and case page say which crop each photo was of.
        var history = (await OkAsync(await farmer.GetAsync("/api/me/diagnosis-cases?pageSize=10", token), token)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["DURIAN", "DURIAN", "RICE"], history.Select(i => i.GetProperty("cropType").GetString()).Order());
        Assert.All(history, i => Assert.Equal(i.GetProperty("cropType").GetString() == "DURIAN" ? "Sầu riêng" : "Lúa", i.GetProperty("cropName").GetString()));
        var durianCase = await OkAsync(await farmer.GetAsync($"/api/me/diagnosis-cases/{wrongCrop.GetProperty("id").GetGuid()}", token), token);
        Assert.Equal("DURIAN", durianCase.GetProperty("cropType").GetString());
        Assert.Equal("Sầu riêng", durianCase.GetProperty("cropName").GetString());

        // 4. Typed advice filters by crop (ADVISORY.md 1.1); rice growth stages do not apply to a durian problem.
        var durianProblems = await OkAsync(await farmer.GetAsync("/api/me/advisory/problems?cropType=durian", token), token);
        Assert.Equal(["DURIAN_ALGAL_SPOT", "DURIAN_ANTHRACNOSE", "DURIAN_PHYTOPHTHORA"],
            durianProblems.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Order());
        Assert.All(durianProblems.GetProperty("items").EnumerateArray(), i => Assert.Equal("DURIAN", i.GetProperty("cropType").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await farmer.GetAsync("/api/me/advisory/problems?cropType=PEPPER", token)).StatusCode);
        var durianAdvice = await OkAsync(await farmer.PostAsJsonAsync("/api/me/advisory/recommendations",
            new { diseaseId = rotId, growthStage = "TILLERING" }, token), token);
        Assert.Equal(JsonValueKind.Null, durianAdvice.GetProperty("growthStage").ValueKind);
        Assert.Equal("DURIAN", durianAdvice.GetProperty("problem").GetProperty("cropType").GetString());
        Assert.Single(durianAdvice.GetProperty("protection").EnumerateArray());
    }

    // AI_DIAGNOSIS.md 2.4 (D19): dragon fruit stem photos go to the team's own model (fixed classes, AI service), every other
    // crop to the general model. Both stay ACTIVE together; activating a model replaces only a model of the same kind.
    [CreditDbFact]
    public async Task A_crop_model_answers_its_crop_and_the_general_model_every_other_crop()
    {
        var token = TestContext.Current.CancellationToken;
        await using var e = await CreditTestDatabase.StartAsync(token);
        var ai = new FakeAi();
        await using var host = Start(factory, e, services =>
        {
            services.RemoveAll<IAiDiagnosisClient>();
            services.AddSingleton<IAiDiagnosisClient>(ai);
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(new FakePrivateStore());
        });
        await host.GrantDefaultsAsync(RoleCode.Farmer, "FARMER", token);
        using var owner = await host.ClientAsync(e.User.UserId!.Value, "STORE_OWNER", token);
        using var farmer = await host.ClientAsync(e.FarmerUserId, "FARMER", token);

        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            foreach (var code in Classes)
                db.Diseases.Add(new Disease(code, code, isHealthyClass: code == "HEALTHY"));
            await db.SaveChangesAsync(token);
        }

        await OkAsync(await owner.PostAsJsonAsync("/api/diseases", new
        {
            code = "DURIAN_PHYTOPHTHORA", name = "Thối gốc chảy nhựa", problemType = "DISEASE", cropType = "DURIAN",
        }, token), token);
        var canker = await OkAsync(await owner.PostAsJsonAsync("/api/diseases", new
        {
            code = "DRAGONFRUIT_STEM_CANKER", name = "Bệnh đốm nâu (tắc kè)", problemType = "DISEASE", cropType = "dragon_fruit",
        }, token), token);
        Assert.Equal("DRAGON_FRUIT", canker.GetProperty("cropType").GetString());

        // A crop model answers one crop: HEALTHY plus that crop's diseases. Two crops, or no HEALTHY, are refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/ai-models", CropModel(
            "mixed", "1", "HEALTHY", "DRAGONFRUIT_STEM_CANKER", "DURIAN_PHYTOPHTHORA"), token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/ai-models", CropModel(
            "no-healthy", "1", "DRAGONFRUIT_STEM_CANKER"), token)).StatusCode);

        string[] general = [.. Classes, "DURIAN_PHYTOPHTHORA"];
        var claude = await RegisterAndActivateAsync(owner, new
        {
            name = "claude", version = "claude-opus-5-5", framework = "ANTHROPIC", modelStorageUrl = "https://api.anthropic.com",
            classLabels = general,
        }, topK: 3, token);
        Assert.Equal(JsonValueKind.Null, claude.GetProperty("cropType").ValueKind);
        var dragon = await RegisterAndActivateAsync(owner, CropModel(
            "agrisage-dragonfruit-stem-classifier", "0.1.0", "HEALTHY", "DRAGONFRUIT_STEM_CANKER"), topK: 2, token);
        Assert.Equal("DRAGON_FRUIT", dragon.GetProperty("cropType").GetString());
        Assert.Equal(["claude", "agrisage-dragonfruit-stem-classifier"], await ActiveModelsAsync(owner, token));

        // The Farmer's crops: dragon fruit through its own model (one disease), durian and rice through the general model.
        var crops = await OkAsync(await farmer.GetAsync("/api/me/diagnosis/crops", token), token);
        Assert.Equal(["RICE", "DURIAN", "DRAGON_FRUIT"], crops.EnumerateArray().Select(c => c.GetProperty("code").GetString()));
        var dragonCrop = crops.EnumerateArray().Single(c => c.GetProperty("code").GetString() == "DRAGON_FRUIT");
        Assert.Equal("Thanh long", dragonCrop.GetProperty("name").GetString());
        Assert.Equal(["DRAGONFRUIT_STEM_CANKER"],
            dragonCrop.GetProperty("diseases").EnumerateArray().Select(d => d.GetProperty("code").GetString()));

        // 1. A dragon fruit stem photo is answered by the crop model, under its own policy.
        ai.Next = Answer("0.1.0", ("DRAGONFRUIT_STEM_CANKER", 0.93m), ("HEALTHY", 0.07m));
        var stem = (await SendPhotoAsync(farmer, token, "DRAGON_FRUIT")).Case.GetProperty("result");
        Assert.Equal("CONFIDENT", stem.GetProperty("certainty").GetString());
        Assert.Equal("DRAGONFRUIT_STEM_CANKER", stem.GetProperty("disease").GetProperty("code").GetString());
        Assert.Equal("pytorch", ai.Last!.Framework);
        Assert.Equal(["DRAGONFRUIT_STEM_CANKER", "HEALTHY"], ai.Last!.Candidates!.Select(c => c.Code));

        // 2. A durian photo still goes to the general model; an answer from another model's version fails the case.
        ai.Next = Answer("claude-opus-5-5", ("DURIAN_PHYTOPHTHORA", 0.9m));
        var durian = (await SendPhotoAsync(farmer, token, "DURIAN")).Case.GetProperty("result");
        Assert.Equal("DURIAN_PHYTOPHTHORA", durian.GetProperty("disease").GetProperty("code").GetString());
        Assert.Equal("ANTHROPIC", ai.Last!.Framework);
        ai.Next = Answer("claude-opus-5-5", ("DRAGONFRUIT_STEM_CANKER", 0.9m), ("HEALTHY", 0.1m));
        Assert.Equal("FAILED", (await SendPhotoAsync(farmer, token, "DRAGON_FRUIT")).Case.GetProperty("status").GetString());

        // 3. Another crop model replaces the dragon fruit model (one AI service serves the crop model); the general model stays.
        await RegisterAndActivateAsync(owner, CropModel("agrisage-rice-classifier", ModelVersion, Classes), topK: 3, token);
        Assert.Equal(["claude", "agrisage-rice-classifier"], await ActiveModelsAsync(owner, token));
        var after = await OkAsync(await farmer.GetAsync("/api/me/diagnosis/crops", token), token);
        Assert.Equal(["RICE", "DURIAN"], after.EnumerateArray().Select(c => c.GetProperty("code").GetString()));
        ai.Next = Answer(("BROWN_SPOT", 0.9m));
        await SendPhotoAsync(farmer, token, "RICE");
        Assert.Equal("pytorch", ai.Last!.Framework);

        // 4. Another general model replaces only the general model.
        await RegisterAndActivateAsync(owner, new
        {
            name = "gemini", version = "gemini-3.8-flash", framework = "GOOGLE", modelStorageUrl = "https://ai.google.dev",
            classLabels = general,
        }, topK: 3, token);
        Assert.Equal(["gemini", "agrisage-rice-classifier"], await ActiveModelsAsync(owner, token));
    }

    private static object CropModel(string name, string version, params string[] labels) => new
    {
        name, version, framework = "pytorch", architecture = "vit_base_patch16_224", modelStorageUrl = "https://example.test/weights",
        inputWidth = 224, inputHeight = 224, classLabels = labels,
    };

    // Registers a model through the API, activates it and gives it an effective ACTIVE policy.
    private static async Task<JsonElement> RegisterAndActivateAsync(HttpClient owner, object manifest, int topK, CancellationToken token)
    {
        var model = await OkAsync(await owner.PostAsJsonAsync("/api/ai-models", manifest, token), token);
        var id = model.GetProperty("id").GetGuid();
        var active = await OkAsync(await owner.PostAsync($"/api/ai-models/{id}/activate", null, token), token);
        Assert.Equal("ACTIVE", active.GetProperty("status").GetString());
        var policy = await OkAsync(await owner.PostAsJsonAsync($"/api/ai-models/{id}/policies", new
        {
            version = "p1", minimumConfidence = 0.70m, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), topK,
        }, token), token);
        await OkAsync(await owner.PostAsync($"/api/ai-policies/{policy.GetProperty("id").GetGuid()}/activate", null, token), token);
        return active;
    }

    // The names of the ACTIVE models: the general model first, then the crop model.
    private static async Task<string[]> ActiveModelsAsync(HttpClient owner, CancellationToken token) =>
        (await OkAsync(await owner.GetAsync("/api/ai-models?status=ACTIVE&pageSize=10", token), token))
            .GetProperty("items").EnumerateArray()
            .OrderBy(m => m.GetProperty("cropType").ValueKind != JsonValueKind.Null)
            .Select(m => m.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();

    private static AiPredictionResult Answer(params (string Label, decimal Score)[] top) => Answer(ModelVersion, top);

    private static AiPredictionResult Answer(string version, params (string Label, decimal Score)[] top) =>
        new(top[0].Label, top[0].Score, top.Select(t => new AiClassScore(t.Label, t.Score)).ToList(), version, 12,
            top[0].Score, "agrisage-rice-classifier");

    private static async Task<HttpResponseMessage> PostPhotoAsync(HttpClient farmer, CancellationToken token, string? cropType = null)
    {
        using var form = new MultipartFormDataContent();
        var photo = new ByteArrayContent(Png);
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(photo, "image", "la-lua.png");
        form.Add(new StringContent("Lá có vết hình thoi"), "note");
        if (cropType is not null) form.Add(new StringContent(cropType), "cropType");
        return await farmer.PostAsync("/api/me/diagnosis-cases", form, token);
    }

    private static async Task<(JsonElement Case, string Json)> SendPhotoAsync(HttpClient farmer, CancellationToken token, string? cropType = null)
    {
        var created = await OkAsync(await PostPhotoAsync(farmer, token, cropType), token);
        var response = await farmer.GetAsync($"/api/me/diagnosis-cases/{created.GetProperty("id").GetGuid()}", token);
        var json = await response.Content.ReadAsStringAsync(token);
        return (await OkAsync(response, token), json);
    }

    private sealed class FakeAi : IAiDiagnosisClient
    {
        public AiPredictionResult? Next { get; set; }

        public AiPredictionRequest? Last { get; private set; }

        public Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(Next ?? throw new InvalidOperationException("No AI answer set."));
        }
    }

    private sealed class FakePrivateStore : IPrivateFileStore
    {
        public Task<string> CreateSignedUrlAsync(string storageKey, StorageArea area, TimeSpan lifetime, CancellationToken cancellationToken) =>
            Task.FromResult($"https://cdn.example.com/signed/{storageKey}");

        public Task<byte[]> ReadAsync(string storageKey, StorageArea area, CancellationToken cancellationToken) => Task.FromResult(Png);
    }
}
