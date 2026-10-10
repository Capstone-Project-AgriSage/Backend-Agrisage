using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using Microsoft.EntityFrameworkCore;
using ValidationException = AgriSage.Application.Common.Exceptions.ValidationException;

namespace AgriSage.Application.Features.Diagnosis;

// Registry of the model versions and the policies that interpret them (AI_DIAGNOSIS.md section 5).
// At most one model is ACTIVE (D5); a policy may be activated only on an ACTIVE model and never overlaps another ACTIVE
// policy of the same model in time. Every status change is audited in the same save.
public sealed class AiModelService(
    IAgriSageDbContext context,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    AuditTrail audit) : IAiModelService
{
    // The classes the production model must predict: exactly the seeded diseases (DiseaseSeeder).
    private static readonly string[] RequiredLabels =
        ["LEAF_BLAST", "BACTERIAL_LEAF_BLIGHT", "BROWN_SPOT", "SHEATH_BLIGHT", "HEALTHY"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult<AiModelResponse>> ListAsync(AiModelListRequest request, CancellationToken cancellationToken)
    {
        var query = context.AiModels.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!EnumText.TryParse<AiModelStatus>(request.Status, out var status))
            {
                throw Invalid("status", "Unknown status.");
            }

            query = query.Where(m => m.Status == status);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var models = await query.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id)
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);

        var ids = models.Select(m => m.Id).ToList();
        var policies = await context.AiPolicyConfigs.AsNoTracking()
            .Where(p => ids.Contains(p.AiModelId)).OrderByDescending(p => p.EffectiveFrom).ToListAsync(cancellationToken);

        return new PagedResult<AiModelResponse>(
            models.Select(m => ToResponse(m, policies.Where(p => p.AiModelId == m.Id))).ToList(),
            request.Page, request.PageSize, total);
    }

    public async Task<AiModelResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await BuildAsync(id, cancellationToken);

    public async Task<AiModelResponse> CreateAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");

        var labels = request.ClassLabels.Select(l => l.Trim()).ToList();
        if (labels.Count != RequiredLabels.Length || !labels.ToHashSet().SetEquals(RequiredLabels))
        {
            throw Invalid("classLabels", $"classLabels must be exactly: {string.Join(", ", RequiredLabels)}.");
        }

        var name = request.Name.Trim();
        var version = request.Version.Trim();
        if (await context.AiModels.IgnoreQueryFilters().AnyAsync(m => m.Name == name && m.Version == version, cancellationToken))
        {
            throw new ConflictException($"The model '{name} {version}' is already registered.");
        }

        var model = new AiModel(
            name,
            version,
            request.Framework.Trim(),
            request.ModelStorageUrl.Trim(),
            JsonSerializer.Serialize(labels, Json),
            userId,
            Texts.Clean(request.Architecture),
            request.InputWidth,
            request.InputHeight,
            ToJson(request.Metrics));
        context.AiModels.Add(model);
        await context.SaveChangesAsync(cancellationToken);

        return await BuildAsync(model.Id, cancellationToken);
    }

    public async Task<AiModelResponse> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var model = await FindModelAsync(id, cancellationToken);
        var now = clock.UtcNow;

        var others = await context.AiModels
            .Where(m => m.Id != id && m.Status == AiModelStatus.Active).ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            other.Retire(now);
            audit.Record("AI_MODEL_RETIRED", "AI_MODEL", other.Id, storeId, new { Status = "ACTIVE" }, new { Status = "RETIRED" },
                $"Replaced by {model.Name} {model.Version}.");
        }

        model.Activate(now);
        audit.Record("AI_MODEL_ACTIVATED", "AI_MODEL", model.Id, storeId, new { Status = "DRAFT" }, new { Status = "ACTIVE" });
        await context.SaveChangesAsync(cancellationToken);

        return await BuildAsync(id, cancellationToken);
    }

    public async Task<AiModelResponse> RetireAsync(Guid id, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var model = await FindModelAsync(id, cancellationToken);

        model.Retire(clock.UtcNow);
        audit.Record("AI_MODEL_RETIRED", "AI_MODEL", model.Id, storeId, new { Status = "ACTIVE" }, new { Status = "RETIRED" });
        await context.SaveChangesAsync(cancellationToken);

        return await BuildAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<AiPolicyResponse>> ListPoliciesAsync(Guid modelId, CancellationToken cancellationToken)
    {
        if (!await context.AiModels.AsNoTracking().AnyAsync(m => m.Id == modelId, cancellationToken))
        {
            throw new NotFoundException("AI model", modelId);
        }

        var policies = await context.AiPolicyConfigs.AsNoTracking()
            .Where(p => p.AiModelId == modelId).OrderByDescending(p => p.EffectiveFrom).ToListAsync(cancellationToken);

        return policies.Select(ToResponse).ToList();
    }

    public async Task<AiPolicyResponse> CreatePolicyAsync(Guid modelId, AiPolicyRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var model = await FindModelAsync(modelId, cancellationToken);
        var version = request.Version.Trim();

        if (model.Status == AiModelStatus.Retired)
        {
            throw new BusinessRuleException("A retired model cannot get new policies.");
        }

        if (await context.AiPolicyConfigs.IgnoreQueryFilters()
                .AnyAsync(p => p.AiModelId == modelId && p.Version == version, cancellationToken))
        {
            throw new ConflictException($"The model already has a policy '{version}'.");
        }

        var policy = new AiPolicyConfig(
            modelId,
            version,
            request.MinimumConfidence,
            request.EffectiveFrom,
            userId,
            request.TopK,
            request.MinimumMargin,
            request.RequiresHumanReview,
            ToJson(request.Parameters),
            request.EffectiveTo);
        context.AiPolicyConfigs.Add(policy);
        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(policy);
    }

    public async Task<AiPolicyResponse> ActivatePolicyAsync(Guid policyId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var policy = await FindPolicyAsync(policyId, cancellationToken);
        var model = await context.AiModels.AsNoTracking().FirstAsync(m => m.Id == policy.AiModelId, cancellationToken);

        if (model.Status != AiModelStatus.Active)
        {
            throw new BusinessRuleException("A policy can only be activated while its model is ACTIVE.");
        }

        var overlapping = await context.AiPolicyConfigs.AsNoTracking()
            .Where(p => p.AiModelId == policy.AiModelId && p.Id != policyId && p.Status == AiPolicyConfigStatus.Active)
            .ToListAsync(cancellationToken);
        var clash = overlapping.FirstOrDefault(p => Overlaps(p, policy));
        if (clash is not null)
        {
            throw new BusinessRuleException(
                $"The effective period overlaps the ACTIVE policy '{clash.Version}'. Deactivate it or change the period first.");
        }

        var before = EnumText.Format(policy.Status);
        policy.Activate();
        audit.Record(
            "AI_POLICY_ACTIVATED", "AI_POLICY", policy.Id, storeId, new { Status = before }, new { Status = "ACTIVE", policy.Version });
        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(policy);
    }

    public async Task<AiPolicyResponse> DeactivatePolicyAsync(Guid policyId, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var policy = await FindPolicyAsync(policyId, cancellationToken);

        policy.Deactivate();
        audit.Record(
            "AI_POLICY_DEACTIVATED", "AI_POLICY", policy.Id, storeId, new { Status = "ACTIVE" }, new { Status = "INACTIVE", policy.Version });
        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(policy);
    }

    // [from, to) intervals; a missing end is open.
    private static bool Overlaps(AiPolicyConfig a, AiPolicyConfig b) =>
        a.EffectiveFrom < (b.EffectiveTo ?? DateTimeOffset.MaxValue) && b.EffectiveFrom < (a.EffectiveTo ?? DateTimeOffset.MaxValue);

    private async Task<AiModel> FindModelAsync(Guid id, CancellationToken cancellationToken) =>
        await context.AiModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
        ?? throw new NotFoundException("AI model", id);

    private async Task<AiPolicyConfig> FindPolicyAsync(Guid id, CancellationToken cancellationToken) =>
        await context.AiPolicyConfigs.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new NotFoundException("AI policy", id);

    private async Task<AiModelResponse> BuildAsync(Guid id, CancellationToken cancellationToken)
    {
        var model = await context.AiModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new NotFoundException("AI model", id);
        var policies = await context.AiPolicyConfigs.AsNoTracking()
            .Where(p => p.AiModelId == id).OrderByDescending(p => p.EffectiveFrom).ToListAsync(cancellationToken);

        return ToResponse(model, policies);
    }

    private static string? ToJson(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.Value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("metrics", "Metrics and parameters must be a JSON object.");
        }

        return value.Value.GetRawText();
    }

    private static JsonElement? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AiPolicyResponse ToResponse(AiPolicyConfig p) => new(
        p.Id, p.AiModelId, p.Version, p.MinimumConfidence, p.MinimumMargin, p.TopK, p.RequiresHumanReview,
        FromJson(p.Parameters), EnumText.Format(p.Status), p.EffectiveFrom, p.EffectiveTo);

    private static AiModelResponse ToResponse(AiModel m, IEnumerable<AiPolicyConfig> policies)
    {
        List<string> labels;
        try
        {
            labels = JsonSerializer.Deserialize<List<string>>(m.ClassLabels) ?? [];
        }
        catch (JsonException)
        {
            labels = [];
        }

        return new AiModelResponse(
            m.Id, m.Name, m.Version, m.Architecture, m.Framework, m.ModelStorageUrl, m.InputWidth, m.InputHeight, labels,
            FromJson(m.Metrics), EnumText.Format(m.Status), m.DeployedAt, m.RetiredAt, m.CreatedAt,
            policies.Select(ToResponse).ToList());
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
