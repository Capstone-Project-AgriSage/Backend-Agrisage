using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public sealed class DebtSmsOutboxMigrationTests
{
    [Fact]
    public void Migration_adds_only_the_durable_sms_queue_and_unique_milestone_key()
    {
        var operations = new DebtSmsOutbox().UpOperations;
        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("debt_sms_outbox", table.Name);
        Assert.Contains(operations.OfType<CreateIndexOperation>(), i => i.IsUnique
            && i.Table == "debt_sms_outbox" && i.Columns.SequenceEqual(["deduplication_key"]));
        Assert.DoesNotContain(operations, o => o is DropTableOperation or DropColumnOperation);
    }
}
