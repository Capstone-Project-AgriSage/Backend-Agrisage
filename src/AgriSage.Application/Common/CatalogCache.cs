using AgriSage.Application.Common.Interfaces;

namespace AgriSage.Application.Common;

internal static class CatalogCache
{
    public static Task<T> ReadAsync<T>(ICatalogReadCache? cache, string area, object parameters,
        Func<Task<T>> load, CancellationToken cancellationToken) where T : class =>
        cache is null ? load() : cache.GetOrCreateAsync(area, parameters, load, cancellationToken);
}
