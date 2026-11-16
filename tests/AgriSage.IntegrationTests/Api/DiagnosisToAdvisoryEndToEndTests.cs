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

    private static AiPredictionResult Answer(params (string Label, decimal Score)[] top) =>
        new(top[0].Label, top[0].Score, top.Select(t => new AiClassScore(t.Label, t.Score)).ToList(), ModelVersion, 12,
            top[0].Score, "agrisage-rice-classifier");

    private static async Task<(JsonElement Case, string Json)> SendPhotoAsync(HttpClient farmer, CancellationToken token)
    {
        using var form = new MultipartFormDataContent();
        var photo = new ByteArrayContent(Png);
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(photo, "image", "la-lua.png");
        form.Add(new StringContent("Lá có vết hình thoi"), "note");
        var created = await OkAsync(await farmer.PostAsync("/api/me/diagnosis-cases", form, token), token);
        var response = await farmer.GetAsync($"/api/me/diagnosis-cases/{created.GetProperty("id").GetGuid()}", token);
        var json = await response.Content.ReadAsStringAsync(token);
        return (await OkAsync(response, token), json);
    }

    private sealed class FakeAi : IAiDiagnosisClient
    {
        public AiPredictionResult? Next { get; set; }

        public Task<AiPredictionResult> PredictAsync(AiPredictionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Next ?? throw new InvalidOperationException("No AI answer set."));
    }

    private sealed class FakePrivateStore : IPrivateFileStore
    {
        public Task<string> CreateSignedUrlAsync(string storageKey, StorageArea area, TimeSpan lifetime, CancellationToken cancellationToken) =>
            Task.FromResult($"https://cdn.example.com/signed/{storageKey}");

        public Task<byte[]> ReadAsync(string storageKey, StorageArea area, CancellationToken cancellationToken) => Task.FromResult(Png);
    }
}
