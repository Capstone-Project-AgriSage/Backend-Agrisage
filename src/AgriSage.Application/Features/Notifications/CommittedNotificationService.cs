using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Inventory.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

// Collect immutable committed evidence when the posting/AI modules do not publish an outbox event.
// This use case only writes notifications. A failed save is retried with the same per-record keys.
public sealed class CommittedNotificationService(IAgriSageDbContext context, NotificationWriter writer)
{
    public async Task StockAsync(int batchSize, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var owners = await NotificationRecipients.StaffAsync(context, store, token, RoleCode.StoreOwner);
        if (owners.Count == 0) return;
        var rows = await context.StockMovements.AsNoTracking().Where(m => m.StoreId == store && m.PostedAt != null
            && (m.Status == StockMovementStatus.Posted || m.Status == StockMovementStatus.Reversed)
            && context.Notifications.IgnoreQueryFilters().Count(n => owners.Contains(n.UserId)
                && n.DeduplicationKey == "stock-posted:" + m.Id.ToString()) < owners.Count)
            .OrderBy(m => m.PostedAt).ThenBy(m => m.Id).Take(batchSize)
            .Select(m => new { m.Id, m.MovementNumber, m.MovementType }).ToListAsync(token);
        foreach (var row in rows)
        {
            var (type, title) = row.MovementType switch
            {
                StockMovementType.StockIn or StockMovementType.ReturnIn => ("STOCK_RECEIVED", "Đã ghi nhận nhập kho"),
                StockMovementType.Sale => ("STOCK_ISSUED", "Đã ghi nhận xuất kho"),
                _ => ("STOCK_ADJUSTED", "Đã ghi nhận điều chỉnh tồn kho")
            };
            await writer.AddAsync(owners, type, title, $"Phiếu {row.MovementNumber} đã được ghi sổ. Vui lòng xem chi tiết biến động kho.",
                $"stock-posted:{row.Id}", "STOCK_MOVEMENT", row.Id, token);
        }
        await context.SaveChangesAsync(token);
    }

    public async Task DiagnosisAsync(int batchSize, CancellationToken token)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var cases = context.DiagnosisCases.AsNoTracking().Where(c => c.FarmerProfile.User.Status == UserStatus.Active
            && c.FarmerProfile.User.Role.IsActive);
        var ai = await cases.Where(c => (c.Status == DiagnosisCaseStatus.AiCompleted || c.Status == DiagnosisCaseStatus.UnderReview
                || c.Status == DiagnosisCaseStatus.Verified || c.Status == DiagnosisCaseStatus.Inconclusive)
            && context.AiInferences.Any(i => i.DiagnosisCaseId == c.Id && i.Status == AiInferenceStatus.Success)
            && !context.Notifications.IgnoreQueryFilters().Any(n => n.UserId == c.FarmerProfile.UserId
                && n.DeduplicationKey == "diagnosis-ai:" + c.Id.ToString()))
            .OrderBy(c => c.SubmittedAt).ThenBy(c => c.Id).Take(batchSize)
            .Select(c => new { c.Id, c.CaseNumber, c.FarmerProfile.UserId }).ToListAsync(token);
        foreach (var row in ai)
            await writer.AddAsync([row.UserId], "AI_DIAGNOSIS_COMPLETED", "Đã có kết quả AI chẩn đoán",
                $"Hồ sơ {row.CaseNumber} đã có kết quả AI. Kết quả cần chuyên gia xét duyệt trước khi sử dụng khuyến nghị điều trị.",
                $"diagnosis-ai:{row.Id}", "DIAGNOSIS_CASE", row.Id, token);

        var authorized = context.StoreMembers.AsNoTracking().Where(m => m.StoreId == store && m.Status == StoreMemberStatus.Active
            && m.CanReviewAi && m.User.Status == UserStatus.Active && m.User.Role.IsActive);
        var reviews = from r in context.AgentReviews.AsNoTracking()
                      join c in cases on r.DiagnosisCaseId equals c.Id
                      where r.IsCurrent && authorized.Any(m => m.Id == r.ReviewerMemberId)
                          && (c.Status == DiagnosisCaseStatus.Verified || c.Status == DiagnosisCaseStatus.Inconclusive)
                      select new { ReviewId = r.Id, CaseId = c.Id, c.CaseNumber, c.FarmerProfile.UserId, r.Decision, c.Status };
        var reviewed = await reviews.Where(r => !context.Notifications.IgnoreQueryFilters().Any(n => n.UserId == r.UserId
                && n.DeduplicationKey == "diagnosis-review:" + r.ReviewId.ToString()))
            .OrderBy(r => r.ReviewId).Take(batchSize).ToListAsync(token);
        foreach (var row in reviewed)
            await writer.AddAsync([row.UserId], "DIAGNOSIS_REVIEWED", "Chuyên gia đã xét duyệt chẩn đoán",
                $"Hồ sơ {row.CaseNumber} đã được xét duyệt. Vui lòng xem kết luận và hướng dẫn của chuyên gia.",
                $"diagnosis-review:{row.ReviewId}", "DIAGNOSIS_CASE", row.CaseId, token);

        var recommended = await reviews.Where(r => r.Status == DiagnosisCaseStatus.Verified
            && (r.Decision == AgentReviewDecision.Confirmed || r.Decision == AgentReviewDecision.Corrected)
            && context.RecommendationItems.Any(i => i.AgentReviewId == r.ReviewId && i.DiagnosisCaseId == r.CaseId && i.IsActive
                && authorized.Any(m => m.UserId == i.ApprovedBy))
            && !context.Notifications.IgnoreQueryFilters().Any(n => n.UserId == r.UserId
                && n.DeduplicationKey == "diagnosis-recommendations:" + r.ReviewId.ToString()))
            .OrderBy(r => r.ReviewId).Take(batchSize).ToListAsync(token);
        foreach (var row in recommended)
            await writer.AddAsync([row.UserId], "DIAGNOSIS_RECOMMENDATIONS", "Đã có khuyến nghị được chuyên gia phê duyệt",
                $"Hồ sơ {row.CaseNumber} đã có khuyến nghị được chuyên gia phê duyệt. Vui lòng xem hướng dẫn phù hợp với kết luận xét duyệt.",
                $"diagnosis-recommendations:{row.ReviewId}", "DIAGNOSIS_CASE", row.CaseId, token);
        await context.SaveChangesAsync(token);
    }
}
