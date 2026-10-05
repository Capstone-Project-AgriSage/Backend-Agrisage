using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Deliveries.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

// FLOW_2 §9 (task F2.7), Manage only. Attempts are counted on their completion day (Vietnam days); successRate =
// successful ÷ completed attempts (SUCCESS + PARTIAL_SUCCESS + FAILED; cancelled attempts are not counted). Like the
// sales report, the filtered rows are read in one query and grouped by Vietnam day in memory.
public sealed class DeliveryReportService(IAgriSageDbContext context) : IDeliveryReportService
{
    private sealed record AttemptRow(Guid DeliveryId, Guid MemberId, DateTimeOffset CompletedAt, DeliveryAttemptStatus Status, string? FailureReasonCode);

    public async Task<DeliveryReportResponse> GetAsync(DeliveryReportRequest request, CancellationToken cancellationToken)
    {
        var storeId = await ActiveStore.GetIdAsync(context, cancellationToken);
        var from = request.FromDate!.Value;
        var to = request.ToDate!.Value;
        var groupBy = string.IsNullOrWhiteSpace(request.GroupBy) ? "DAY" : request.GroupBy.Trim().ToUpperInvariant();
        var start = BusinessCalendar.StartOfDay(from);
        var end = BusinessCalendar.StartOfDay(to.AddDays(1));

        var storeDeliveries = context.Deliveries.Where(d => d.StoreId == storeId).Select(d => d.Id);
        var attempts = await context.DeliveryAttempts.AsNoTracking()
            .Where(a => storeDeliveries.Contains(a.DeliveryId) && a.CompletedAt >= start && a.CompletedAt < end
                && (a.Status == DeliveryAttemptStatus.Success || a.Status == DeliveryAttemptStatus.PartialSuccess
                    || a.Status == DeliveryAttemptStatus.Failed))
            .Select(a => new AttemptRow(a.DeliveryId, a.AttemptedByMemberId, a.CompletedAt!.Value, a.Status, a.FailureReasonCode))
            .ToListAsync(cancellationToken);

        // STAFF rows are keyed by the staff user id (the id the rest of the API uses), labelled with the name.
        var memberIds = attempts.Select(a => a.MemberId).Distinct().ToList();
        var staff = groupBy == "STAFF"
            ? await context.StoreMembers.AsNoTracking().IgnoreQueryFilters()
                .Where(m => memberIds.Contains(m.Id))
                .Select(m => new { m.Id, m.UserId, m.User.FullName })
                .ToDictionaryAsync(m => m.Id, m => (Key: m.UserId.ToString(), Label: m.FullName), cancellationToken)
            : [];

        var rows = attempts
            .GroupBy(a => groupBy == "STAFF"
                ? staff[a.MemberId]
                : (Key: BusinessCalendar.Today(a.CompletedAt).ToString("yyyy-MM-dd"), Label: BusinessCalendar.Today(a.CompletedAt).ToString("yyyy-MM-dd")))
            .OrderBy(g => groupBy == "STAFF" ? g.Key.Label : g.Key.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var counts = Count(g.ToList());
                return new DeliveryReportRow(g.Key.Key, g.Key.Label, counts.Deliveries, counts.Attempts, counts.Successful,
                    counts.Partial, counts.Failed, counts.SuccessRate);
            })
            .ToList();

        var failureReasons = attempts.Where(a => a.FailureReasonCode != null)
            .GroupBy(a => a.FailureReasonCode!)
            .Select(g => new FailureReasonCount(g.Key, g.Count()))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();

        var incidents = (await context.DeliveryIncidents.AsNoTracking()
                .Where(i => storeDeliveries.Contains(i.DeliveryId) && i.ReportedAt >= start && i.ReportedAt < end)
                .GroupBy(i => new { i.IncidentType, i.Status })
                .Select(g => new { g.Key.IncidentType, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .GroupBy(i => i.IncidentType)
            .Select(g => new IncidentTypeCount(EnumText.Format(g.Key),
                g.Where(i => i.Status == DeliveryIncidentStatus.Open).Sum(i => i.Count),
                g.Where(i => i.Status == DeliveryIncidentStatus.Resolved).Sum(i => i.Count)))
            .OrderBy(i => i.IncidentType, StringComparer.Ordinal)
            .ToList();

        return new DeliveryReportResponse(from, to, groupBy, rows, failureReasons, incidents, Count(attempts));
    }

    private static DeliveryReportTotals Count(IReadOnlyCollection<AttemptRow> attempts)
    {
        var successful = attempts.Count(a => a.Status == DeliveryAttemptStatus.Success);
        var partial = attempts.Count(a => a.Status == DeliveryAttemptStatus.PartialSuccess);
        var failed = attempts.Count(a => a.Status == DeliveryAttemptStatus.Failed);

        return new DeliveryReportTotals(
            attempts.Select(a => a.DeliveryId).Distinct().Count(), attempts.Count, successful, partial, failed,
            attempts.Count == 0 ? 0m : Math.Round((decimal)successful / attempts.Count, 4));
    }
}
