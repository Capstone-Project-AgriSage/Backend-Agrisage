using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Audit.Entities;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Application.Features.Notifications;

namespace AgriSage.Application.Common;

// Business audit trail (audit_logs, database design §67). A shared step (api-flows README §4.9): it only adds the row
// to the caller's unit of work, so the audit is saved — or rolled back — together with the change it describes.
// The actor is the current user. Values are serialized as camelCase JSON; never pass secrets or password hashes.
public sealed class AuditTrail(IAgriSageDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock,
    IAuditRequestMetadata? requestMetadata = null)
{
    public AuditLog Record(
        string action,
        string entityType,
        Guid? entityId,
        Guid? storeId,
        object? oldValues = null,
        object? newValues = null,
        string? reason = null) => Add(action, entityType, entityId, storeId, currentUser.UserId, oldValues, newValues, reason);

    // Authentication use cases supply the user verified by the server, before a JWT exists.
    public AuditLog RecordAuthentication(string action, Guid authenticatedUserId, object? newValues = null) =>
        Add(action, "USER", authenticatedUserId, null, authenticatedUserId, null, newValues, null);

    private AuditLog Add(string action, string entityType, Guid? entityId, Guid? storeId, Guid? actorUserId,
        object? oldValues, object? newValues, string? reason)
    {
        var log = new AuditLog(
            action,
            entityType,
            clock.UtcNow,
            storeId,
            actorUserId,
            entityId,
            AuditValues.Serialize(oldValues),
            AuditValues.Serialize(newValues),
            reason,
            requestMetadata?.IpAddress,
            requestMetadata?.UserAgent,
            requestMetadata?.CorrelationId);
        context.AuditLogs.Add(log);
        if (BusinessNotificationPolicy.Supports(action))
            context.NotificationOutbox.Add(new NotificationOutbox(log.Id, clock.UtcNow));

        return log;
    }
}
