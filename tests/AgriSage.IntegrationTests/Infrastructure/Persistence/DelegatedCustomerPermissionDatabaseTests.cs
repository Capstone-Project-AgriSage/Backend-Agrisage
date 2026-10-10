using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Orders;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Infrastructure.Authentication;
using AgriSage.Infrastructure.Persistence;
using AgriSage.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

[Collection(RealDb.WalkInPriceListCollection)]
public sealed class DelegatedCustomerPermissionDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Tag() => Guid.NewGuid().ToString("N")[..12];

    private sealed class Env(RealDb.Session session, AgriSageDbContext db, User owner, User sale, FarmerProfile farmer, Guid store)
    {
        public AgriSageDbContext Db => db;
        public User Owner => owner;
        public User Sale => sale;
        public FarmerProfile Farmer => farmer;
        public Guid Store => store;
        private readonly DateTimeProvider clock = new();

        public void As(User user)
        {
            session.CurrentUser.UserId = user.Id;
            session.CurrentUser.Role = user.Id == owner.Id ? "STORE_OWNER" : "SALES_STAFF";
        }
        private PermissionEvaluator Evaluator => new(db, session.CurrentUser);
        private AuditTrail Audit => new(db, session.CurrentUser, clock);
        private CustomerWrites Writes => new(db, session.CurrentUser, clock, Audit, Evaluator);
        private RowLockService Locks => new(db);
        private PaymentQueries Payments => new(db);
        public CustomerService Customers => new(db, new PasswordHashService(), clock, new NpgsqlErrorClassifier(), Locks,
            Audit, Writes, Payments, new CustomerAddresses(db));
        public CustomerGroupService Groups => new(db, clock, new NpgsqlErrorClassifier(), Locks,
            new CustomerGroupDefaultSwitcher(db), Writes, Audit);
        public CustomerCreditService Credit => new(db, Locks, new NpgsqlErrorClassifier(), Writes, Audit, clock);
        public DebtService Debt => new(db, Locks, clock, session.CurrentUser, Writes, Audit, new OrderQueries(db), Payments);
        public BankDebtPaymentService Bank => new(db, Locks, Writes, clock,
            new PaymentAllocator(clock, new CreditReservationAdjuster(db, Locks, clock), new DebtRepaymentPosting(db, Locks, clock)), Payments, Audit);

        public async Task Configure(params PermissionOverride[] overrides)
        {
            As(owner);
            var permissions = new PermissionConfigurationService(db, session.CurrentUser, Evaluator, new PermissionWriteLock(db), Audit);
            var current = await permissions.MemberAsync(sale.Id, Token);
            await permissions.SetMemberAsync(sale.Id, new(overrides, current.Version, current.RoleVersion, "Delegation test"), Token);
            As(sale);
        }
    }

    private static async Task<Env> Setup(RealDb.Session session, AgriSageDbContext db)
    {
        var roles = await db.Roles.ToDictionaryAsync(r => r.Code, Token);
        var store = await ActiveStore.GetIdAsync(db, Token);
        User Make(RoleCode role) => new(roles[role].Id, "Permission test " + role, "test-hash", $"{Tag()}@example.test", null);
        var owner = Make(RoleCode.StoreOwner); var sale = Make(RoleCode.SalesStaff); var customer = Make(RoleCode.Farmer);
        var farmer = new FarmerProfile(customer.Id);
        db.AddRange(owner, sale, customer, farmer, new StoreMember(store, owner.Id), new StoreMember(store, sale.Id));
        await db.SaveChangesAsync(Token);
        return new(session, db, owner, sale, farmer, store);
    }

    [RealDbFact]
    public async Task Sale_grants_enable_group_tier_customer_and_credit_status_actions_and_revocation_is_live()
    {
        await using var session = await RealDb.Session.StartAsync(); await using var db = session.NewContext();
        var env = await Setup(session, db);
        await env.Configure(
            new("CUSTOMER_GROUPS.CREATE", true), new("CUSTOMER_GROUPS.UPDATE", true), new("CUSTOMER_GROUPS.DEACTIVATE", true),
            new("CUSTOMER_GROUPS.ACTIVATE", true), new("CUSTOMER_GROUPS.DELETE", true),
            new("CREDIT_TIERS.CREATE", true), new("CREDIT_TIERS.UPDATE", true), new("CREDIT_TIERS.DEACTIVATE", true),
            new("CREDIT_TIERS.ACTIVATE", true), new("CUSTOMERS.STATUS", true), new("CREDIT.SUSPEND", true), new("CREDIT.ACTIVATE", true));
        var group = await env.Groups.CreateAsync(new(Tag(), "Delegated group"), Token);
        Assert.Equal("Changed group", (await env.Groups.UpdateAsync(group.Id, new("Changed group"), Token)).Name);
        Assert.False((await env.Groups.SetActiveAsync(group.Id, false, Token)).IsActive);
        Assert.True((await env.Groups.SetActiveAsync(group.Id, true, Token)).IsActive);
        await env.Groups.DeleteAsync(group.Id, Token);
        var tier = await env.Credit.CreateTierAsync(new(Tag(), "Delegated tier", 1000, 30), Token);
        Assert.Equal(2000, (await env.Credit.UpdateTierAsync(tier.Id, new("Updated tier", 2000, 30), Token)).DefaultCreditLimit);
        Assert.False((await env.Credit.SetTierActiveAsync(tier.Id, false, Token)).IsActive);
        Assert.True((await env.Credit.SetTierActiveAsync(tier.Id, true, Token)).IsActive);
        Assert.Equal("INACTIVE", (await env.Customers.SetStatusAsync(env.Farmer.Id, new("INACTIVE"), Token)).Status);
        Assert.Equal("ACTIVE", (await env.Customers.SetStatusAsync(env.Farmer.Id, new("ACTIVE"), Token)).Status);
        await env.Credit.CreateAsync(env.Farmer.Id, new(tier.Id, 1000), Token);
        Assert.Equal("SUSPENDED", (await env.Credit.StatusAsync(env.Farmer.Id, "SUSPENDED", new("Suspend credit"), Token)).Status);
        Assert.Equal("ACTIVE", (await env.Credit.StatusAsync(env.Farmer.Id, "ACTIVE", new("Reactivate credit"), Token)).Status);
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Credit.StatusAsync(env.Farmer.Id, "BLOCKED", new("Not granted"), Token));
        await env.Configure(new("CUSTOMER_GROUPS.CREATE", false), new("CREDIT.ACTIVATE", false));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Groups.CreateAsync(new(Tag(), "Revoked"), Token));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Credit.StatusAsync(env.Farmer.Id, "ACTIVE", new("Revoked"), Token));
    }

    [RealDbFact]
    public async Task Combined_customer_forms_and_group_assignment_cannot_bypass_revoked_credit_permissions()
    {
        await using var session = await RealDb.Session.StartAsync(); await using var db = session.NewContext();
        var env = await Setup(session, db); env.As(env.Owner);
        var tier = await env.Credit.CreateTierAsync(new(Tag(), "Initial tier", 1000, 30), Token);
        var otherTier = await env.Credit.CreateTierAsync(new(Tag(), "Other tier", 2000, 30), Token);
        await env.Credit.CreateAsync(env.Farmer.Id, new(tier.Id, 1000), Token);
        var group = await env.Groups.CreateAsync(new(Tag(), "Group with other tier"), Token);
        await env.Groups.SetCreditTierAsync(group.Id, new(otherTier.Id), Token);
        await env.Configure(new("CREDIT.UPDATE", false), new("CREDIT.CREATE", false), new("CREDIT.SUSPEND", false));
        var userId = env.Farmer.UserId;
        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).SingleAsync(Token);
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(Token);
        async Task Denied(UpdateCustomerRequest request)
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => env.Customers.UpdateAsync(env.Farmer.Id, request, Token));
            db.ChangeTracker.Clear(); // the next attempt represents a new request scope
            Assert.Equal(name, await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).SingleAsync(Token));
            Assert.Equal(1000, await db.FarmerCreditProfiles.AsNoTracking().Where(p => p.FarmerProfileId == env.Farmer.Id).Select(p => p.CreditLimit).SingleAsync(Token));
        }
        await Denied(new() { FullName = "Must not persist", Email = email, CreditLimit = 5000, CreditChangeReason = "Bypass attempt" });
        await Denied(new() { FullName = "Must not persist", Email = email, AllowCreditPurchase = false, CreditChangeReason = "Bypass status attempt" });
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Customers.AssignGroupAsync(env.Farmer.Id, new(group.Id, "Bypass tier attempt"), Token));
        db.ChangeTracker.Clear();
        Assert.False(await db.CustomerGroupAssignments.AsNoTracking().AnyAsync(a => a.FarmerProfileId == env.Farmer.Id && a.CustomerGroupId == group.Id, Token));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Customers.CreateAsync(new()
        { FullName = "Denied credit creation", Email = $"{Tag()}@example.test", Password = "test-only-password", AllowCreditPurchase = true, CreditTierId = tier.Id }, Token));
        db.ChangeTracker.Clear();
        Assert.False(await db.Users.AsNoTracking().AnyAsync(u => u.FullName == "Denied credit creation", Token));
    }

    [RealDbFact]
    public async Task Debt_and_bank_review_require_each_exact_grant_and_keep_financial_ledgers()
    {
        await using var session = await RealDb.Session.StartAsync(); await using var db = session.NewContext();
        var env = await Setup(session, db);
        await env.Configure(new("DEBT.MANUAL", true), new("DEBT.ADJUST", true), new("DEBT.CANCEL", true), new("BANK_PAYMENTS.REJECT", true));
        var entry = await env.Debt.ManualAsync(env.Farmer.Id, new(300, BusinessCalendar.Today(DateTimeOffset.UtcNow).AddDays(30), "Opening debt"), Token);
        Assert.Equal(250, (await env.Debt.ActionAsync(entry.Id, "ADJUST", "Correction", 50, null, Token)).OutstandingAmount);
        var payment = await env.Bank.RecordAsync(new(env.Farmer.Id, 100), Token);
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Bank.ConfirmAsync(payment.Id, Token));
        Assert.Equal("FAILED", (await env.Bank.RejectAsync(payment.Id, new("Declined bank receipt"), Token)).Status);
        await env.Configure(new PermissionOverride("BANK_PAYMENTS.CONFIRM", true));
        var confirmed = await env.Bank.RecordAsync(new(env.Farmer.Id, 100), Token);
        Assert.Equal("PAID", (await env.Bank.ConfirmAsync(confirmed.Id, Token)).Status);
        Assert.Equal(150, (await env.Debt.EntryAsync(entry.Id, Token)).OutstandingAmount);
        var cancelled = await env.Debt.ManualAsync(env.Farmer.Id, new(10, BusinessCalendar.Today(DateTimeOffset.UtcNow).AddDays(30), "Another debt"), Token);
        Assert.Equal("CANCELLED", (await env.Debt.ActionAsync(cancelled.Id, "CANCEL", "Cancel erroneous debt", null, null, Token)).Status);
        Assert.True(await db.DebtTransactions.AsNoTracking().CountAsync(t => t.DebtEntryId == entry.Id, Token) >= 3);
    }
}
