using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Dtos.Responses;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgriSage.Application.Features.Auth.Services;

public sealed class AuthSessionService(IAgriSageDbContext context, IAccessTokenService accessTokens,
    ISecretTokenService secrets, IAuthSecurityLock locks, ICurrentUserService currentUser,
    IDateTimeProvider clock, IOptions<AuthSecurityOptions> options, AuditTrail audit) : IAuthSessionService
{
    // Shared step: register/login owns the save and transaction.
    public AuthResponse Start(User user, string roleCode)
    {
        var session = new AuthSession(user.Id, user.SecurityVersion, clock.UtcNow,
            clock.UtcNow.AddDays(options.Value.SessionDays));
        context.AuthSessions.Add(session);
        return Issue(user, roleCode, session);
    }

    private AuthResponse Issue(User user, string roleCode, AuthSession session)
    {
        var raw = secrets.GenerateToken();
        context.RefreshTokens.Add(new RefreshToken(session.Id, secrets.HashToken(raw), session.ExpiresAt));
        var access = accessTokens.Issue(user.Id, roleCode, session.Id, user.SecurityVersion);
        return new AuthResponse(access.Value, access.ExpiresAt,
            new AuthUserResponse(user.Id, user.FullName, user.PhoneNumber, user.Email, roleCode),
            raw, session.ExpiresAt, session.Id);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken token)
    {
        var hash = secrets.HashToken(refreshToken);
        var target = await (from r in context.RefreshTokens.AsNoTracking()
                            join s in context.AuthSessions.AsNoTracking() on r.SessionId equals s.Id
                            where r.TokenHash == hash
                            select new { s.Id, s.UserId }).SingleOrDefaultAsync(token);
        if (target is null) throw InvalidRefresh();

        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockUserAsync(target.UserId, token);
        await locks.LockSessionAsync(target.Id, token);
        var user = await context.Users.Include(u => u.Role).SingleOrDefaultAsync(u => u.Id == target.UserId, token);
        var session = await context.AuthSessions.SingleOrDefaultAsync(s => s.Id == target.Id, token);
        var stored = await context.RefreshTokens.SingleOrDefaultAsync(r => r.TokenHash == hash, token);
        if (user is null || session is null || stored is null) throw InvalidRefresh();

        if (stored.ConsumedAt is not null)
        {
            if (session.RevokedAt is null)
            {
                session.Revoke(clock.UtcNow, "REFRESH_TOKEN_REUSE");
                audit.Record("AUTH_REFRESH_REUSE", "AUTH_SESSION", session.Id, null);
                await context.SaveChangesAsync(token);
            }
            await tx.CommitAsync(token); // persist family revocation before reporting the failed refresh
            throw InvalidRefresh();
        }

        if (user.Status != UserStatus.Active || !user.Role.IsActive
            || !session.IsUsable(clock.UtcNow, user.SecurityVersion) || stored.ExpiresAt <= clock.UtcNow)
            throw InvalidRefresh();

        stored.Consume(clock.UtcNow);
        session.Touch(clock.UtcNow);
        var response = Issue(user, RoleCodeFormat.ToText(user.Role.Code), session);
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return response;
    }

    public async Task<IReadOnlyList<AuthSessionResponse>> ListAsync(CancellationToken token)
    {
        var userId = RequiredUser();
        var version = await context.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => (long?)u.SecurityVersion).SingleOrDefaultAsync(token) ?? throw InvalidRefresh();
        var now = clock.UtcNow;
        var currentSession = currentUser.SessionId;
        return await context.AuthSessions.AsNoTracking().Where(s => s.UserId == userId && s.RevokedAt == null
                && s.ExpiresAt > now && s.SecurityVersion == version)
            .OrderByDescending(s => s.LastUsedAt).ThenBy(s => s.Id)
            .Select(s => new AuthSessionResponse(s.Id, s.CreatedAt, s.LastUsedAt, s.ExpiresAt, s.Id == currentSession))
            .ToListAsync(token);
    }

    public Task LogoutAsync(CancellationToken token) =>
        RevokeAsync(currentUser.SessionId ?? throw InvalidRefresh(), token);

    public async Task RevokeAsync(Guid sessionId, CancellationToken token)
    {
        var userId = RequiredUser();
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockUserAsync(userId, token);
        await locks.LockSessionAsync(sessionId, token);
        var session = await context.AuthSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, token)
            ?? throw new NotFoundException("Session", sessionId);
        if (session.RevokedAt is null)
        {
            session.Revoke(clock.UtcNow, "USER_LOGOUT");
            audit.Record("AUTH_SESSION_REVOKED", "AUTH_SESSION", session.Id, null);
            await context.SaveChangesAsync(token);
        }
        await tx.CommitAsync(token);
    }

    public async Task LogoutAllAsync(CancellationToken token)
    {
        var userId = RequiredUser();
        await using var tx = await context.BeginTransactionAsync(token);
        await locks.LockUserAsync(userId, token);
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId, token) ?? throw InvalidRefresh();
        user.InvalidateSessions();
        foreach (var session in await context.AuthSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(token))
            session.Revoke(clock.UtcNow, "USER_LOGOUT_ALL");
        audit.Record("AUTH_LOGOUT_ALL", "USER", userId, null);
        await context.SaveChangesAsync(token);
        await tx.CommitAsync(token);
    }

    private Guid RequiredUser() => currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
    private static AuthenticationFailedException InvalidRefresh() => new("The session or refresh token is invalid.");
}
