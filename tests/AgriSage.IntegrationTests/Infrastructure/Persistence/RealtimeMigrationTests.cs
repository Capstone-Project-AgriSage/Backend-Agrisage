using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public sealed class RealtimeMigrationTests
{
    [Fact]
    public void Migration_only_adds_a_transport_queue_indexes_and_transactional_triggers()
    {
        var operations = new EventDrivenRealtime().UpOperations;
        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("realtime_outbox", table.Name);
        Assert.Contains(table.CheckConstraints, c => c.Sql == "attempts >= 0");
        Assert.Contains(operations.OfType<CreateIndexOperation>(), i => i.IsUnique
            && i.Columns.SequenceEqual(new[] { "transaction_id", "topic", "scope", "subject_id" }));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), i => i.Filter == "processed_at IS NULL AND deleted_at IS NULL");
        Assert.DoesNotContain(operations, o => o is DropTableOperation or DropColumnOperation or AlterColumnOperation);
        Assert.Equal(RealtimeOutboxSql.Install, Assert.IsType<SqlOperation>(operations[^1]).Sql);
        Assert.Equal(RealtimeOutboxSql.Uninstall, Assert.IsType<SqlOperation>(new EventDrivenRealtime().DownOperations[0]).Sql);
    }
}
