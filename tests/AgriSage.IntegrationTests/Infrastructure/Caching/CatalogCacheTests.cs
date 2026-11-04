using AgriSage.Application.Common.Models;
using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Advisory;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Diagnosis;
using AgriSage.Application.Features.Pricing;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Domain.Features.Diagnosis.Entities;
using AgriSage.Domain.Features.Diagnosis.Enums;
using AgriSage.Infrastructure.Caching;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Caching;

public class CatalogCacheTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static AgriSageDbContext Context(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AgriSageDbContext>().UseNpgsql().AddInterceptors(interceptors).Options);
    private sealed record Item(string Name);
    private sealed class Backend : ICatalogCacheBackend
    {
        public string? Generation { get; set; } = "initial";
        public Dictionary<string, string> Values { get; } = [];
        public int Invalidations { get; private set; }
        public Task<string?> GenerationAsync(CancellationToken token) => Task.FromResult(Generation);
        public Task<string?> GetAsync(string key, CancellationToken token) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken token) { Values[key] = value; return Task.CompletedTask; }
        public Task InvalidateAsync() { Generation = Guid.NewGuid().ToString(); Invalidations++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Repeated_read_uses_cached_DTO_and_keeps_filters_and_public_scope_separate()
    {
        await using var context = Context();
        var backend = new Backend();
        var cache = new CatalogReadCache(backend, context);
        var calls = 0;
        Task<PagedResult<Item>> Load() => Task.FromResult(new PagedResult<Item>([new($"load-{++calls}")], 1, 20, 1));
        var first = await cache.GetOrCreateAsync("brands", new { page = 1, active = true }, Load, Token);
        var second = await cache.GetOrCreateAsync("brands", new { page = 1, active = true }, Load, Token);
        Assert.Equal(first.Items[0], second.Items[0]);
        Assert.Equal(1, calls);
        await cache.GetOrCreateAsync("brands", new { page = 2, active = true }, Load, Token);
        await cache.GetOrCreateAsync("brands", new { page = 1, active = false }, Load, Token);
        await cache.GetOrCreateAsync("public-brands", new { page = 1, active = true }, Load, Token);
        Assert.Equal(4, calls);
        Assert.DoesNotContain(backend.Values.Keys, key => key.Contains("brands"));
    }

    [Fact]
    public async Task Invalidation_during_a_slow_load_cannot_repopulate_the_current_generation()
    {
        await using var context = Context();
        var backend = new Backend();
        var cache = new CatalogReadCache(backend, context);
        await cache.GetOrCreateAsync("products", new { }, async () =>
        {
            await backend.InvalidateAsync();
            return new Item("old");
        }, Token);
        var fresh = await cache.GetOrCreateAsync("products", new { }, () => Task.FromResult(new Item("fresh")), Token);
        Assert.Equal("fresh", fresh.Name);
    }

    [Fact]
    public async Task Missing_Redis_and_invalid_cached_JSON_fall_back_to_loader()
    {
        await using var context = Context();
        var backend = new Backend { Generation = null };
        var cache = new CatalogReadCache(backend, context);
        Assert.Equal("db", (await cache.GetOrCreateAsync("brands", new { }, () => Task.FromResult(new Item("db")), Token)).Name);
        Assert.Empty(backend.Values);
        backend.Generation = "initial";
        await cache.GetOrCreateAsync("brands", new { }, () => Task.FromResult(new Item("old")), Token);
        backend.Values[backend.Values.Keys.Single()] = "invalid-json";
        Assert.Equal("new", (await cache.GetOrCreateAsync("brands", new { }, () => Task.FromResult(new Item("new")), Token)).Name);
    }

    [Fact]
    public async Task Loader_failures_are_not_swallowed_or_cached()
    {
        await using var context = Context();
        var backend = new Backend();
        var cache = new CatalogReadCache(backend, context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync<Item>("brands", new { },
            () => throw new InvalidOperationException("database failure"), Token));
        Assert.Empty(backend.Values);
    }

    [Fact]
    public async Task Ambient_transaction_bypasses_shared_cache()
    {
        await using var context = Context();
        var backend = new Backend();
        var cache = new CatalogReadCache(backend, context);
        using var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        await cache.GetOrCreateAsync("brands", new { }, () => Task.FromResult(new Item("private")), Token);
        Assert.Empty(backend.Values);
    }

    [Fact]
    public async Task SaveChanges_invalidates_after_success_and_rollback_discards_pending_invalidation()
    {
        var backend = new Backend();
        var state = new CatalogCacheInvalidation(backend);
        await using var context = Context(new CatalogCacheSaveInterceptor(state), new SuppressWrite());
        context.Brands.Add(new Brand("Changed"));
        Assert.Equal(0, backend.Invalidations);
        await context.SaveChangesAsync(Token);
        Assert.Equal(1, backend.Invalidations);
        context.ChangeTracker.AcceptAllChanges();
        await context.SaveChangesAsync(Token);
        Assert.Equal(1, backend.Invalidations);
        context.Brands.Add(new Brand("Rolled back"));
        state.Capture(context);
        state.Reset();
        await state.FlushAsync();
        Assert.Equal(1, backend.Invalidations);
    }

    private sealed class SuppressWrite : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
    }

    [Theory]
    [InlineData("packaging")]
    [InlineData("ingredient-link")]
    [InlineData("disease")]
    [InlineData("treatment")]
    public async Task Reference_children_invalidate_cached_DTOs_after_successful_save(string kind)
    {
        var backend = new Backend();
        var state = new CatalogCacheInvalidation(backend);
        await using var context = Context(new CatalogCacheSaveInterceptor(state), new SuppressWrite());
        var product = new Product(Guid.NewGuid(), "CACHE-TEST", "Product");
        object entity = kind switch
        {
            "packaging" => product.AddPackaging(Guid.NewGuid(), 1, true, true, true, "ACTIVE"),
            "ingredient-link" => new ProductActiveIngredient(product.Id, Guid.NewGuid(), "10%"),
            "disease" => new Disease("CACHE_TEST", "Problem"),
            _ => new DiseaseTreatment(Guid.NewGuid(), TreatmentType.Cultural, "Care", "Instructions")
        };
        // Track the child alone: edits need to invalidate even when the Product/Disease parent is unchanged.
        context.Entry(entity).State = EntityState.Added;
        var cache = new CatalogReadCache(backend, context);
        await cache.GetOrCreateAsync("reference", new { }, () => Task.FromResult(new Item("old")), Token);
        await context.SaveChangesAsync(Token);
        Assert.Equal(1, backend.Invalidations);
        var fresh = await cache.GetOrCreateAsync("reference", new { }, () => Task.FromResult(new Item("fresh")), Token);
        Assert.Equal("fresh", fresh.Name);

        context.ChangeTracker.AcceptAllChanges();
        context.Entry(entity).State = EntityState.Modified;
        await context.SaveChangesAsync(Token);
        Assert.Equal(2, backend.Invalidations);
        context.Entry(entity).State = EntityState.Deleted;
        await context.SaveChangesAsync(Token);
        Assert.Equal(3, backend.Invalidations);
    }

    [Fact]
    public async Task Readonly_DTO_collections_roundtrip_without_reloading_and_care_keys_are_per_disease()
    {
        await using var context = Context();
        var cache = new CatalogReadCache(new Backend(), context);
        var id = Guid.NewGuid();
        var loads = 0;
        Task<IReadOnlyList<Item>> Load()
        {
            loads++;
            return Task.FromResult<IReadOnlyList<Item>>([new("Care")]);
        }
        await cache.GetOrCreateAsync("advisory-care", new { diseaseId = id }, Load, Token);
        var result = await cache.GetOrCreateAsync("advisory-care", new { diseaseId = id }, Load, Token);
        Assert.Equal("Care", Assert.Single(result).Name);
        Assert.Equal(1, loads);
        await cache.GetOrCreateAsync("advisory-care", new { diseaseId = Guid.NewGuid() }, Load, Token);
        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Disease_service_uses_cached_content_with_the_requested_active_filter()
    {
        await using var context = Context();
        var cache = new CatalogReadCache(new Backend(), context);
        IReadOnlyList<DiseaseResponse> active = [new(Guid.NewGuid(), "PROBLEM", "Problem", null, "RICE",
            null, "Symptoms", null, "Prevention", false, true, [])];
        await cache.GetOrCreateAsync("diseases", new { isActive = (bool?)true }, () => Task.FromResult(active), Token);
        await cache.GetOrCreateAsync<IReadOnlyList<DiseaseResponse>>("diseases", new { isActive = (bool?)false },
            () => Task.FromResult<IReadOnlyList<DiseaseResponse>>([]), Token);
        var service = new DiseaseService(context, cache: cache);
        Assert.Equal("PROBLEM", Assert.Single(await service.ListAsync(true, Token)).Code);
        Assert.Empty(await service.ListAsync(false, Token));
    }

    private sealed class AnonymousUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public bool IsAuthenticated => false;
    }

    [Fact]
    public async Task Advisory_problem_lookup_still_authenticates_before_accessing_shared_cache()
    {
        await using var context = Context();
        var backend = new Backend();
        var user = new AnonymousUser();
        var clock = new DateTimeProvider();
        var service = new MyAdvisoryService(context, new CurrentFarmer(context, user),
            new AdvisoryEngine(context, new PriceResolver(context), clock), clock, new AuditTrail(context, user, clock),
            new CatalogReadCache(backend, context));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => service.ProblemsAsync(new(), Token));
        Assert.Empty(backend.Values);
    }

    [Fact]
    public async Task Unconfigured_backend_is_optional_and_has_no_connection_side_effects()
    {
        await using var backend = new RedisCatalogCacheBackend(Options.Create(new RedisOptions()), NullLogger<RedisCatalogCacheBackend>.Instance);
        Assert.Null(await backend.GenerationAsync(Token));
        await backend.InvalidateAsync();
    }

    [Fact]
    public async Task Unreachable_Redis_is_a_cache_miss_and_does_not_fail_committed_writes()
    {
        await using var backend = new RedisCatalogCacheBackend(Options.Create(new RedisOptions
        {
            Url = "rediss://default:test-password@127.0.0.1:1", TimeoutMilliseconds = 500,
        }), NullLogger<RedisCatalogCacheBackend>.Instance);
        Assert.Null(await backend.GenerationAsync(Token));
        await backend.InvalidateAsync();
        Assert.Null(await backend.GetAsync("test", Token));
    }

    [Fact]
    public void Rediss_URL_is_parsed_with_TLS_and_does_not_require_StackExchange_connection_string_format()
    {
        var config = new RedisOptions { Url = "rediss\\://default:test%3Asecret@redis.example:6379" }.BuildConfiguration()!;
        Assert.True(config.Ssl);
        Assert.Equal("redis.example", config.SslHost);
        Assert.Equal("default", config.User);
        Assert.Equal("test:secret", config.Password);
        Assert.False(config.AbortOnConnectFail);
        Assert.False(config.AllowAdmin);
    }

    [Theory]
    [InlineData("redis://default:password@redis.example:6379")]
    [InlineData("https://default:password@redis.example:6379")]
    [InlineData("rediss://redis.example:6379")]
    [InlineData("invalid-secret-string")]
    public void Invalid_config_raises_a_sanitized_error(string url)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new RedisOptions { Url = url }.BuildConfiguration());
        Assert.DoesNotContain(url, error.Message);
    }
}
