using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Application.Features.Audit;

public sealed class AuditLogService(IAgriSageDbContext context, ICurrentUserService currentUser) : IAuditLogService
{
    public async Task<PagedResult<AuditLogResponse>> ListAsync(AuditLogListRequest request, CancellationToken token)
    {
        var scope = await ScopeAsync(token);
        var query = context.AuditLogs.AsNoTracking().Where(a => scope == null || a.StoreId == scope);
        if (!string.IsNullOrWhiteSpace(request.Action)) query = query.Where(a => a.Action == request.Action.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(request.EntityType)) query = query.Where(a => a.EntityType == request.EntityType.Trim().ToUpper());
        if (request.EntityId is { } entity) query = query.Where(a => a.EntityId == entity);
        if (request.ActorUserId is { } actor) query = query.Where(a => a.ActorUserId == actor);
        if (request.From is { } from) query = query.Where(a => a.OccurredAt >= from.ToUniversalTime());
        if (request.To is { } to) query = query.Where(a => a.OccurredAt <= to.ToUniversalTime());
        if (EnumText.TryParse<RoleCode>(request.ActorRole, out var role))
            query = query.Where(a => context.Users.IgnoreQueryFilters().Any(u => u.Id == a.ActorUserId && u.Role.Code == role));
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var failed = request.Status.Trim().Equals("FAILURE", StringComparison.OrdinalIgnoreCase);
            query = query.Where(a => (a.Action.EndsWith("_FAILED") || a.Action == "AUTH_REFRESH_REUSE") == failed);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            Guid? resourceId = Guid.TryParse(term, out var parsedId) ? parsedId : null;
            query = query.Where(a => context.Users.IgnoreQueryFilters().Any(u => u.Id == a.ActorUserId
                    && (u.FullName.ToLower().Contains(term) || (u.Email != null && u.Email.ToLower().Contains(term))))
                || a.Action.ToLower().Contains(term) || a.EntityType.ToLower().Contains(term)
                || (a.Reason != null && a.Reason.ToLower().Contains(term))
                || (resourceId != null && (a.EntityId == resourceId || a.ActorUserId == resourceId)));
        }
        var total = await query.LongCountAsync(token);
        var rows = await Project(query.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip(request.Skip).Take(request.PageSize)).ToListAsync(token);
        return new(rows.Select(Map).ToList(), request.Page, request.PageSize, total);
    }
    public async Task<AuditLogResponse> GetAsync(Guid id, CancellationToken token)
    {
        var scope = await ScopeAsync(token);
        var row = await Project(context.AuditLogs.AsNoTracking().Where(a => a.Id == id && (scope == null || a.StoreId == scope)))
            .SingleOrDefaultAsync(token) ?? throw new NotFoundException("Audit log", id);
        return Map(row);
    }
    private async Task<Guid?> ScopeAsync(CancellationToken token)
    {
        var id = currentUser.UserId ?? throw new AuthenticationFailedException("Authentication is required.");
        if (currentUser.Role == "ADMIN") return null;
        if (currentUser.Role != "STORE_OWNER") throw new ForbiddenException();
        var store = await ActiveStore.GetIdAsync(context, token);
        if (!await context.StoreMembers.AsNoTracking().AnyAsync(m => m.StoreId == store && m.UserId == id
            && m.Status == StoreMemberStatus.Active, token)) throw new ForbiddenException();
        return store;
    }
    private sealed record Row(Guid Id, Guid? StoreId, Guid? ActorUserId, string Action, string EntityType,
        Guid? EntityId, string? OldValues, string? NewValues, string? Reason, string? IpAddress,
        string? UserAgent, string? CorrelationId, DateTimeOffset OccurredAt,
        string? ActorName, string? ActorEmail, RoleCode? ActorRole);
    private IQueryable<Row> Project(IQueryable<AuditLog> query) =>
        from a in query
        join user in context.Users.IgnoreQueryFilters().AsNoTracking() on a.ActorUserId equals (Guid?)user.Id into users
        from user in users.DefaultIfEmpty()
        join role in context.Roles.IgnoreQueryFilters().AsNoTracking() on user.RoleId equals role.Id into roles
        from role in roles.DefaultIfEmpty()
        select new Row(a.Id, a.StoreId, a.ActorUserId, a.Action, a.EntityType, a.EntityId,
            a.OldValues, a.NewValues, a.Reason, a.IpAddress, a.UserAgent, a.CorrelationId, a.OccurredAt,
            user == null ? null : user.FullName, user == null ? null : user.Email,
            role == null ? null : role.Code);
    private static AuditLogResponse Map(Row r) => new(r.Id, r.StoreId, r.ActorUserId, r.Action, r.EntityType, r.EntityId,
        AuditValues.Parse(r.OldValues), AuditValues.Parse(r.NewValues), r.Reason, r.IpAddress, r.UserAgent, r.CorrelationId,
        r.OccurredAt, r.ActorName, r.ActorEmail, r.ActorRole is { } role ? EnumText.Format(role) : null,
        r.Action.EndsWith("_FAILED", StringComparison.Ordinal) || r.Action == "AUTH_REFRESH_REUSE" ? "FAILURE" : "SUCCESS");
}
