using System.Text.Json;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AgriSage.IntegrationTests.Api;

// Hosts the real API over the isolated schema of CreditTestDatabase for the main-workflow end-to-end tests (BM04 WF-01..05):
// real JWT validation, real permission evaluator on the database, real DI; payOS in simulated mode (its real status path),
// a fake photo store, no background workers.
internal sealed class EndToEndHost : IAsyncDisposable
{
    private readonly CreditTestDatabase _db;

    private EndToEndHost(CreditTestDatabase db, WebApplicationFactory<Program> app, MovableClock clock)
    {
        _db = db;
        App = app;
        Clock = clock;
    }

    public WebApplicationFactory<Program> App { get; }

    // The API's clock: real time plus an offset a test may move forward (for rules that depend on elapsed time).
    public MovableClock Clock { get; }

    public sealed class MovableClock : IDateTimeProvider
    {
        private TimeSpan _offset;

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + _offset;

        public void Advance(TimeSpan by) => _offset += by;
    }

    // `replace` may swap further services for one test (for example a sender that keeps the SMS codes).
    public static EndToEndHost Start(StaffHttpTests.StaffApiFactory factory, CreditTestDatabase e, Action<IServiceCollection>? replace = null)
    {
        var clock = new MovableClock();
        return new(e, factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PayOS:Mode", "Simulated");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDateTimeProvider>();
                services.AddSingleton<IDateTimeProvider>(clock);
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IPermissionEvaluator>();
                services.AddScoped<IPermissionEvaluator>(sp => sp.GetRequiredService<PermissionEvaluator>());
                services.RemoveAll<IUserAccessValidator>();
                services.AddScoped<IUserAccessValidator, UserAccessValidator>();
                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService>(new FilesHttpTests.FakeStorage());
                services.RemoveAll<DbContextOptions<AgriSageDbContext>>();
                services.RemoveAll<AgriSageDbContext>();
                services.AddDbContext<AgriSageDbContext>((p, o) => o.UseNpgsql(e.ConnectionString).AddInterceptors(
                    p.GetRequiredService<SoftDeleteInterceptor>(), p.GetRequiredService<AuditableEntityInterceptor>(),
                    p.GetRequiredService<ConcurrencyVersionInterceptor>()));
                replace?.Invoke(services);
            });
        }), clock);
    }

    // A signed-in client; the real account validator needs a persisted session matching the user's security version.
    public async Task<HttpClient> ClientAsync(Guid userId, string role, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        Guid sessionId;
        long version;
        using (var s = _db.Scope())
        {
            var context = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            version = (await context.Users.AsNoTracking().SingleAsync(u => u.Id == userId, token)).SecurityVersion;
            var session = new AuthSession(userId, version, now, now.AddHours(1));
            context.AuthSessions.Add(session);
            await context.SaveChangesAsync(token);
            sessionId = session.Id;
        }
        var client = App.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            App.Services.GetRequiredService<IAccessTokenService>().Issue(userId, role, sessionId, version).Value);
        return client;
    }

    // Gives a role its catalogue default permissions (CreditTestDatabase only grants the Store Owner's).
    public async Task GrantDefaultsAsync(RoleCode code, string roleText, CancellationToken token)
    {
        using var s = _db.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Code == code, token);
        foreach (var permission in PermissionCatalog.Entries.Where(p => p.DefaultRoles.Contains(roleText)))
            db.RolePermissions.Add(new RolePermission(role.Id, permission.Id, true));
        await db.SaveChangesAsync(token);
    }

    // A new active member of the test store with the given role (created with its default permissions when missing).
    public async Task<Guid> AddMemberAsync(RoleCode code, string roleText, string name, CancellationToken token)
    {
        bool newRole;
        Guid userId;
        using (var s = _db.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var role = await db.Roles.SingleOrDefaultAsync(r => r.Code == code, token);
            newRole = role is null;
            role ??= db.Roles.Add(new Role(code, roleText)).Entity;
            var user = new User(role.Id, name, "hash", $"{Guid.NewGuid():N}@example.test", null);
            db.Users.Add(user);
            db.StoreMembers.Add(new StoreMember(_db.StoreId, user.Id));
            await db.SaveChangesAsync(token);
            userId = user.Id;
        }
        if (newRole) await GrantDefaultsAsync(code, roleText, token);
        return userId;
    }

    public static async Task<JsonElement> OkAsync(HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.RequestMessage?.Method} " +
                                                   $"{response.RequestMessage?.RequestUri?.AbsolutePath}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();
}
