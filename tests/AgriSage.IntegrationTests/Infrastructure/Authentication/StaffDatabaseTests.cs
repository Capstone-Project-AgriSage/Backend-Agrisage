using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Services;
using AgriSage.Application.Features.Staff;
using AgriSage.Application.Features.Staff.Dtos.Requests;
using AgriSage.Application.Features.Staff.Services;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using AgriSage.IntegrationTests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.Authentication;

// REAL PostgreSQL (opt-in AGRISAGE_DB_TESTS=1): staff management against the real schema, always rolled back.
public class StaffDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string RandomPhone() => "09" + Random.Shared.Next(10_000_000, 99_999_999);

    private sealed class Env(AgriSageDbContext context, RealDb.Session session) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();

        public AgriSageDbContext Context { get; } = context;

        public RealDb.Session Session { get; } = session;

        public Guid StoreId { get; set; }

        public Dictionary<RoleCode, Role> Roles { get; } = [];

        public StaffService StaffAs(Guid? userId, string? role) => Build(userId, role).Staff;

        public AuthService AuthAs(Guid? userId, string? role) => Build(userId, role).Auth;

        public UserAccessValidator Validator => new(Context);

        private (StaffService Staff, AuthService Auth) Build(Guid? userId, string? role)
        {
            var hasher = new PasswordHashService();
            var clock = new DateTimeProvider();
            var tokens = new AccessTokenService(
                Options.Create(new JwtOptions
                {
                    Issuer = "AgriSage",
                    Audience = "AgriSage.Clients",
                    SigningKey = "integration-test-signing-key-not-a-secret-000000",
                    AccessTokenMinutes = 60
                }),
                clock);
            var current = new RealDb.MutableUser { UserId = userId, Role = role };
            var errors = new NpgsqlErrorClassifier();

            return (new StaffService(Context, hasher, current, clock, errors),
                new AuthService(Context, hasher, tokens, current, clock, errors));
        }

        // A user that acts as the caller (Admin or Store Owner) without going through the API.
        public async Task<User> NewActorAsync(RoleCode role)
        {
            var user = new User(Roles[role].Id, $"Actor {role}", new PasswordHashService().Hash("password1"),
                $"{Guid.NewGuid():N}@example.test", null);
            Context.Users.Add(user);
            if (StaffPolicy.IsStaffRole(role))
            {
                Context.StoreMembers.Add(new StoreMember(StoreId, user.Id));
            }

            await Context.SaveChangesAsync(Token);
            return user;
        }
    }

    private static async Task<Env> PrepareAsync(RealDb.Session session)
    {
        var context = session.NewContext();
        var env = new Env(context, session);

        foreach (var code in Enum.GetValues<RoleCode>())
        {
            var role = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == code, Token);
            if (role is null)
            {
                role = new Role(code, code.ToString());
                context.Roles.Add(role);
                await context.SaveChangesAsync(Token);
            }

            env.Roles[code] = role;
        }

        var storeId = await context.Stores.Where(s => s.Status == StoreStatus.Active).Select(s => s.Id).FirstOrDefaultAsync(Token);
        if (storeId == Guid.Empty)
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var store = new Store($"T{suffix}", "Test store", "1 Test Street", "Test");
            context.Stores.Add(store);
            await context.SaveChangesAsync(Token);
            storeId = store.Id;
        }

        env.StoreId = storeId;
        return env;
    }

    private static CreateStaffRequest NewStaff(string role, string? name = null, string? phone = null, string? email = null) =>
        new(name ?? $"Staff {Guid.NewGuid():N}"[..20], role, phone ?? RandomPhone(), email, "password1", "E-1", new DateOnly(2026, 1, 15));

    [RealDbFact]
    public async Task Admin_creates_each_staff_role_attached_to_the_active_store()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var staff = env.StaffAs(admin.Id, "ADMIN");

        foreach (var role in new[] { "STORE_OWNER", "SALES_STAFF", "DELIVERY_STAFF" })
        {
            var phone = RandomPhone();
            var created = await staff.CreateAsync(NewStaff(role, phone: $"+84{phone[1..]}"), Token);

            Assert.Equal(role, created.Role);
            Assert.Equal("ACTIVE", created.Status);
            Assert.Equal("ACTIVE", created.MemberStatus);
            Assert.Equal(phone, created.PhoneNumber);
            Assert.False(created.CanReviewAi);
            Assert.Equal("E-1", created.EmployeeCode);
            var member = await env.Context.StoreMembers.Include(m => m.User)
                .SingleAsync(m => m.UserId == created.Id, Token);
            Assert.Equal(env.StoreId, member.StoreId);
            Assert.NotEqual("password1", member.User.PasswordHash);
            // The initial password works for login.
            var login = await env.AuthAs(null, null).LoginAsync(new LoginRequest(phone, "password1"), Token);
            Assert.Equal(created.Id, login.User.Id);
            Assert.Equal(role, login.User.Role);
        }
    }

    [RealDbFact]
    public async Task Store_owner_creates_sales_and_delivery_but_not_owner_or_admin()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var owner = await env.NewActorAsync(RoleCode.StoreOwner);
        var staff = env.StaffAs(owner.Id, "STORE_OWNER");

        Assert.Equal("SALES_STAFF", (await staff.CreateAsync(NewStaff("SALES_STAFF"), Token)).Role);
        Assert.Equal("DELIVERY_STAFF", (await staff.CreateAsync(NewStaff("DELIVERY_STAFF"), Token)).Role);
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.CreateAsync(NewStaff("STORE_OWNER"), Token));
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.CreateAsync(NewStaff("ADMIN"), Token));
    }

    [RealDbFact]
    public async Task Other_roles_and_anonymous_callers_cannot_use_the_service()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var sales = await env.NewActorAsync(RoleCode.SalesStaff);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            env.StaffAs(sales.Id, "SALES_STAFF").CreateAsync(NewStaff("SALES_STAFF"), Token));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            env.StaffAs(sales.Id, "SALES_STAFF").ListAsync(new StaffListRequest(), Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            env.StaffAs(null, null).ListAsync(new StaffListRequest(), Token));
    }

    [RealDbFact]
    public async Task Duplicate_phone_or_email_is_a_conflict()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var staff = env.StaffAs(admin.Id, "ADMIN");
        var phone = RandomPhone();
        var email = $"{Guid.NewGuid():N}@Example.com";
        await staff.CreateAsync(NewStaff("SALES_STAFF", phone: phone, email: email), Token);

        await Assert.ThrowsAsync<ConflictException>(() =>
            staff.CreateAsync(NewStaff("DELIVERY_STAFF", phone: $"{phone[..4]} {phone[4..7]} {phone[7..]}"), Token));
        await Assert.ThrowsAsync<ConflictException>(() =>
            staff.CreateAsync(NewStaff("DELIVERY_STAFF", phone: RandomPhone(), email: email.ToUpperInvariant()), Token));
    }

    [RealDbFact]
    public async Task List_filters_searches_and_pages()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var staff = env.StaffAs(admin.Id, "ADMIN");
        var tag = Guid.NewGuid().ToString("N")[..8];
        var a = await staff.CreateAsync(NewStaff("SALES_STAFF", $"Zz{tag} A"), Token);
        await staff.CreateAsync(NewStaff("SALES_STAFF", $"Zz{tag} B"), Token);
        await staff.CreateAsync(NewStaff("DELIVERY_STAFF", $"Zz{tag} C"), Token);
        await staff.LockAsync(a.Id, Token);

        var all = await staff.ListAsync(new StaffListRequest { Search = $"zz{tag}" }, Token);
        var sales = await staff.ListAsync(new StaffListRequest { Search = tag, Role = "SALES_STAFF" }, Token);
        var locked = await staff.ListAsync(new StaffListRequest { Search = tag, Status = "locked" }, Token);
        var page1 = await staff.ListAsync(new StaffListRequest { Search = tag, Page = 1, PageSize = 2 }, Token);
        var page2 = await staff.ListAsync(new StaffListRequest { Search = tag, Page = 2, PageSize = 2 }, Token);

        Assert.Equal(3, all.TotalCount);
        Assert.Equal(2, sales.TotalCount);
        Assert.Equal(a.Id, Assert.Single(locked.Items).Id);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(2, page1.TotalPages);
        Assert.All(all.Items, item => Assert.NotEqual("ADMIN", item.Role));
        // Alphabetical: A, B, C.
        Assert.EndsWith(" A", page1.Items[0].FullName);
    }

    [RealDbFact]
    public async Task Get_returns_staff_only_and_admins_are_not_found()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var staff = env.StaffAs(admin.Id, "ADMIN");
        var created = await staff.CreateAsync(NewStaff("SALES_STAFF"), Token);

        var found = await staff.GetAsync(created.Id, Token);

        Assert.Equal(created.FullName, found.FullName);
        await Assert.ThrowsAsync<NotFoundException>(() => staff.GetAsync(admin.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => staff.GetAsync(Guid.NewGuid(), Token));
    }

    [RealDbFact]
    public async Task Update_changes_profile_contact_and_employment_and_rejects_taken_contacts()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var staff = env.StaffAs(admin.Id, "ADMIN");
        var first = await staff.CreateAsync(NewStaff("SALES_STAFF"), Token);
        var second = await staff.CreateAsync(NewStaff("DELIVERY_STAFF"), Token);
        var newPhone = RandomPhone();

        var updated = await staff.UpdateAsync(
            first.Id, new UpdateStaffRequest("  New Name ", newPhone, "New@Example.com", "E-9", new DateOnly(2026, 2, 1)), Token);

        Assert.Equal("New Name", updated.FullName);
        Assert.Equal(newPhone, updated.PhoneNumber);
        Assert.Equal("new@example.com", updated.Email);
        Assert.Equal("E-9", updated.EmployeeCode);
        Assert.Equal(new DateOnly(2026, 2, 1), updated.JoinedAt);
        await Assert.ThrowsAsync<ConflictException>(() =>
            staff.UpdateAsync(second.Id, new UpdateStaffRequest("X", newPhone, null), Token));
        // Keeping its own contact is not a conflict.
        await staff.UpdateAsync(first.Id, new UpdateStaffRequest("Same", newPhone, "new@example.com"), Token);
    }

    [RealDbFact]
    public async Task Lock_takes_effect_immediately_and_unlock_restores_access()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var owner = await env.NewActorAsync(RoleCode.StoreOwner);
        var staff = env.StaffAs(owner.Id, "STORE_OWNER");
        var sales = await staff.CreateAsync(NewStaff("SALES_STAFF"), Token);
        Assert.True(await env.Validator.IsActiveAsync(sales.Id, Token));

        await staff.LockAsync(sales.Id, Token);

        Assert.False(await env.Validator.IsActiveAsync(sales.Id, Token));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            env.AuthAs(null, null).LoginAsync(new LoginRequest(sales.PhoneNumber!, "password1"), Token));

        await staff.UnlockAsync(sales.Id, Token);

        Assert.True(await env.Validator.IsActiveAsync(sales.Id, Token));
        Assert.Equal("ACTIVE", (await staff.GetAsync(sales.Id, Token)).Status);
    }

    [RealDbFact]
    public async Task Management_rules_protect_owners_admins_and_the_caller()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var owner = await env.NewActorAsync(RoleCode.StoreOwner);
        var otherOwner = await env.NewActorAsync(RoleCode.StoreOwner);
        var asOwner = env.StaffAs(owner.Id, "STORE_OWNER");

        await Assert.ThrowsAsync<ForbiddenException>(() => asOwner.LockAsync(otherOwner.Id, Token));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            asOwner.ResetPasswordAsync(otherOwner.Id, new ResetStaffPasswordRequest("password2"), Token));
        await Assert.ThrowsAsync<ForbiddenException>(() => asOwner.RemoveAsync(otherOwner.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => asOwner.LockAsync(admin.Id, Token));
        // An owner is not allowed to manage any Store Owner, itself included.
        await Assert.ThrowsAsync<ForbiddenException>(() => asOwner.LockAsync(owner.Id, Token));
        // Admin accounts are not staff, so even an Admin cannot target one (itself included) through this API.
        await Assert.ThrowsAsync<NotFoundException>(() => env.StaffAs(admin.Id, "ADMIN").LockAsync(admin.Id, Token));
        // Admin can manage any staff including owners.
        await env.StaffAs(admin.Id, "ADMIN").LockAsync(owner.Id, Token);
        Assert.False(await env.Validator.IsActiveAsync(owner.Id, Token));
    }

    [RealDbFact]
    public async Task Reset_password_replaces_the_old_password()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var staff = env.StaffAs(admin.Id, "ADMIN");
        var sales = await staff.CreateAsync(NewStaff("SALES_STAFF"), Token);

        await staff.ResetPasswordAsync(sales.Id, new ResetStaffPasswordRequest("newpassword1"), Token);

        var auth = env.AuthAs(null, null);
        Assert.Equal(sales.Id, (await auth.LoginAsync(new LoginRequest(sales.PhoneNumber!, "newpassword1"), Token)).User.Id);
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            auth.LoginAsync(new LoginRequest(sales.PhoneNumber!, "password1"), Token));
    }

    [RealDbFact]
    public async Task Remove_marks_the_member_as_left_and_locks_without_soft_deleting_the_user()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var staff = env.StaffAs(admin.Id, "ADMIN");
        var sales = await staff.CreateAsync(NewStaff("SALES_STAFF"), Token);

        await staff.RemoveAsync(sales.Id, Token);
        await staff.RemoveAsync(sales.Id, Token); // idempotent

        var removed = await staff.GetAsync(sales.Id, Token);
        Assert.Equal("LEFT", removed.MemberStatus);
        Assert.Equal("LOCKED", removed.Status);
        Assert.NotNull(removed.LeftAt);
        var user = await env.Context.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == sales.Id, Token);
        Assert.Null(user.DeletedAt);

        await staff.UnlockAsync(sales.Id, Token);
        var back = await staff.GetAsync(sales.Id, Token);
        Assert.Equal("ACTIVE", back.MemberStatus);
        Assert.Null(back.LeftAt);
        Assert.Equal("ACTIVE", back.Status);
    }

    [RealDbFact]
    public async Task Change_password_requires_the_current_password()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var sales = await env.StaffAs(admin.Id, "ADMIN").CreateAsync(NewStaff("SALES_STAFF"), Token);
        var asSales = env.AuthAs(sales.Id, "SALES_STAFF");

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            asSales.ChangePasswordAsync(new ChangePasswordRequest("wrong", "newpassword1"), Token));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            asSales.ChangePasswordAsync(new ChangePasswordRequest("password1", "password1"), Token));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            env.AuthAs(null, null).ChangePasswordAsync(new ChangePasswordRequest("password1", "newpassword1"), Token));

        await asSales.ChangePasswordAsync(new ChangePasswordRequest("password1", "newpassword1"), Token);

        var auth = env.AuthAs(null, null);
        Assert.Equal(sales.Id, (await auth.LoginAsync(new LoginRequest(sales.PhoneNumber!, "newpassword1"), Token)).User.Id);
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            auth.LoginAsync(new LoginRequest(sales.PhoneNumber!, "password1"), Token));
    }

    [RealDbFact]
    public async Task Account_validator_rejects_unknown_and_deleted_users()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var env = await PrepareAsync(session);
        var admin = await env.NewActorAsync(RoleCode.Admin);
        var gone = await env.NewActorAsync(RoleCode.SalesStaff);
        Assert.True(await env.Validator.IsActiveAsync(admin.Id, Token));
        Assert.False(await env.Validator.IsActiveAsync(Guid.NewGuid(), Token));

        gone.MarkDeleted(admin.Id, DateTimeOffset.UtcNow);
        await env.Context.SaveChangesAsync(Token);

        Assert.False(await env.Validator.IsActiveAsync(gone.Id, Token));
    }
}
