using System.Text.Json;
using AgriSage.Application.Common.Models;

namespace AgriSage.Application.Features.Audit;

public sealed record AuditLogListRequest : PaginationRequest
{
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? Search { get; init; }
    public string? ActorRole { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
public sealed record AuditLogResponse(Guid Id, Guid? StoreId, Guid? ActorUserId, string Action, string EntityType,
    Guid? EntityId, JsonElement? OldValues, JsonElement? NewValues, string? Reason, string? IpAddress,
    string? UserAgent, string? CorrelationId, DateTimeOffset OccurredAt,
    string? ActorName = null, string? ActorEmail = null, string? ActorRole = null, string Status = "SUCCESS");
public interface IAuditLogService
{
    Task<PagedResult<AuditLogResponse>> ListAsync(AuditLogListRequest request, CancellationToken token);
    Task<AuditLogResponse> GetAsync(Guid id, CancellationToken token);
}
