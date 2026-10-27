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
    public async Task StockAsync(int batchSize, CancellationToken token, Guid? movementId = null)
    {
        var store = await ActiveStore.GetIdAsync(context, token);
        var owners = await NotificationRecipients.StaffAsync(context, store, token, RoleCode.StoreOwner);
        if (owners.Count == 0) return;
        var rows = await context.StockMovements.AsNoTracking().Where(m => (movementId == null || m.Id == movementId) && m.StoreId == store && m.PostedAt != null
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

    // One alert per case once its AI result exists (AI_DIAGNOSIS.md 2.0, section 8). There is no Human Review any more, so
    // no review or recommendation alert; the text differs when the AI was not sure (D2).
    public async Task DiagnosisAsync(int batchSize, CancellationToken token, Guid? caseId = null)
    {
        var cases = context.DiagnosisCases.AsNoTracking().Where(c => (caseId == null || c.Id == caseId) && c.FarmerProfile.User.Status == UserStatus.Active
            && c.FarmerProfile.User.Role.IsActive);
        var ai = await cases.Where(c => (c.Status == DiagnosisCaseStatus.AiCompleted || c.Status == DiagnosisCaseStatus.UnderReview
                || c.Status == DiagnosisCaseStatus.Verified || c.Status == DiagnosisCaseStatus.Inconclusive)
            && context.AiInferences.Any(i => i.DiagnosisCaseId == c.Id && i.Status == AiInferenceStatus.Success)
            && !context.Notifications.IgnoreQueryFilters().Any(n => n.UserId == c.FarmerProfile.UserId
                && n.DeduplicationKey == "diagnosis-ai:" + c.Id.ToString()))
            .OrderBy(c => c.SubmittedAt).ThenBy(c => c.Id).Take(batchSize)
            .Select(c => new
            {
                c.Id,
                c.CaseNumber,
                c.FarmerProfile.UserId,
                Passed = context.AiInferences
                    .Where(i => i.DiagnosisCaseId == c.Id && i.Status == AiInferenceStatus.Success)
                    .OrderByDescending(i => i.InferredAt).ThenByDescending(i => i.Id)
                    .Select(i => (bool?)i.PassedPolicy)
                    .FirstOrDefault()
            })
            .ToListAsync(token);
        foreach (var row in ai)
            await writer.AddAsync([row.UserId], "AI_DIAGNOSIS_COMPLETED", "Đã có kết quả chẩn đoán",
                row.Passed == true
                    ? $"Hồ sơ {row.CaseNumber} đã có kết quả chẩn đoán từ AI. Mở để xem bệnh và cách xử lý."
                    : $"Hồ sơ {row.CaseNumber}: AI chưa chắc chắn về bệnh. Mở để xem gợi ý; nên chụp lại ảnh rõ hơn hoặc hỏi cửa hàng.",
                $"diagnosis-ai:{row.Id}", "DIAGNOSIS_CASE", row.Id, token);
        await context.SaveChangesAsync(token);
    }
}
