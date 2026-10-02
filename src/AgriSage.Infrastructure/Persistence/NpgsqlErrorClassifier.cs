using AgriSage.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AgriSage.Infrastructure.Persistence;

public sealed class NpgsqlErrorClassifier : IDatabaseErrorClassifier
{
    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
