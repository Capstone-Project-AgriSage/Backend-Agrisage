using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public sealed class AuthOperationsMigrationTests
{
    [Fact]
    public void Up_is_additive_and_creates_only_the_four_reviewed_tables()
    {
        var ops = new AuthSessionsNotificationsOperations().UpOperations;
        Assert.Equal(new[] { "auth_challenges", "auth_sessions", "notification_outbox", "refresh_tokens" },
            ops.OfType<CreateTableOperation>().Select(t => t.Name).Order());
        Assert.DoesNotContain(ops, o => o is DropTableOperation or DropColumnOperation or DropIndexOperation
            or AlterColumnOperation or DeleteDataOperation or UpdateDataOperation);
        Assert.Equal(new[] { "notifications.deduplication_key", "payments.last_reconciliation_attempt_at", "users.security_version" },
            ops.OfType<AddColumnOperation>().Select(c => $"{c.Table}.{c.Name}").Order());
        Assert.All(ops.OfType<CreateTableOperation>().SelectMany(t => t.ForeignKeys), fk =>
            Assert.Contains(fk.OnDelete, new[] { Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict,
                Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.NoAction }));
    }
    [Fact]
    public void Token_hashes_and_notification_keys_are_unique_and_retry_queries_are_indexed()
    {
        var indexes = new AuthSessionsNotificationsOperations().UpOperations.OfType<CreateIndexOperation>().ToList();
        Assert.Contains(indexes, i => i.Table == "refresh_tokens" && i.IsUnique && i.Columns.SequenceEqual(["token_hash"]));
        Assert.Contains(indexes, i => i.Table == "notification_outbox" && i.IsUnique && i.Columns.SequenceEqual(["audit_log_id"]));
        Assert.Contains(indexes, i => i.Table == "notifications" && i.IsUnique && i.Filter == "deduplication_key IS NOT NULL");
        Assert.Contains(indexes, i => i.Table == "notification_outbox" && i.Filter == "processed_at IS NULL AND deleted_at IS NULL");
        Assert.Contains(indexes, i => i.Table == "payments" && i.Filter!.Contains("payment_method = 'PAYOS'"));
        var challenges = new AuthSessionsNotificationsOperations().UpOperations.OfType<CreateTableOperation>()
            .Single(t => t.Name == "auth_challenges");
        Assert.Contains(challenges.CheckConstraints, c => c.Sql == "failed_attempts BETWEEN 0 AND 5");
        Assert.DoesNotContain(challenges.Columns, c => c.Name is "token" or "code");
    }
}
