using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Advisory;

// Staff side of the problem-to-product links (api-flows/ADVISORY.md section 6, V1). Every write is audited; a link is
// approved by a person with its own permission, and an approved link that is edited goes back to draft.
public sealed class DiseaseProductService(
    IAgriSageDbContext context,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    AuditTrail audit) : IDiseaseProductService
{
    public async Task<IReadOnlyList<DiseaseProductResponse>> ListAsync(Guid diseaseId, CancellationToken cancellationToken)
    {
        await EnsureDiseaseAsync(diseaseId, cancellationToken);

        var rows = await Query(diseaseId: diseaseId).ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Link.Status == DiseaseProductStatus.Inactive ? 1 : 0)
            .ThenBy(r => r.Link.RecommendationGroup)
            .ThenByDescending(r => r.Link.Priority)
            .ThenBy(r => r.ProductName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<DiseaseProductResponse> CreateAsync(
        Guid diseaseId, CreateDiseaseProductRequest request, CancellationToken cancellationToken)
    {
        await EnsureDiseaseAsync(diseaseId, cancellationToken);

        var product = await context.Products.AsNoTracking()
                .Where(p => p.Id == request.ProductId).Select(p => new { p.Id, p.Status }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductId);
        if (product.Status != ProductStatus.Active)
        {
            throw new BusinessRuleException("Only an ACTIVE product can be linked to a problem.");
        }

        if (await context.DiseaseProducts.AsNoTracking()
                .AnyAsync(l => l.DiseaseId == diseaseId && l.ProductId == request.ProductId, cancellationToken))
        {
            throw new ConflictException("This product is already linked to the problem.");
        }

        var link = new DiseaseProduct(
            diseaseId,
            request.ProductId,
            ParseGroup(request.RecommendationGroup),
            request.DoseText,
            request.RegistrationSource,
            request.Reason,
            request.DoseSource,
            request.DosePerHaBase,
            request.Applications,
            request.PreHarvestDays,
            ParseStages(request.GrowthStages),
            request.Priority);
        context.DiseaseProducts.Add(link);
        audit.Record("DISEASE_PRODUCT_CREATED", "DISEASE_PRODUCT", link.Id, await StoreIdAsync(cancellationToken), newValues: Snapshot(link));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(link.Id, cancellationToken);
    }

    public async Task<DiseaseProductResponse> UpdateAsync(Guid id, DiseaseProductRequest request, CancellationToken cancellationToken)
    {
        var link = await FindAsync(id, cancellationToken);
        var before = Snapshot(link);

        link.Update(
            ParseGroup(request.RecommendationGroup),
            request.DoseText,
            request.RegistrationSource,
            request.Reason,
            request.DoseSource,
            request.DosePerHaBase,
            request.Applications,
            request.PreHarvestDays,
            ParseStages(request.GrowthStages),
            request.Priority);
        audit.Record("DISEASE_PRODUCT_UPDATED", "DISEASE_PRODUCT", link.Id, await StoreIdAsync(cancellationToken), before, Snapshot(link));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DiseaseProductResponse> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var approver = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        var link = await FindAsync(id, cancellationToken);
        var before = Snapshot(link);

        link.Approve(approver, clock.UtcNow);
        audit.Record("DISEASE_PRODUCT_APPROVED", "DISEASE_PRODUCT", link.Id, await StoreIdAsync(cancellationToken), before, Snapshot(link));
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var link = await FindAsync(id, cancellationToken);
        var before = Snapshot(link);

        link.Deactivate();
        audit.Record("DISEASE_PRODUCT_DEACTIVATED", "DISEASE_PRODUCT", link.Id, await StoreIdAsync(cancellationToken), before, Snapshot(link));
        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed record Row(DiseaseProduct Link, string Sku, string ProductName, string? BaseUnit);

    // The filters are applied before the projection: a query cannot be filtered after it has been shaped into a Row.
    private IQueryable<Row> Query(Guid? diseaseId = null, Guid? id = null)
    {
        var links = context.DiseaseProducts.AsNoTracking().AsQueryable();
        if (diseaseId is { } disease)
        {
            links = links.Where(l => l.DiseaseId == disease);
        }

        if (id is { } linkId)
        {
            links = links.Where(l => l.Id == linkId);
        }

        return links.Select(l => new Row(
            l,
            l.Product.Sku,
            l.Product.Name,
            l.Product.Packagings.Where(p => p.IsBaseUnit).Select(p => p.Unit.Name).FirstOrDefault()));
    }

    private async Task<DiseaseProductResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await Query(id: id).FirstAsync(cancellationToken));

    private async Task<DiseaseProduct> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await context.DiseaseProducts.FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
        ?? throw new NotFoundException("Disease product", id);

    private async Task EnsureDiseaseAsync(Guid diseaseId, CancellationToken cancellationToken)
    {
        if (!await context.Diseases.AsNoTracking().AnyAsync(d => d.Id == diseaseId, cancellationToken))
        {
            throw new NotFoundException("Disease", diseaseId);
        }
    }

    private Task<Guid> StoreIdAsync(CancellationToken cancellationToken) => ActiveStore.GetIdAsync(context, cancellationToken);

    private static RecommendationGroup ParseGroup(string text) =>
        EnumText.TryParse<RecommendationGroup>(text, out var group)
            ? group
            : throw new ValidationException(new Dictionary<string, string[]>
            {
                ["recommendationGroup"] = ["RecommendationGroup must be PROTECTION or NUTRITION."]
            });

    private static IReadOnlyCollection<GrowthStage>? ParseStages(IReadOnlyList<string>? stages) =>
        stages?.Select(code => EnumText.TryParse<GrowthStage>(code, out var stage)
                ? stage
                : throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["growthStages"] = [$"Unknown growth stage '{code}'."]
                }))
            .ToList();

    private static object Snapshot(DiseaseProduct link) => new
    {
        link.DiseaseId,
        link.ProductId,
        Group = EnumText.Format(link.RecommendationGroup),
        link.RegistrationSource,
        link.Reason,
        link.DoseText,
        link.DoseSource,
        link.DosePerHaBase,
        link.Applications,
        link.PreHarvestDays,
        link.GrowthStages,
        link.Priority,
        Status = EnumText.Format(link.Status)
    };

    private static DiseaseProductResponse ToResponse(Row row) => new(
        row.Link.Id,
        row.Link.DiseaseId,
        row.Link.ProductId,
        row.Sku,
        row.ProductName,
        EnumText.Format(row.Link.RecommendationGroup),
        row.Link.RegistrationSource,
        row.Link.Reason,
        row.Link.DoseText,
        row.Link.DoseSource,
        row.Link.DosePerHaBase,
        row.BaseUnit ?? string.Empty,
        row.Link.Applications,
        row.Link.PreHarvestDays,
        row.Link.StageList.Select(s => s.ToString().ToUpperInvariant()).ToList(),
        row.Link.Priority,
        EnumText.Format(row.Link.Status),
        row.Link.ApprovedBy,
        row.Link.ApprovedAt);
}
