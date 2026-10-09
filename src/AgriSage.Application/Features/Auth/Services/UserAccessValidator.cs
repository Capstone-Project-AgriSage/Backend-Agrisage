using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Auth.Services;

public sealed class UserAccessValidator(IAgriSageDbContext context, IDateTimeProvider clock) : IUserAccessValidator
{
    // Deleted users are hidden by the soft-delete query filter, so they are treated as not active.
    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken);

    public async Task<bool> IsSessionAllowedAsync(Guid userId, Guid sessionId, long securityVersion, string role,
        CancellationToken cancellationToken)
    {
        var code = RoleCodeFormat.TryParse(role, out var parsed) ? parsed : (RoleCode?)null;
        if (code is null) return false;
        var now = clock.UtcNow;
        return await (from u in context.Users.AsNoTracking()
                      join s in context.AuthSessions.AsNoTracking() on u.Id equals s.UserId
                      where u.Id == userId && u.Status == UserStatus.Active && u.Role.IsActive && u.Role.Code == code
                          && u.SecurityVersion == securityVersion && s.Id == sessionId && s.SecurityVersion == securityVersion
                          && s.RevokedAt == null && s.ExpiresAt > now
                      select u.Id).AnyAsync(cancellationToken);
    }
}
