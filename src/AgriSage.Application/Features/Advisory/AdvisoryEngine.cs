using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Inventory;
using AgriSage.Application.Features.Pricing;
using AgriSage.Application.Features.Products;
using AgriSage.Domain.Features.Advisory;
using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Products.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Advisory;

// The one place that decides which products are offered for a problem (api-flows/ADVISORY.md V1-V9). Read only: it never
// saves, reserves stock or changes a price. Used by the Farmer advisory API and by the AI diagnosis result.
public sealed class AdvisoryEngine(IAgriSageDbContext context, IPriceResolver prices, IDateTimeProvider clock)
{
    public const int MaxPerGroup = 5;

    // An offered product and the link it comes from (the session log keeps the link).
    public sealed record Entry(AdvisoryProduct Product, Guid DiseaseProductId);

    // EmptyReason says why nothing is offered (AdvisoryText), or null when something is.
    public sealed record Outcome(IReadOnlyList<Entry> Protection, IReadOnlyList<Entry> Nutrition, string? EmptyReason)
    {
        public IEnumerable<Entry> All => Protection.Concat(Nutrition);

        public static Outcome Empty(string reason) => new([], [], reason);
    }

    private sealed record StoreProductRow(Guid Id, Guid ProductId, string Name, string Sku, string? ImageUrl);

    private sealed record PackagingRow(Guid Id, Guid ProductId, long ConversionToBase, string Name);

    public async Task<Outcome> BuildAsync(
        Disease disease, Guid? farmerProfileId, decimal? areaSqm, GrowthStage? stage, CancellationToken cancellationToken)
    {
        var links = await context.DiseaseProducts.AsNoTracking()
            .Where(l => l.DiseaseId == disease.Id && l.Status == DiseaseProductStatus.Approved)
            .ToListAsync(cancellationToken);
        if (links.Count == 0)
        {
            return Outcome.Empty(AdvisoryText.NoApprovedProduct);
        }

        var fitting = links.Where(l => l.FitsStage(stage)).ToList();
        if (fitting.Count == 0)
        {
            return Outcome.Empty(AdvisoryText.NoProductForStage);
        }

        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var productIds = fitting.Select(l => l.ProductId).Distinct().ToList();

        var storeProducts = (await CatalogRules.Sellable(context.StoreProducts.AsNoTracking(), storeId)
                .Where(sp => productIds.Contains(sp.ProductId))
                .Select(sp => new StoreProductRow(sp.Id, sp.ProductId, sp.Product.Name, sp.StoreSku ?? sp.Product.Sku, sp.Product.ImageUrl))
                .ToListAsync(cancellationToken))
            .GroupBy(sp => sp.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(sp => sp.Id).First());
        if (storeProducts.Count == 0)
        {
            return Outcome.Empty(AdvisoryText.NothingInStore);
        }

        var packagings = (await context.ProductPackagings.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId) && p.Status == PackagingStatus.Active && p.IsSaleUnit)
                .Select(p => new PackagingRow(p.Id, p.ProductId, p.ConversionToBase, p.PackagingName ?? p.Unit.Name))
                .ToListAsync(cancellationToken))
            .ToLookup(p => p.ProductId);

        var storeProductIds = storeProducts.Values.Select(sp => sp.Id).ToList();
        var priceOf = await PricesAsync(farmerProfileId, storeProducts.Values, packagings, cancellationToken);
        var stock = await SellableStock.AvailableBaseAsync(context, storeProductIds, BusinessCalendar.Today(clock.UtcNow), cancellationToken);

        var entries = new List<(Entry Entry, DiseaseProduct Link, decimal? Amount)>();
        foreach (var link in fitting)
        {
            if (!storeProducts.TryGetValue(link.ProductId, out var storeProduct))
            {
                continue;
            }

            var packs = packagings[link.ProductId]
                .Where(p => priceOf.ContainsKey(new PriceLine(storeProduct.Id, p.Id)))
                .Select(p => new DosePlanner.SalePack(p.Id, p.Name, p.ConversionToBase, priceOf[new PriceLine(storeProduct.Id, p.Id)]))
                .ToList();
            var required = DosePlanner.RequiredBaseQuantity(link.DosePerHaBase, link.Applications, areaSqm);
            var plan = DosePlanner.Split(required, packs);
            var available = stock.GetValueOrDefault(storeProduct.Id);
            var needed = plan?.BaseQuantity ?? required ?? 1;

            var product = new AdvisoryProduct(
                storeProduct.Id,
                storeProduct.ProductId,
                storeProduct.Name,
                storeProduct.Sku,
                storeProduct.ImageUrl,
                EnumText.Format(link.RecommendationGroup),
                0,
                ReasonOf(link, disease),
                link.RegistrationSource,
                link.DoseText,
                link.Applications,
                link.PreHarvestDays,
                link.StageList.Select(s => s.ToString().ToUpperInvariant()).ToList(),
                available <= 0 ? AdvisoryStock.OutOfStock : available >= needed ? AdvisoryStock.Enough : AdvisoryStock.NotEnough,
                plan is null
                    ? null
                    : new AdvisoryPlan(
                        plan.BaseQuantity,
                        plan.TotalAmount,
                        plan.Lines.Select(l => new AdvisoryPlanLine(l.PackagingId, l.Name, l.Quantity, l.UnitPrice, l.LineAmount)).ToList()));

            entries.Add((new Entry(product, link.Id), link, plan?.TotalAmount));
        }

        var protection = Rank(entries, RecommendationGroup.Protection);
        var nutrition = Rank(entries, RecommendationGroup.Nutrition);

        return new Outcome(protection, nutrition, protection.Count + nutrition.Count == 0 ? AdvisoryText.NothingInStore : null);
    }

    // V8: out of stock last, then priority (higher first), then estimated amount, then name; at most five per group.
    private static IReadOnlyList<Entry> Rank(
        List<(Entry Entry, DiseaseProduct Link, decimal? Amount)> entries, RecommendationGroup group) =>
        entries
            .Where(e => e.Link.RecommendationGroup == group)
            .OrderBy(e => e.Entry.Product.StockStatus == AdvisoryStock.OutOfStock ? 1 : 0)
            .ThenByDescending(e => e.Link.Priority)
            .ThenBy(e => e.Amount ?? decimal.MaxValue)
            .ThenBy(e => e.Entry.Product.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Entry.Product.StoreProductId)
            .Take(MaxPerGroup)
            .Select((e, index) => e.Entry with { Product = e.Entry.Product with { RankOrder = index + 1 } })
            .ToList();

    private static string ReasonOf(DiseaseProduct link, Disease disease) =>
        link.Reason ?? $"Đăng ký phòng trừ: {disease.Name}";

    // The Farmer's own price list; a store with no usable list cannot sell, so the products are still shown, without a plan.
    private async Task<IReadOnlyDictionary<PriceLine, decimal>> PricesAsync(
        Guid? farmerProfileId,
        IEnumerable<StoreProductRow> storeProducts,
        ILookup<Guid, PackagingRow> packagings,
        CancellationToken cancellationToken)
    {
        var lines = storeProducts
            .SelectMany(sp => packagings[sp.ProductId].Select(p => new PriceLine(sp.Id, p.Id)))
            .ToList();
        if (lines.Count == 0)
        {
            return new Dictionary<PriceLine, decimal>();
        }

        try
        {
            var priceContext = await prices.GetContextAsync(farmerProfileId, clock.UtcNow, cancellationToken);

            return await prices.GetPricesAsync(priceContext.PriceListId, lines, cancellationToken);
        }
        catch (BusinessRuleException)
        {
            return new Dictionary<PriceLine, decimal>();
        }
    }
}
