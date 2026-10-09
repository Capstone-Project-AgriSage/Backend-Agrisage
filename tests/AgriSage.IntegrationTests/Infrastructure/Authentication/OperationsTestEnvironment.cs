using AgriSage.Application.Common;
using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using AgriSage.Domain.Features.Stores.Enums;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

internal sealed class OperationsTestEnvironment : IAsyncDisposable
{
    public required RealDb.Session Database { get; init; }
    public required AgriSageDbContext Context { get; init; }
    public required User User { get; init; }
    public required Store Store { get; init; }
    public required FarmerProfile Farmer { get; init; }
    public RealDb.MutableUser Current => Database.CurrentUser;
    public Clock Time { get; } = new();
    public Sender Messages { get; } = new();
    public PasswordHashService Passwords { get; } = new();
    public static JwtOptions Jwt => new() { Issuer = "AgriSage", Audience = "AgriSage.Clients",
        SigningKey = "operations-test-signing-key-not-a-secret-000000", AccessTokenMinutes = 60 };
    public SecretTokenService Secrets => new(Options.Create(Jwt));
    public AuditTrail Audit => new(Context, Current, Time);
    public AuthSecurityLock Locks => new(Context);
    public AuthSessionService Sessions => new(Context, new AccessTokenService(Options.Create(Jwt), Time), Secrets,
        Locks, Current, Time, Options.Create(new AuthSecurityOptions()), Audit);
    public AuthChallengeService Challenges => new(Context, Secrets, Messages, Locks, Current, Passwords, Time,
        Options.Create(new AuthSecurityOptions()), Audit);
    public AuthService Auth => new(Context, Passwords, Current, Time, new NpgsqlErrorClassifier(), Audit, Sessions, Locks);
    public UserAccessValidator Validator => new(Context, Time);

    public static async Task<OperationsTestEnvironment> CreateAsync()
    {
        var db = await RealDb.Session.StartAsync();
        var context = db.NewContext();
        var (user, store, farmer) = await db.SeedAsync(context);
        var existing = await context.Stores.Where(s => s.Status == StoreStatus.Active && s.Id != store.Id).FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        if (existing is not null) { store.ChangeStatus(StoreStatus.Inactive); store = existing; }
        user.ChangePasswordHash(new PasswordHashService().Hash("initial-password"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.CurrentUser.Role = "FARMER";
        return new() { Database = db, Context = context, User = user, Store = store, Farmer = farmer };
    }
    public async Task<AgriSage.Application.Features.Auth.Dtos.Responses.AuthResponse> StartAsync()
    {
        var result = Sessions.Start(User, "FARMER");
        await Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        Current.SessionId = result.SessionId;
        return result;
    }
    public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await Database.DisposeAsync(); }
    public sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }
    public sealed class Sender : IAuthMessageSender
    {
        public bool Configured { get; set; } = true;
        public bool Fail { get; set; }
        public List<AuthMessage> Sent { get; } = [];
        public bool IsConfigured(AgriSage.Domain.Features.Identity.Enums.AuthDeliveryChannel channel) => Configured;
        public Task SendAsync(AuthMessage message, CancellationToken cancellationToken)
        {
            if (Fail) throw new AgriSage.Application.Common.Exceptions.MessageDeliveryUnavailableException();
            Sent.Add(message); return Task.CompletedTask;
        }
    }
}
