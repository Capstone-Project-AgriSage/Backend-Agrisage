using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Notifications;

internal static class NotificationRecipients
{
    public static Task<List<Guid>> StaffAsync(IAgriSageDbContext context, Guid storeId, CancellationToken token,
        params RoleCode[] roles) => context.StoreMembers.AsNoTracking().Where(m => m.StoreId == storeId
            && m.Status == StoreMemberStatus.Active && m.User.Status == UserStatus.Active
            && m.User.Role.IsActive && m.User.Role.DeletedAt == null && roles.Contains(m.User.Role.Code))
            .Select(m => m.UserId).Distinct().ToListAsync(token);
}
