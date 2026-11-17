using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Infrastructure.Persistence;

namespace AgriSage.Infrastructure.Caching;

public sealed class CatalogReadCache(ICatalogCacheBackend backend, AgriSageDbContext context) : ICatalogReadCache
{
    public async Task<T> GetOrCreateAsync<T>(string area, object parameters, Func<Task<T>> load,
        CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Do not read shared cache or publish uncommitted rows from a transaction.
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            return await load();
        var generation = await backend.GenerationAsync(cancellationToken);
        if (string.IsNullOrEmpty(generation)) return await load();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { area, parameters, type = typeof(T).FullName }))));
        var key = generation + ":" + hash;
        var cached = await backend.GetAsync(key, cancellationToken);
        if (cached is not null)
        {
            try
            {
                var value = JsonSerializer.Deserialize<T>(cached);
                if (value is not null) return value;
            }
            catch (JsonException) { /* Old/incompatible payloads are cache misses. */ }
        }
        // DB exceptions propagate normally. A concurrent write changes the generation, so a slow
        // reader can only fill the obsolete namespace (which expires), never resurrect stale rows.
        var result = await load();
        await backend.SetAsync(key, JsonSerializer.Serialize(result), cancellationToken);
        return result;
    }
}
