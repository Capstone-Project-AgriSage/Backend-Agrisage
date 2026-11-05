namespace AgriSage.Infrastructure.Caching;

public interface ICatalogCacheBackend
{
    Task<string?> GenerationAsync(CancellationToken cancellationToken);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
    Task InvalidateAsync();
}
