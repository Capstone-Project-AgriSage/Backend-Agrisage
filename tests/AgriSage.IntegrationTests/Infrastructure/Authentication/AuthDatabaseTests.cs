using AgriSage.Application.Common;
using System.Security.Claims;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Auth;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Products.Entities;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1). Services run against the real schema inside a transaction that is
// always rolled back. Registration/login are exercised through the services, not over HTTP, so nothing is committed.
public class AuthDatabaseTests
{
    private const string SigningKey = "integration-test-signing-key-not-a-secret-000000";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string RandomPhone() => "09" + Random.Shared.Next(10_000_000, 99_999_999);

    private static async Task EnsureRoleAsync(AgriSageDbContext context, RoleCode code)
    {
        if (!await context.Roles.IgnoreQueryFilters().AnyAsync(r => r.Code == code, Token))
        {
            context.Roles.Add(new Role(code, code.ToString()));
            await context.SaveChangesAsync(Token);
        }
    }

    private static JwtOptions JwtSettings => new()
    {
        Issuer = "AgriSage",
        Audience = "AgriSage.Clients",
        SigningKey = SigningKey,
        AccessTokenMinutes = 60
    };

    private static (AuthService Auth, AdminBootstrapService Admin) Services(AgriSageDbContext context, Guid? currentUserId = null)
    {
        var hasher = new PasswordHashService();
        var clock = new DateTimeProvider();
        var tokens = new AccessTokenService(Options.Create(JwtSettings), clock);
        var current = new RealDb.MutableUser { UserId = currentUserId };
        var errors = new NpgsqlErrorClassifier();

        var audit = new AuditTrail(context, current, clock);
        var locks = new AuthSecurityLock(context);
        var sessions = new AuthSessionService(context, tokens, new SecretTokenService(Options.Create(JwtSettings)), locks,
            current, clock, Options.Create(new AuthSecurityOptions()), audit);
        return (new AuthService(context, hasher, current, clock, errors, audit, sessions, locks),
            new AdminBootstrapService(context, hasher, errors));
    }

    [RealDbFact]
    public async Task Register_by_phone_creates_a_farmer_with_a_profile_and_an_unverified_phone()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Farmer);
        var (auth, _) = Services(context);
        var phone = RandomPhone();

        var response = await auth.RegisterFarmerAsync(
            new RegisterFarmerRequest("  Nguyen Van A ", $"+84{phone[1..]}", null, "password1"), Token);

        await using var reader = session.NewContext();
        var user = await reader.Users.Include(u => u.Role).SingleAsync(u => u.Id == response.User.Id, Token);
        Assert.Equal(phone, user.PhoneNumber);
        Assert.Null(user.Email);
        Assert.Equal("Nguyen Van A", user.FullName);
        Assert.Equal(RoleCode.Farmer, user.Role.Code);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.False(user.PhoneVerified);
        Assert.NotEqual("password1", user.PasswordHash);
        Assert.NotNull(user.LastLoginAt);
        Assert.True(await reader.FarmerProfiles.AnyAsync(f => f.UserId == user.Id, Token));
        Assert.Equal("FARMER", response.User.Role);

        var validated = await new JsonWebTokenHandler().ValidateTokenAsync(response.AccessToken, TokenRules());
        Assert.True(validated.IsValid);
        Assert.Equal(user.Id.ToString(), validated.ClaimsIdentity.FindFirst("sub")?.Value);
    }

    [RealDbFact]
    public async Task Duplicate_phone_in_another_format_or_email_in_another_case_is_a_conflict()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Farmer);
        var (auth, _) = Services(context);
        var phone = RandomPhone();
        var email = $"{Guid.NewGuid():N}@Example.com";
        await auth.RegisterFarmerAsync(new RegisterFarmerRequest("A", phone, email, "password1"), Token);

        await Assert.ThrowsAsync<ConflictException>(() => auth.RegisterFarmerAsync(
            new RegisterFarmerRequest("B", $"{phone[..4]} {phone[4..7]} {phone[7..]}", null, "password1"), Token));
        await Assert.ThrowsAsync<ConflictException>(() => auth.RegisterFarmerAsync(
            new RegisterFarmerRequest("C", null, email.ToUpperInvariant(), "password1"), Token));
    }

    [RealDbFact]
    public async Task Login_succeeds_with_the_phone_in_any_format_and_records_the_login_time()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Farmer);
        var (auth, _) = Services(context);
        var phone = RandomPhone();
        var registered = await auth.RegisterFarmerAsync(new RegisterFarmerRequest("A", phone, null, "password1"), Token);
        var user = await context.Users.SingleAsync(u => u.Id == registered.User.Id, Token);
        var firstLogin = user.LastLoginAt;
        await Task.Delay(20, Token);

        var response = await auth.LoginAsync(new LoginRequest($"+84 {phone[1..]}", "password1"), Token);

        Assert.Equal(registered.User.Id, response.User.Id);
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.True((await context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, Token)).LastLoginAt > firstLogin);
    }

    [RealDbFact]
    public async Task Login_by_email_ignores_case()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Farmer);
        var (auth, _) = Services(context);
        var email = $"{Guid.NewGuid():N}@example.com";
        var registered = await auth.RegisterFarmerAsync(new RegisterFarmerRequest("A", null, email, "password1"), Token);

        var response = await auth.LoginAsync(new LoginRequest(email.ToUpperInvariant(), "password1"), Token);

        Assert.Equal(registered.User.Id, response.User.Id);
    }

    [RealDbFact]
    public async Task Wrong_password_and_unknown_account_fail_with_the_same_message()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Farmer);
        var (auth, _) = Services(context);
        var phone = RandomPhone();
        await auth.RegisterFarmerAsync(new RegisterFarmerRequest("A", phone, null, "password1"), Token);

        var wrong = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            auth.LoginAsync(new LoginRequest(phone, "wrong-password"), Token));
        var unknown = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            auth.LoginAsync(new LoginRequest(RandomPhone(), "password1"), Token));
        var unparsable = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            auth.LoginAsync(new LoginRequest("hello", "password1"), Token));

        Assert.Equal(wrong.Message, unknown.Message);
        Assert.Equal(wrong.Message, unparsable.Message);
    }

    [RealDbFact]
    public async Task Inactive_account_is_forbidden_after_the_password_is_verified()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Farmer);
        var (auth, _) = Services(context);
        var phone = RandomPhone();
        var registered = await auth.RegisterFarmerAsync(new RegisterFarmerRequest("A", phone, null, "password1"), Token);
        (await context.Users.SingleAsync(u => u.Id == registered.User.Id, Token)).ChangeStatus(UserStatus.Locked);
        await context.SaveChangesAsync(Token);

        await Assert.ThrowsAsync<ForbiddenException>(() => auth.LoginAsync(new LoginRequest(phone, "password1"), Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => auth.LoginAsync(new LoginRequest(phone, "nope"), Token));
    }

    [RealDbFact]
    public async Task Me_returns_the_account_of_the_token_user()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Farmer);
        var (auth, _) = Services(context);
        var registered = await auth.RegisterFarmerAsync(new RegisterFarmerRequest("A", RandomPhone(), null, "password1"), Token);

        var me = await Services(context, registered.User.Id).Auth.GetCurrentUserAsync(Token);

        Assert.Equal(registered.User.Id, me.Id);
        Assert.Equal("FARMER", me.Role);
        Assert.Equal("ACTIVE", me.Status);
        Assert.False(me.PhoneVerified);
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => Services(context).Auth.GetCurrentUserAsync(Token));
    }

    [RealDbFact]
    public async Task First_admin_is_created_once()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Admin);
        // agrisage-dev already has its first Admin: hide existing admins inside this (rolled back) transaction.
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE users SET deleted_at = now() WHERE role_id IN (SELECT id FROM roles WHERE code = 'ADMIN')", Token);
        var (_, admin) = Services(context);
        var email = $"{Guid.NewGuid():N}@example.com";
        var request = new CreateAdminRequest("Administrator", email, null, "password1");

        var first = await admin.CreateFirstAdminAsync(request, Token);
        var second = await admin.CreateFirstAdminAsync(request with { Email = $"other-{email}" }, Token);

        Assert.Equal(AdminBootstrapResult.Created, first);
        Assert.Equal(AdminBootstrapResult.AlreadyExists, second);
        var created = await context.Users.Include(u => u.Role).SingleAsync(u => u.Email == email, Token);
        Assert.Equal(RoleCode.Admin, created.Role.Code);
        Assert.Equal(UserStatus.Active, created.Status);
        Assert.NotEqual("password1", created.PasswordHash);
        Assert.Equal(1, await context.Users.CountAsync(u => u.Role.Code == RoleCode.Admin, Token));
    }

    // Token → principal → CurrentUserService → SoftDeleteInterceptor: deleted_by is the user from the token.
    [RealDbFact]
    public async Task Soft_delete_records_the_user_from_a_real_token_as_deleted_by()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var context = session.NewContext();
        await EnsureRoleAsync(context, RoleCode.Farmer);
        var (auth, _) = Services(context);
        var registered = await auth.RegisterFarmerAsync(new RegisterFarmerRequest("A", RandomPhone(), null, "password1"), Token);

        var validated = await new JsonWebTokenHandler().ValidateTokenAsync(registered.AccessToken, TokenRules());
        session.CurrentUserOverride = new CurrentUserService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(validated.ClaimsIdentity) }
        });
        await using var acting = session.NewContext();
        var brand = new Brand($"Brand {Guid.NewGuid():N}"[..20]);
        acting.Add(brand);
        await acting.SaveChangesAsync(Token);

        acting.Remove(brand);
        await acting.SaveChangesAsync(Token);

        var stored = await acting.Brands.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Id == brand.Id, Token);
        Assert.Equal(registered.User.Id, stored.DeletedBy);
        Assert.NotNull(stored.DeletedAt);
    }

    private static TokenValidationParameters TokenRules() => new()
    {
        ValidIssuer = "AgriSage",
        ValidAudience = "AgriSage.Clients",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        NameClaimType = "sub",
        RoleClaimType = "role"
    };
}
