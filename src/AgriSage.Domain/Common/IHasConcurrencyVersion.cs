namespace AgriSage.Domain.Common;

// Entity backed by a `version bigint` column (database design §0.8). The version is mapped as an
// optimistic concurrency token and incremented on save by Infrastructure (§35.15), not by Domain code.
public interface IHasConcurrencyVersion
{
    long Version { get; }
}
