using AgriSage.Application;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Dtos.Responses;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Persistence.Interceptors;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

// Independent PostgreSQL connections prove serialization; each test owns and drops a dedicated schema.
public sealed class AuthConcurrencyDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [CreditDbFact]
    public async Task Concurrent_refreshes_allow_one_rotation_and_revoke_the_family_when_the_other_replays()
    {
        await using var db = await Database.StartAsync(Token);
        var original = await db.StartSessionAsync(Token);
        async Task<AuthResponse?> Refresh()
        {
            await using var scope = db.Services.CreateAsyncScope();
            try { return await scope.ServiceProvider.GetRequiredService<IAuthSessionService>().RefreshAsync(original.RefreshToken!, Token); }
            catch (AuthenticationFailedException) { return null; }
        }
        var results = await Task.WhenAll(Refresh(), Refresh());
        Assert.Single(results, r => r is not null);
        await using var check = db.Services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var session = await context.AuthSessions.SingleAsync(s => s.Id == original.SessionId, Token);
        Assert.Equal("REFRESH_TOKEN_REUSE", session.RevocationReason);
        Assert.False(await check.ServiceProvider.GetRequiredService<IUserAccessValidator>()
            .IsSessionAllowedAsync(db.UserId, session.Id, 0, "FARMER", Token));
        Assert.Equal(2, await context.RefreshTokens.CountAsync(t => t.SessionId == session.Id, Token));
    }
    [CreditDbFact]
    public async Task Password_change_racing_refresh_cannot_leave_a_usable_old_version_session()
    {
        await using var db = await Database.StartAsync(Token);
        var original = await db.StartSessionAsync(Token);
        async Task Refresh()
        {
            await using var scope = db.Services.CreateAsyncScope();
            try { await scope.ServiceProvider.GetRequiredService<IAuthSessionService>().RefreshAsync(original.RefreshToken!, Token); }
            catch (AuthenticationFailedException) { }
        }
        async Task Change()
        {
            await using var scope = db.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAuthService>()
                .ChangePasswordAsync(new ChangePasswordRequest("initial-password", "replacement-password"), Token);
        }
        await Task.WhenAll(Refresh(), Change());
        await using var check = db.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<IUserAccessValidator>()
            .IsSessionAllowedAsync(db.UserId, original.SessionId!.Value, 0, "FARMER", Token));
        Assert.Empty(await check.ServiceProvider.GetRequiredService<IAuthSessionService>().ListAsync(Token));
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly string _admin = Environment.GetEnvironmentVariable("AGRISAGE_CREDIT_TEST_CONNECTION_STRING")!;
        private readonly string _schema = "auth_test_" + Guid.NewGuid().ToString("N");
        public ServiceProvider Services { get; private set; } = null!;
        public Guid UserId { get; private set; }
        public static async Task<Database> StartAsync(CancellationToken token)
        {
            var d = new Database();
            try
            {
                await d.SqlAsync($"CREATE SCHEMA {d._schema}", token);
                var current = new RealDb.MutableUser(); var clock = new DateTimeProvider();
                var settings = new NpgsqlConnectionStringBuilder(d._admin) { SearchPath = d._schema, Pooling = false };
                var services = new ServiceCollection(); services.AddApplication();
                services.AddSingleton<ICurrentUserService>(current); services.AddSingleton<IDateTimeProvider>(clock);
                services.AddSingleton(Options.Create(OperationsTestEnvironment.Jwt));
                services.AddOptions<AuthSecurityOptions>();
                services.AddSingleton<IPasswordHashService, PasswordHashService>();
                services.AddSingleton<IAccessTokenService, AccessTokenService>();
                services.AddSingleton<ISecretTokenService, SecretTokenService>();
                services.AddSingleton<IDatabaseErrorClassifier, NpgsqlErrorClassifier>();
                services.AddScoped<IAuthSecurityLock, AuthSecurityLock>();
                services.AddDbContext<AgriSageDbContext>(o => o.UseNpgsql(settings.ConnectionString).AddInterceptors(
                    new SoftDeleteInterceptor(current, clock), new AuditableEntityInterceptor(clock), new ConcurrencyVersionInterceptor()));
                services.AddScoped<IAgriSageDbContext>(p => p.GetRequiredService<AgriSageDbContext>());
                d.Services = services.BuildServiceProvider();
                await using var scope = d.Services.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
                await context.Database.MigrateAsync(token);
                var role = new Role(RoleCode.Farmer, "Farmer");
                var user = new User(role.Id, "Concurrency test", new PasswordHashService().Hash("initial-password"), "auth@example.test", null);
                context.AddRange(role, user); await context.SaveChangesAsync(token);
                d.UserId = user.Id; current.UserId = user.Id; current.Role = "FARMER";
                return d;
            }
            catch { await d.DisposeAsync(); throw; }
        }
        public async Task<AuthResponse> StartSessionAsync(CancellationToken token)
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var user = await context.Users.SingleAsync(u => u.Id == UserId, token);
            var response = scope.ServiceProvider.GetRequiredService<AuthSessionService>().Start(user, "FARMER");
            await context.SaveChangesAsync(token);
            return response;
        }
        private async Task SqlAsync(string sql, CancellationToken token)
        {
            await using var connection = new NpgsqlConnection(_admin); await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(token);
        }
        public async ValueTask DisposeAsync()
        {
            if (Services is not null) await Services.DisposeAsync();
            await SqlAsync($"DROP SCHEMA IF EXISTS {_schema} CASCADE", CancellationToken.None);
        }
    }
}
