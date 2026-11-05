using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AgriSage.Infrastructure.Caching;

// One lazily opened multiplexer for the process. Missing/unavailable Redis is a cache miss.
public sealed class RedisCatalogCacheBackend : ICatalogCacheBackend, IAsyncDisposable
{
    private readonly RedisOptions options;
    private readonly ILogger<RedisCatalogCacheBackend> logger;
    private readonly Lazy<Task<ConnectionMultiplexer>>? connection;
    private long retryAfter;
    public RedisCatalogCacheBackend(IOptions<RedisOptions> options, ILogger<RedisCatalogCacheBackend> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        var configuration = this.options.BuildConfiguration();
        if (configuration is not null) connection = new(() => ConnectionMultiplexer.ConnectAsync(configuration));
    }

    private bool Available => connection is not null && Environment.TickCount64 >= Interlocked.Read(ref retryAfter);
    private void Unavailable(bool invalidation = false)
    {
        // If invalidation fails, bypass old entries until their maximum TTL has elapsed.
        var deadline = Environment.TickCount64 + (invalidation ? Math.Clamp(options.TtlSeconds, 10, 600) * 1000L : 10_000);
        long previous;
        do { previous = Interlocked.Read(ref retryAfter); }
        while (previous < deadline && Interlocked.CompareExchange(ref retryAfter, deadline, previous) != previous);
        logger.LogWarning("Redis catalog cache unavailable; reading the database directly.");
    }
    private async Task<IDatabase> DatabaseAsync(CancellationToken token) =>
        (await connection!.Value.WaitAsync(token).ConfigureAwait(false)).GetDatabase();

    public async Task<string?> GenerationAsync(CancellationToken cancellationToken)
    {
        if (!Available) return null;
        try
        {
            var database = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
            var key = options.KeyPrefix + "generation";
            var generation = await database.StringGetAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (generation.HasValue) return generation.ToString();
            await database.StringSetAsync(key, Guid.NewGuid().ToString("N"), when: When.NotExists).WaitAsync(cancellationToken).ConfigureAwait(false);
            return (await database.StringGetAsync(key).WaitAsync(cancellationToken).ConfigureAwait(false)).ToString();
        }
        catch (RedisException) { Unavailable(); return null; }
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (!Available) return null;
        try
        {
            var database = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
            var value = await database.StringGetAsync(options.KeyPrefix + key).WaitAsync(cancellationToken).ConfigureAwait(false);
            return value.HasValue ? value.ToString() : null;
        }
        catch (RedisException) { Unavailable(); return null; }
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (!Available) return;
        try
        {
            var database = await DatabaseAsync(cancellationToken).ConfigureAwait(false);
            await database.StringSetAsync(options.KeyPrefix + key, value,
                TimeSpan.FromSeconds(Math.Clamp(options.TtlSeconds, 10, 600))).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RedisException) { Unavailable(); }
    }

    public async Task InvalidateAsync()
    {
        if (connection is null) return;
        try
        {
            // Not tied to an HTTP cancellation: the database has already committed.
            var database = await DatabaseAsync(CancellationToken.None).ConfigureAwait(false);
            await database.StringSetAsync(options.KeyPrefix + "generation", Guid.NewGuid().ToString("N")).ConfigureAwait(false);
        }
        catch (RedisException) { Unavailable(invalidation: true); }
    }

    public async ValueTask DisposeAsync()
    {
        if (connection?.IsValueCreated == true)
        {
            try { await (await connection.Value.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false); }
            catch (RedisException) { /* Never turn an optional cache failure into a shutdown failure. */ }
        }
    }
}
