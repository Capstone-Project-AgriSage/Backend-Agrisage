using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Advisory;

// Staff view of the advice given to Farmers, with whether it led to a purchase (api-flows/ADVISORY.md section 7).
// Read only: AsNoTracking, filters and paging in SQL.
public sealed class AdvisorySessionService(IAgriSageDbContext context) : IAdvisorySessionService
{
    private static readonly TimeSpan PurchaseWindow = TimeSpan.FromDays(7);

    public async Task<PagedResult<AdvisorySessionResponse>> ListAsync(
        AdvisorySessionListRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var query = context.AdvisorySessions.AsNoTracking().Where(s => s.StoreId == storeId);

        if (request.From is { } from)
        {
            var start = BusinessCalendar.StartOfDay(from);
            query = query.Where(s => s.CreatedAt >= start);
        }

        if (request.To is { } to)
        {
            var end = BusinessCalendar.StartOfDay(to.AddDays(1));
            query = query.Where(s => s.CreatedAt < end);
        }

        if (request.DiseaseId is { } diseaseId)
        {
            query = query.Where(s => s.DiseaseId == diseaseId);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var sessions = await query
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            return new PagedResult<AdvisorySessionResponse>([], request.Page, request.PageSize, total);
        }

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var farmerIds = sessions.Select(s => s.FarmerProfileId).Distinct().ToList();
        var diseaseIds = sessions.Select(s => s.DiseaseId).Distinct().ToList();

        var farmers = await context.FarmerProfiles.AsNoTracking()
            .Where(f => farmerIds.Contains(f.Id))
            .Select(f => new AdvisorySessionFarmer(f.Id, f.User.FullName, f.User.PhoneNumber))
            .ToDictionaryAsync(f => f.Id, cancellationToken);
        var diseases = await context.Diseases.AsNoTracking()
            .Where(d => diseaseIds.Contains(d.Id))
            .Select(d => new AdvisorySessionProblem(d.Id, d.Name))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        var items = await (
            from item in context.AdvisorySessionItems.AsNoTracking()
            join storeProduct in context.StoreProducts on item.StoreProductId equals storeProduct.Id
            join link in context.DiseaseProducts on item.DiseaseProductId equals link.Id
            where sessionIds.Contains(item.AdvisorySessionId)
            select new { item.AdvisorySessionId, item.StoreProductId, storeProduct.Product.Name, link.RecommendationGroup, item.RankOrder })
            .ToListAsync(cancellationToken);
        var itemsBySession = items.ToLookup(i => i.AdvisorySessionId);

        var bought = await PurchasesAsync(sessions.Select(s => (s.Id, s.FarmerProfileId, s.CreatedAt)).ToList(), itemsBySession
            .ToDictionary(g => g.Key, g => g.Select(i => i.StoreProductId).ToHashSet()), cancellationToken);

        var responses = sessions.Select(s => new AdvisorySessionResponse(
            s.Id,
            s.CreatedAt,
            farmers.GetValueOrDefault(s.FarmerProfileId) ?? new AdvisorySessionFarmer(s.FarmerProfileId, string.Empty, null),
            diseases.GetValueOrDefault(s.DiseaseId) ?? new AdvisorySessionProblem(s.DiseaseId, string.Empty),
            EnumText.Format(s.Source),
            s.AreaSqm,
            s.GrowthStage is null ? null : EnumText.Format(s.GrowthStage.Value),
            itemsBySession[s.Id]
                .OrderBy(i => i.RecommendationGroup).ThenBy(i => i.RankOrder)
                .Select(i => new AdvisorySessionItemResponse(i.StoreProductId, i.Name, EnumText.Format(i.RecommendationGroup), i.RankOrder))
                .ToList(),
            bought.Contains(s.Id))).ToList();

        return new PagedResult<AdvisorySessionResponse>(responses, request.Page, request.PageSize, total);
    }

    // A session led to a purchase when the same Farmer placed a not cancelled order with one of its products from the
    // session time up to seven days later.
    private async Task<HashSet<Guid>> PurchasesAsync(
        IReadOnlyList<(Guid Id, Guid FarmerProfileId, DateTimeOffset CreatedAt)> sessions,
        IReadOnlyDictionary<Guid, HashSet<Guid>> productsBySession,
        CancellationToken cancellationToken)
    {
        var farmerIds = sessions.Select(s => (Guid?)s.FarmerProfileId).Distinct().ToList();
        var from = sessions.Min(s => s.CreatedAt);
        var to = sessions.Max(s => s.CreatedAt) + PurchaseWindow;

        var orders = await context.Orders.AsNoTracking()
            .Where(o => farmerIds.Contains(o.FarmerProfileId) && o.CreatedAt >= from && o.CreatedAt <= to
                && o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items.Where(i => i.DeletedAt == null).Select(i => new { o.FarmerProfileId, o.CreatedAt, i.StoreProductId }))
            .ToListAsync(cancellationToken);

        return sessions
            .Where(s => productsBySession.TryGetValue(s.Id, out var products)
                && orders.Any(o => o.FarmerProfileId == s.FarmerProfileId
                    && o.CreatedAt >= s.CreatedAt && o.CreatedAt <= s.CreatedAt + PurchaseWindow
                    && products.Contains(o.StoreProductId)))
            .Select(s => s.Id)
            .ToHashSet();
    }
}
