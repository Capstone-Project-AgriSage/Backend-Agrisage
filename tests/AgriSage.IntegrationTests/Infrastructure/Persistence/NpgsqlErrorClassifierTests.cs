using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

public class NpgsqlErrorClassifierTests
{
    private readonly NpgsqlErrorClassifier _classifier = new();

    private static PostgresException Postgres(string sqlState, string message) => new(message, "FATAL", "FATAL", sqlState);

    public static TheoryData<string, string> ConnectionLimits => new()
    {
        { PostgresErrorCodes.InternalError, "(EMAXCONNSESSION) max clients reached in session mode - max clients are limited to pool_size: 15" },
        { PostgresErrorCodes.InternalError, "(EMAXCLIENTSTRANSACTION) max clients reached in transaction mode" },
        { PostgresErrorCodes.TooManyConnections, "sorry, too many clients already" }
    };

    [Theory]
    [MemberData(nameof(ConnectionLimits))]
    public void A_refused_connection_because_of_a_client_limit_is_recognized(string sqlState, string message)
    {
        Assert.True(_classifier.IsConnectionUnavailable(Postgres(sqlState, message)));
    }

    [Fact]
    public void The_limit_is_recognized_when_it_arrives_wrapped()
    {
        var limit = Postgres(PostgresErrorCodes.TooManyConnections, "sorry, too many clients already");

        Assert.True(_classifier.IsConnectionUnavailable(new DbUpdateException("save failed", limit)));
        Assert.True(_classifier.IsConnectionUnavailable(new InvalidOperationException("outer", new InvalidOperationException("inner", limit))));
    }

    [Fact]
    public void An_exhausted_local_pool_is_recognized()
    {
        const string message = "The connection pool has been exhausted. Either raise 'Maximum Pool Size' (currently 5) or 'Timeout' (currently 15 seconds)";

        Assert.True(_classifier.IsConnectionUnavailable(new InvalidOperationException(message)));
        Assert.True(_classifier.IsConnectionUnavailable(new NpgsqlException(message)));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "duplicate key value violates unique constraint")]
    [InlineData(PostgresErrorCodes.UndefinedTable, "relation \"x\" does not exist")]
    [InlineData(PostgresErrorCodes.InternalError, "an unrelated internal error")]
    public void Other_database_errors_are_not_mistaken_for_a_full_pool(string sqlState, string message)
    {
        Assert.False(_classifier.IsConnectionUnavailable(Postgres(sqlState, message)));
    }

    [Fact]
    public void Ordinary_exceptions_are_not_mistaken_for_a_full_pool()
    {
        Assert.False(_classifier.IsConnectionUnavailable(new InvalidOperationException("Sequence contains no elements")));
        Assert.False(_classifier.IsConnectionUnavailable(new TimeoutException("A command timed out")));
    }
}
