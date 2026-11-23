using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Advisory;
using AgriSage.Domain.Common;
using AgriSage.Domain.Features.Advisory.Entities;
using AgriSage.Domain.Features.Advisory.Enums;
using AgriSage.Domain.Features.Diagnosis;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Reports;

// The advice given to Farmers and the photo diagnoses of a period, for the owner dashboard (REPORTS.md "Advisory and
// diagnosis"). Read only: AsNoTracking, counts and sums in SQL.
public sealed class AdvisoryDiagnosisReportService(IAgriSageDbContext db, ReportScope scope) : IAdvisoryDiagnosisReportService
{
    private const int TopProblemCount = 5;

    public async Task<AdvisoryDiagnosisReportResponse> GetAsync(AdvisoryDiagnosisReportRequest request, CancellationToken token)
    {
        var store = await scope.GetStoreIdAsync(token);
        var from = request.FromDate!.Value;
        var to = request.ToDate!.Value;
        var start = BusinessCalendar.StartOfDay(from);
        var end = BusinessCalendar.StartOfDay(to).AddDays(1);
        var sessions = db.AdvisorySessions.AsNoTracking()
            .Where(s => s.StoreId == store && s.CreatedAt >= start && s.CreatedAt < end);

        return new(from, to, AdvisorySessionService.PurchaseWindowDays,
            await AdvisoryAsync(store, sessions, token),
            await TopProblemsAsync(sessions, token),
            await DiagnosisAsync(start, end, token));
    }

    private async Task<AdvisoryReportTotals> AdvisoryAsync(Guid store, IQueryable<AdvisorySession> sessions, CancellationToken token)
    {
        var counts = await sessions.GroupBy(s => 1).Select(g => new
        {
            Sessions = g.Count(),
            Farmers = g.Select(s => s.FarmerProfileId).Distinct().Count(),
            Text = g.Count(s => s.Source == AdvisorySource.Text),
            Diagnosis = g.Count(s => s.Source == AdvisorySource.DiagnosisCase)
        }).SingleOrDefaultAsync(token);

        var items = from s in sessions
            join i in db.AdvisorySessionItems.AsNoTracking() on s.Id equals i.AdvisorySessionId
            select new { SessionId = s.Id, s.FarmerProfileId, s.CreatedAt, i.StoreProductId, i.EstimatedAmount };
        var withProducts = await items.Select(i => i.SessionId).Distinct().CountAsync(token);
        var estimated = await items.SumAsync(i => i.EstimatedAmount, token) ?? 0m;

        // The rule of the staff session list (boughtWithin7Days, ADVISORY.md section 7): the same Farmer created a not
        // cancelled order with a product of the session from the session time up to seven days later. An attribution:
        // no order records the advice it came from.
        var lines = from o in db.Orders.AsNoTracking()
            join l in db.OrderItems.AsNoTracking() on o.Id equals l.OrderId
            where o.StoreId == store && o.Status != OrderStatus.Cancelled
            select new { LineId = l.Id, o.FarmerProfileId, o.CreatedAt, l.StoreProductId, l.LineTotalAmount };
        var matches = from i in items
            join l in lines on new { Farmer = (Guid?)i.FarmerProfileId, Product = i.StoreProductId }
                equals new { Farmer = l.FarmerProfileId, Product = l.StoreProductId }
            where l.CreatedAt >= i.CreatedAt && l.CreatedAt <= i.CreatedAt.AddDays(AdvisorySessionService.PurchaseWindowDays)
            select new { i.SessionId, l.LineId, l.LineTotalAmount };
        var converted = await matches.Select(m => m.SessionId).Distinct().CountAsync(token);
        // An order line matched by several sessions counts once.
        var convertedValue = await matches.Select(m => new { m.LineId, m.LineTotalAmount }).Distinct()
            .SumAsync(m => m.LineTotalAmount, token);

        return new(counts?.Sessions ?? 0, counts?.Farmers ?? 0, counts?.Text ?? 0, counts?.Diagnosis ?? 0,
            withProducts, estimated, converted,
            withProducts == 0 ? null : CostRounding.RoundMoney(converted * 100m / withProducts),
            convertedValue);
    }

    private async Task<IReadOnlyList<AdvisoryTopProblem>> TopProblemsAsync(IQueryable<AdvisorySession> sessions, CancellationToken token) =>
        await (from g in sessions.GroupBy(s => s.DiseaseId).Select(g => new { DiseaseId = g.Key, Count = g.Count() })
                join d in db.Diseases.IgnoreQueryFilters().AsNoTracking() on g.DiseaseId equals d.Id
                orderby g.Count descending, d.Code
                select new AdvisoryTopProblem(d.Id, d.Code, d.Name, d.CropType, g.Count))
            .Take(TopProblemCount).ToListAsync(token);

    private async Task<DiagnosisReportTotals> DiagnosisAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken token)
    {
        // Diagnosis cases carry no store: one operational store, as in the staff case list.
        var groups = await db.DiagnosisCases.AsNoTracking()
            .Where(c => c.SubmittedAt >= start && c.SubmittedAt < end)
            .Select(c => new
            {
                c.CropType,
                c.Status,
                Passed = db.AiInferences.Where(i => i.DiagnosisCaseId == c.Id && i.Status == AiInferenceStatus.Success)
                    .OrderByDescending(i => i.InferredAt).ThenByDescending(i => i.Id)
                    .Select(i => (bool?)i.PassedPolicy).FirstOrDefault()
            })
            .GroupBy(c => new { c.CropType, c.Status, c.Passed })
            .Select(g => new DiagnosisCropTally.Group(g.Key.CropType, g.Key.Status, g.Key.Passed, g.Count()))
            .ToListAsync(token);
        var rows = DiagnosisCropTally.ByCrop(groups);

        return new(rows.Sum(r => r.CaseCount), rows);
    }
}

// Diagnosis cases per crop by the certainty the Farmer reads (DiagnosisResultPolicy.CertaintyOf with the latest SUCCESS
// inference's passed_policy). Rows follow the crop catalog; a code outside it keeps its code as name and comes last.
public static class DiagnosisCropTally
{
    public sealed record Group(string CropType, DiagnosisCaseStatus Status, bool? LatestPassedPolicy, int Count);

    public static IReadOnlyList<DiagnosisCropRow> ByCrop(IEnumerable<Group> groups) =>
        groups.GroupBy(g => g.CropType, StringComparer.Ordinal)
            .Select(crop =>
            {
                int Count(DiagnosisCertainty? certainty) => crop
                    .Where(g => DiagnosisResultPolicy.CertaintyOf(g.Status, g.LatestPassedPolicy) == certainty)
                    .Sum(g => g.Count);
                var known = CropCatalog.Find(crop.Key);

                return (Order: known?.Order ?? int.MaxValue, Row: new DiagnosisCropRow(
                    crop.Key, known?.Name ?? crop.Key, crop.Sum(g => g.Count),
                    Count(DiagnosisCertainty.Confident), Count(DiagnosisCertainty.Uncertain),
                    Count(DiagnosisCertainty.Verified), Count(null)));
            })
            .OrderBy(r => r.Order).ThenBy(r => r.Row.CropType, StringComparer.Ordinal)
            .Select(r => r.Row)
            .ToList();
}
