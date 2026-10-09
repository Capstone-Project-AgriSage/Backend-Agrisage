using System.Text.Json;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Audit.Entities;
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
        string? UserAgent, string? CorrelationId, DateTimeOffset OccurredAt);
    private static IQueryable<Row> Project(IQueryable<AuditLog> query) => query.Select(a => new Row(a.Id, a.StoreId,
        a.ActorUserId, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues, a.Reason, a.IpAddress,
        a.UserAgent, a.CorrelationId, a.OccurredAt));
    private static AuditLogResponse Map(Row r) => new(r.Id, r.StoreId, r.ActorUserId, r.Action, r.EntityType, r.EntityId,
        Parse(r.OldValues), Parse(r.NewValues), r.Reason, r.IpAddress, r.UserAgent, r.CorrelationId, r.OccurredAt);
    private static JsonElement? Parse(string? value) => value is null ? null : JsonSerializer.Deserialize<JsonElement>(value);
}
