using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Audit.Entities;

namespace AgriSage.Application.Common;

// Business audit trail (audit_logs, database design §67). A shared step (api-flows README §4.9): it only adds the row
// to the caller's unit of work, so the audit is saved — or rolled back — together with the change it describes.
// The actor is the current user. Values are serialized as camelCase JSON; never pass secrets or password hashes.
public sealed class AuditTrail(IAgriSageDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AuditLog Record(
        string action,
        string entityType,
        Guid? entityId,
        Guid? storeId,
        object? oldValues = null,
        object? newValues = null,
        string? reason = null)
    {
        var log = new AuditLog(
            action,
            entityType,
            clock.UtcNow,
            storeId,
            currentUser.UserId,
            entityId,
            oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
            newValues is null ? null : JsonSerializer.Serialize(newValues, Json),
            reason);
        context.AuditLogs.Add(log);

        return log;
    }
}
