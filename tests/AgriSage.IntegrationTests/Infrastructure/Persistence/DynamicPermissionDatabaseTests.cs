using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// Rollback sessions retain role/member FK locks alongside the permission write lock;
// serialize with store business fixtures that lock those same rows.
[Collection(RealDb.WalkInPriceListCollection)]
public sealed class DynamicPermissionDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Env(RealDb.Session session, AgriSageDbContext db, User owner, User sale, Guid store, Dictionary<RoleCode, Role> roles)
    {
        public AgriSageDbContext Db => db;
        public User Owner => owner;
        public User Sale => sale;
        public Guid Store => store;
        public Dictionary<RoleCode, Role> Roles => roles;
        public PermissionEvaluator As(User user) { session.CurrentUser.UserId = user.Id; session.CurrentUser.Role = EnumText.Format(roles.Values.Single(r => r.Id == user.RoleId).Code); return new(db, session.CurrentUser); }
        public PermissionConfigurationService Service(User user) { var evaluator = As(user); return new(db, session.CurrentUser, evaluator, new PermissionWriteLock(db), new AuditTrail(db, session.CurrentUser, new DateTimeProvider())); }
    }
    private static async Task<Env> Setup(RealDb.Session session, AgriSageDbContext db)
    {
        var roles = await db.Roles.ToDictionaryAsync(r => r.Code, Token);
        var store = await ActiveStore.GetIdAsync(db, Token);
        User Make(RoleCode code) => new(roles[code].Id, code.ToString(), "test-hash", $"{Guid.NewGuid():N}@example.test", null);
        var owner = Make(RoleCode.StoreOwner); var sale = Make(RoleCode.SalesStaff);
        db.AddRange(owner, sale, new StoreMember(store, owner.Id), new StoreMember(store, sale.Id));
        await db.SaveChangesAsync(Token);
        return new(session, db, owner, sale, store, roles);
    }
    [RealDbFact]
    public async Task Refund_mutations_require_their_exact_grants_independently_of_read_permission()
    {
        await using var session = await RealDb.Session.StartAsync(); await using var db = session.NewContext();
        var env = await Setup(session, db); var configuration = env.Service(env.Owner);
        var target = await configuration.MemberAsync(env.Sale.Id, Token);
        var codes = new[] { "REFUNDS.CREATE_RETURN", "REFUNDS.COMPLETE_RETURN", "REFUNDS.FAIL_RETURN", "REFUNDS.CANCEL_RETURN",
            "REFUNDS.CREATE_ORDER", "REFUNDS.COMPLETE_ORDER", "REFUNDS.FAIL_ORDER", "REFUNDS.CANCEL_ORDER" };
        var granted = await configuration.SetMemberAsync(env.Sale.Id,
            new(codes.Select(c => new PermissionOverride(c, true)).ToList(), target.Version, target.RoleVersion, "Refund duties"), Token);
        var evaluator = env.As(env.Sale);
        Assert.False(await evaluator.HasAsync("REFUNDS.READ", Token));
        var clock = new DateTimeProvider();
        var refunds = new AgriSage.Application.Features.Returns.RefundService(db, new RowLockService(db), session.CurrentUser,
            clock, new AuditTrail(db, session.CurrentUser, clock), evaluator);
        var missing = Guid.NewGuid(); var refundId = Guid.NewGuid();
        // A valid grant reaches the scoped data lookup (404); an unrelated permission must not veto the operation.
        await Assert.ThrowsAsync<NotFoundException>(() => refunds.CreateReturnAsync(missing, new("CASH", 1), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => refunds.CompleteReturnAsync(missing, refundId, new(), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => refunds.FailReturnAsync(missing, refundId, new(), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => refunds.CancelReturnAsync(missing, refundId, new("Reason"), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => refunds.CreateOrderAsync(missing, new(Guid.NewGuid(), "CASH", 1), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => refunds.CompleteOrderAsync(missing, refundId, new(), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => refunds.FailOrderAsync(missing, refundId, new(), Token));
        await Assert.ThrowsAsync<NotFoundException>(() => refunds.CancelOrderAsync(missing, refundId, new("Reason"), Token));
        configuration = env.Service(env.Owner);
        await configuration.SetMemberAsync(env.Sale.Id, new([new("REFUNDS.COMPLETE_RETURN", false)], granted.Version, granted.RoleVersion, "Revoke completion"), Token);
        env.As(env.Sale);
        await Assert.ThrowsAsync<ForbiddenException>(() => refunds.CompleteReturnAsync(missing, refundId, new(), Token));
    }
    [RealDbFact]
    public async Task Grant_revoke_defaults_and_audits_are_live_atomic_and_preserve_unlisted_permissions()
    {
        await using var session = await RealDb.Session.StartAsync();
        await using var db = session.NewContext();
        var env = await Setup(session, db);
        Assert.False(await env.As(env.Sale).HasAsync("PRODUCTS.CREATE", Token));
        var service = env.Service(env.Owner);
        var before = await service.MemberAsync(env.Sale.Id, Token);
        var granted = await service.SetMemberAsync(env.Sale.Id, new([new("PRODUCTS.CREATE", true)], before.Version, before.RoleVersion, "Cấp quyền sản phẩm"), Token);
        Assert.Equal(before.Version + 1, granted.Version);
        Assert.True(await env.As(env.Sale).HasAsync("PRODUCTS.CREATE", Token));
        service = env.Service(env.Owner);
        var revoked = await service.SetMemberAsync(env.Sale.Id, new([new("ORDERS.READ", false)], granted.Version, granted.RoleVersion, "Thu hồi xem đơn"), Token);
        Assert.True(await env.As(env.Sale).HasAsync("PRODUCTS.CREATE", Token));
        Assert.False(await env.As(env.Sale).HasAsync("ORDERS.READ", Token));
        var logs = await db.AuditLogs.Where(l => l.EntityId == env.Sale.Id && l.Action == "STAFF_PERMISSIONS_CHANGED").ToListAsync(Token);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => { Assert.Equal(env.Owner.Id, l.ActorUserId); Assert.Equal(env.Store, l.StoreId); Assert.NotNull(l.Reason); Assert.Contains("overrides", l.OldValues!); Assert.Contains("overrides", l.NewValues!); });
        Assert.Contains(logs, l => l.NewValues!.Contains("PRODUCTS.CREATE"));
        Assert.Equal(granted.Version + 1, revoked.Version);
    }
    [RealDbFact]
    public async Task Owner_cannot_modify_self_other_store_non_sale_or_administrative_authority()
    {
        await using var session = await RealDb.Session.StartAsync(); await using var db = session.NewContext();
        var env = await Setup(session, db);
        var inactiveStore = new Store($"P{Guid.NewGuid():N}"[..15], "Other", "Address", "Province", status: StoreStatus.Inactive);
        var other = new User(env.Roles[RoleCode.SalesStaff].Id, "Other Sale", "hash", $"{Guid.NewGuid():N}@example.test", null);
        var farmer = new User(env.Roles[RoleCode.Farmer].Id, "Farmer", "hash", $"{Guid.NewGuid():N}@example.test", null);
        db.AddRange(inactiveStore, other, farmer, new StoreMember(inactiveStore.Id, other.Id)); await db.SaveChangesAsync(Token);
        var service = env.Service(env.Owner);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.MemberAsync(env.Owner.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => service.MemberAsync(other.Id, Token));
        await Assert.ThrowsAsync<NotFoundException>(() => service.MemberAsync(farmer.Id, Token));
        var target = await service.MemberAsync(env.Sale.Id, Token);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SetMemberAsync(env.Sale.Id, new([new("PERMISSIONS.MANAGE_ROLES", true)], target.Version, target.RoleVersion, "Elevation"), Token));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SetRoleAsync(env.Roles[RoleCode.SalesStaff].Id, new([], 0, "Elevation"), Token));
        Assert.False(await env.As(env.Sale).HasAsync("PERMISSIONS.DELEGATE", Token));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Service(env.Sale).MemberAsync(env.Owner.Id, Token));
        Assert.Empty(await db.AuditLogs.Where(l => l.EntityId == env.Sale.Id).ToListAsync(Token));
    }
    [RealDbFact]
    public async Task Stale_member_and_role_drafts_are_rejected_without_an_extra_audit()
    {
        await using var session = await RealDb.Session.StartAsync(); await using var db = session.NewContext();
        var env = await Setup(session, db); var service = env.Service(env.Owner);
        var target = await service.MemberAsync(env.Sale.Id, Token);
        await service.SetMemberAsync(env.Sale.Id, new([new("ORDERS.READ", false)], target.Version, target.RoleVersion, "First"), Token);
        await Assert.ThrowsAsync<ConflictException>(() => service.SetMemberAsync(env.Sale.Id, new([new("ORDERS.READ", true)], target.Version, target.RoleVersion, "Stale"), Token));
        Assert.Single(await db.AuditLogs.Where(l => l.EntityId == env.Sale.Id).ToListAsync(Token));
        var updated = await service.MemberAsync(env.Sale.Id, Token);
        await Assert.ThrowsAsync<ConflictException>(() => service.SetMemberAsync(env.Sale.Id, new([], updated.Version, updated.RoleVersion + 1, "Stale role"), Token));
    }
    [RealDbFact]
    public async Task Admin_role_changes_clamp_sale_grants_immediately_and_farmer_stays_scoped()
    {
        await using var session = await RealDb.Session.StartAsync(); await using var db = session.NewContext();
        var env = await Setup(session, db); var ownerService = env.Service(env.Owner); var target = await ownerService.MemberAsync(env.Sale.Id, Token);
        await ownerService.SetMemberAsync(env.Sale.Id, new([new("PRODUCTS.CREATE", true)], target.Version, target.RoleVersion, "Grant"), Token);
        var admin = new User(env.Roles[RoleCode.Admin].Id, "Admin", "hash", $"{Guid.NewGuid():N}@example.test", null);
        var farmer = new User(env.Roles[RoleCode.Farmer].Id, "Farmer", "hash", $"{Guid.NewGuid():N}@example.test", null);
        db.AddRange(admin, farmer); await db.SaveChangesAsync(Token);
        var service = env.Service(admin); var role = (await service.RolesAsync(Token)).Single(r => r.Code == "STORE_OWNER");
        var changed = await service.SetRoleAsync(role.Id, new(role.PermissionCodes.Where(c => c != "PRODUCTS.CREATE").ToList(), role.Version, "Narrow owner ceiling"), Token);
        Assert.Equal(role.Version + 1, changed.Version);
        Assert.False(await env.As(env.Sale).HasAsync("PRODUCTS.CREATE", Token));
        Assert.False(await env.As(env.Owner).HasAsync("PRODUCTS.CREATE", Token));
        Assert.True(await env.As(admin).HasAsync("PRODUCTS.CREATE", Token));
        Assert.False(await env.As(farmer).HasAsync("ORDERS.READ", Token));
        Assert.True(await env.As(farmer).HasAsync("MY_ORDERS.READ", Token));
        Assert.Contains(await db.AuditLogs.Where(l => l.EntityId == role.Id).ToListAsync(Token), l => l.Action == "ROLE_PERMISSIONS_CHANGED" && l.StoreId == null && l.Reason == "Narrow owner ceiling");
    }
}
