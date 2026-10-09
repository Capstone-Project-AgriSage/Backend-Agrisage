using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Auth.Services;

public sealed class AuthMaintenanceService(IAgriSageDbContext context, IDateTimeProvider clock)
{
    public async Task ExpireAsync(int batchSize, CancellationToken token)
    {
        var now = clock.UtcNow;
        foreach (var session in await context.AuthSessions.Where(s => s.RevokedAt == null && s.ExpiresAt <= now)
            .OrderBy(s => s.ExpiresAt).ThenBy(s => s.Id).Take(batchSize).ToListAsync(token))
            session.Revoke(now, "SESSION_EXPIRED");
        foreach (var challenge in await context.AuthChallenges.Where(c => c.ConsumedAt == null && c.ExpiresAt <= now)
            .OrderBy(c => c.ExpiresAt).ThenBy(c => c.Id).Take(batchSize).ToListAsync(token))
            challenge.Invalidate(now);
        await context.SaveChangesAsync(token);
    }
}
