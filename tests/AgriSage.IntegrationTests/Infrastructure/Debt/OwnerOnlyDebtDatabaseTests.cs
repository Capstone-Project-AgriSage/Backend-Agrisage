using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Permissions;
using AgriSage.Domain.Features.Credit.Enums;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Payments.Enums;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.21: money-changing debt and credit actions are owner-only (CS-01, CS-10, CS-11, AD-02). They run against a real PostgreSQL
// schema with the real permission evaluator, so a delegation by an override row, by a Sale role default or by an inherited
// (null) override is exercised end to end. Every refused action must leave the data exactly as it was.
public sealed class OwnerOnlyDebtDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly string[] Codes =
    [
        "DEBT.ADJUST", "DEBT.CANCEL", "DEBT.MANUAL", "BANK_PAYMENTS.CONFIRM", "BANK_PAYMENTS.REJECT", "CREDIT.SUSPEND", "CREDIT.BLOCK",
        "CREDIT.ACTIVATE", "DEBT.DUE_DATE",
    ];

    // How the permission reaches the Sale staff member.
    internal const string ByOverride = "override";     // store_member_permissions.is_granted = true
    private const string ByRole = "role";             // role_permissions of the Sale role
    private const string ByInheritance = "inherit";   // role default + an override row with is_granted = null

    internal static async Task<(Guid UserId, Guid MemberId)> AddStaffAsync(CreditTestDatabase e, string style)
    {
        using var s = e.Scope(); var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var sale = new Role(RoleCode.SalesStaff, "Sale"); var user = new User(sale.Id, "Staff", "hash", "staff@example.test", null);
        var member = new StoreMember(e.StoreId, user.Id);
        db.AddRange(sale, user, member); await db.SaveChangesAsync(Token);
        foreach (var code in Codes)
        {
            var id = PermissionCatalog.ByCode[code].Id;
            if (style == ByOverride) db.StoreMemberPermissions.Add(new(member.Id, id, true));
            else
            {
                db.RolePermissions.Add(new(sale.Id, id, true));
                if (style == ByInheritance) db.StoreMemberPermissions.Add(new(member.Id, id, null));
            }
        }
        await db.SaveChangesAsync(Token);
        return (user.Id, member.Id);
    }

    internal static async Task<string> SnapshotAsync(CreditTestDatabase e, Guid paymentId)
    {
        using var s = e.Scope(); var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var entries = (await db.DebtEntries.AsNoTracking().OrderBy(x => x.EntryNumber)
            .Select(x => new { x.EntryNumber, x.OutstandingAmount, x.Status, x.DueDate }).ToListAsync(Token))
            .Select(x => $"{x.EntryNumber}:{x.OutstandingAmount}:{x.Status}:{x.DueDate}");
        var balances = (await db.DebtAccounts.AsNoTracking().OrderBy(a => a.Id).Select(a => a.CurrentBalance).ToListAsync(Token));
        return string.Join("|", entries) + $"/balances={string.Join(",", balances)}"
            + $"/ledger={await db.DebtTransactions.CountAsync(Token)}/actions={await db.DebtEntryActions.CountAsync(Token)}"
            + $"/payment={(await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId, Token)).Status}"
            + $"/allocations={await db.PaymentAllocations.CountAsync(Token)}"
            + $"/credit={(await db.FarmerCreditProfiles.AsNoTracking().SingleAsync(Token)).Status}/audits={await db.AuditLogs.CountAsync(Token)}";
    }

    private async Task StaffIsRefusedEverywhereAsync(string style)
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var order = await e.FulfillAsync(10, Token);
        Guid entryId, paymentId;
        using (var s = e.Scope())
        {
            entryId = (await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, Token)).Id;
            paymentId = (await s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>().RecordAsync(new(e.FarmerId, 3_000_000, Reference: "pending"), Token)).Id;
        }
        var staff = await AddStaffAsync(e, style);
        var before = await SnapshotAsync(e, paymentId);
        var ownerId = e.User.UserId; var ownerRole = e.User.Role;
        e.User.UserId = staff.UserId; e.User.Role = "SALES_STAFF";

        async Task UpdateCustomerAsync(IServiceProvider p, bool allowCredit)
        {
            // The fixture does not register the password hasher that CustomerService needs; supply it here.
            var customers = ActivatorUtilities.CreateInstance<AgriSage.Application.Features.Customers.CustomerService>(
                p, new AgriSage.Infrastructure.Authentication.PasswordHashService());
            var current = await customers.GetAsync(e.FarmerId, Token);
            await customers.UpdateAsync(e.FarmerId, new AgriSage.Application.Features.Customers.UpdateCustomerRequest
            {
                FullName = current.FullName, PhoneNumber = current.PhoneNumber, Email = current.Email,
                AllowCreditPurchase = allowCredit, CreditChangeReason = "reason",
            }, Token);
        }

        async Task Refused(Func<IServiceProvider, Task> act, string what)
        {
            using var s = e.Scope();
            await Assert.ThrowsAsync<ForbiddenException>(() => act(s.ServiceProvider)).WaitAsync(Token);
            Assert.True(before == await SnapshotAsync(e, paymentId), $"Data changed after the refused action: {what}");
        }
        await Refused(p => p.GetRequiredService<IDebtService>().ActionAsync(entryId, "ADJUST", "reason", 100, null, Token), "adjust");
        await Refused(p => p.GetRequiredService<IDebtService>().ActionAsync(entryId, "CANCEL", "reason", null, null, Token), "cancel");
        await Refused(p => p.GetRequiredService<IDebtService>().ManualAsync(e.FarmerId, new(500_000, new DateOnly(2026, 12, 1), "reason"), Token), "manual entry");
        await Refused(p => p.GetRequiredService<IBankDebtPaymentService>().ConfirmAsync(paymentId, Token), "bank confirm");
        await Refused(p => p.GetRequiredService<IBankDebtPaymentService>().RejectAsync(paymentId, new("reason"), Token), "bank reject");
        await Refused(p => p.GetRequiredService<ICustomerCreditService>().StatusAsync(e.FarmerId, "SUSPENDED", new("reason"), Token), "credit suspend");
        await Refused(p => p.GetRequiredService<ICustomerCreditService>().StatusAsync(e.FarmerId, "BLOCKED", new("reason"), Token), "credit block");
        await Refused(p => p.GetRequiredService<IDebtService>().ActionAsync(entryId, "CHANGE_DUE_DATE", "reason", null, new DateOnly(2099, 12, 1), Token), "change due date");

        // Indirect path: the customer form suspends credit when AllowCreditPurchase is switched off.
        await Refused(p => UpdateCustomerAsync(p, allowCredit: false), "credit suspend through the customer form");

        // Reactivation is owner-only too: the owner suspends, then staff cannot switch credit back on by either path.
        e.User.UserId = ownerId; e.User.Role = ownerRole;
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<ICustomerCreditService>().StatusAsync(e.FarmerId, "SUSPENDED", new("Late payer"), Token);
        before = await SnapshotAsync(e, paymentId);
        e.User.UserId = staff.UserId; e.User.Role = "SALES_STAFF";
        await Refused(p => p.GetRequiredService<ICustomerCreditService>().StatusAsync(e.FarmerId, "ACTIVE", new("reason"), Token), "credit activate");
        await Refused(p => UpdateCustomerAsync(p, allowCredit: true), "credit activate through the customer form");

        // The action the staff member is still allowed to take keeps working (a pending transfer is only recorded).
        using (var s = e.Scope()) await s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>().RecordAsync(new(e.FarmerId, 100_000, Reference: "staff"), Token);
        e.User.UserId = ownerId; e.User.Role = ownerRole;
    }

    [CreditDbFact]
    public Task Sale_staff_delegated_by_an_override_row_is_refused_and_nothing_changes() => StaffIsRefusedEverywhereAsync(ByOverride);

    [CreditDbFact]
    public Task Sale_staff_with_the_permission_in_the_sale_role_defaults_is_refused_and_nothing_changes() => StaffIsRefusedEverywhereAsync(ByRole);

    [CreditDbFact]
    public Task Sale_staff_with_an_inherited_override_is_refused_and_nothing_changes() => StaffIsRefusedEverywhereAsync(ByInheritance);

    // The customer form only changes credit status on a real transition. Re-sending AllowCreditPurchase = false for a customer
    // whose credit is already suspended is not a suspension, so staff can still edit the customer; switching it back on through
    // the form is a reactivation, which is owner-only even for staff holding CREDIT.ACTIVATE.
    [CreditDbFact]
    public async Task Customer_form_only_guards_a_real_credit_status_change()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        using (var s = e.Scope())
            await s.ServiceProvider.GetRequiredService<ICustomerCreditService>().StatusAsync(e.FarmerId, "SUSPENDED", new("Late payer"), Token);
        var staff = await AddStaffAsync(e, ByOverride); // holds every owner-only code, CREDIT.ACTIVATE included
        var ownerId = e.User.UserId; e.User.UserId = staff.UserId; e.User.Role = "SALES_STAFF";

        async Task Update(bool allowCredit, string name)
        {
            using var s = e.Scope();
            var customers = ActivatorUtilities.CreateInstance<AgriSage.Application.Features.Customers.CustomerService>(
                s.ServiceProvider, new AgriSage.Infrastructure.Authentication.PasswordHashService());
            var current = await customers.GetAsync(e.FarmerId, Token);
            await customers.UpdateAsync(e.FarmerId, new AgriSage.Application.Features.Customers.UpdateCustomerRequest
            {
                FullName = name, PhoneNumber = current.PhoneNumber, Email = current.Email,
                AllowCreditPurchase = allowCredit, CreditChangeReason = "reason",
            }, Token);
        }
        async Task<(string Name, FarmerCreditProfileStatus Status)> State()
        {
            using var s = e.Scope(); var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var name = await db.FarmerProfiles.AsNoTracking().Where(f => f.Id == e.FarmerId).Select(f => f.User.FullName).SingleAsync(Token);
            return (name, (await db.FarmerCreditProfiles.AsNoTracking().SingleAsync(Token)).Status);
        }

        await Update(allowCredit: false, name: "Renamed by staff");
        Assert.Equal(("Renamed by staff", FarmerCreditProfileStatus.Suspended), await State());

        await Assert.ThrowsAsync<ForbiddenException>(() => Update(allowCredit: true, name: "Reactivated by staff"));
        Assert.Equal(("Renamed by staff", FarmerCreditProfileStatus.Suspended), await State());
        e.User.UserId = ownerId; e.User.Role = "STORE_OWNER";
    }

    [CreditDbFact]
    public async Task The_owner_of_the_active_store_can_still_do_every_owner_only_action()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var order = await e.FulfillAsync(10, Token);
        using var s = e.Scope(); var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var entryId = (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, Token)).Id;
        var debt = s.ServiceProvider.GetRequiredService<IDebtService>(); var bank = s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>();

        var confirmed = await bank.RecordAsync(new(e.FarmerId, 1_000_000), Token);
        var rejected = await bank.RecordAsync(new(e.FarmerId, 500_000), Token);
        // The owner confirms a transfer they recorded themselves: the policy asks for the right role, not for two people.
        await bank.ConfirmAsync(confirmed.Id, Token);
        await bank.RejectAsync(rejected.Id, new("Wrong reference"), Token);
        await debt.ManualAsync(e.FarmerId, new(200_000, new DateOnly(2026, 12, 1), "Old balance"), Token);
        await debt.ActionAsync(entryId, "ADJUST", "Price correction", 100_000, null, Token);
        await debt.ActionAsync(entryId, "CHANGE_DUE_DATE", "Harvest delayed", null, new DateOnly(2099, 12, 1), Token);
        var credit = s.ServiceProvider.GetRequiredService<ICustomerCreditService>();
        await credit.StatusAsync(e.FarmerId, "SUSPENDED", new("Late payer"), Token);
        Assert.Equal(FarmerCreditProfileStatus.Suspended, (await db.FarmerCreditProfiles.AsNoTracking().SingleAsync(Token)).Status);
        await credit.StatusAsync(e.FarmerId, "ACTIVE", new("Paid up"), Token);

        Assert.Equal(FarmerCreditProfileStatus.Active, (await db.FarmerCreditProfiles.AsNoTracking().SingleAsync(Token)).Status);
        Assert.Equal(new DateOnly(2099, 12, 1), (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.Id == entryId, Token)).DueDate);
        Assert.Equal(PaymentStatus.Paid, (await db.Payments.AsNoTracking().SingleAsync(p => p.Id == confirmed.Id, Token)).Status);
        Assert.Equal(PaymentStatus.Failed, (await db.Payments.AsNoTracking().SingleAsync(p => p.Id == rejected.Id, Token)).Status);
    }

    [CreditDbFact]
    public async Task An_owner_who_is_not_a_member_of_the_active_store_is_refused_and_nothing_changes()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var order = await e.FulfillAsync(10, Token);
        Guid entryId, paymentId, otherOwnerId;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            entryId = (await db.DebtEntries.AsNoTracking().SingleAsync(x => x.OrderId == order.Id, Token)).Id;
            paymentId = (await s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>().RecordAsync(new(e.FarmerId, 3_000_000), Token)).Id;
            // A Store Owner of ANOTHER store: member of an inactive store only.
            var ownerRole = await db.Roles.SingleAsync(r => r.Code == RoleCode.StoreOwner, Token);
            var other = new Store("OTHER2", "Other store", "Test", "Test", status: StoreStatus.Inactive);
            var user = new User(ownerRole.Id, "Other owner", "hash", "other-owner@example.test", null);
            db.AddRange(other, user, new StoreMember(other.Id, user.Id)); await db.SaveChangesAsync(Token);
            otherOwnerId = user.Id;
        }
        var before = await SnapshotAsync(e, paymentId);
        var ownerId = e.User.UserId; e.User.UserId = otherOwnerId; e.User.Role = "STORE_OWNER";

        using (var s = e.Scope())
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => s.ServiceProvider.GetRequiredService<IDebtService>().ActionAsync(entryId, "ADJUST", "reason", 100, null, Token));
            await Assert.ThrowsAsync<ForbiddenException>(() => s.ServiceProvider.GetRequiredService<IBankDebtPaymentService>().ConfirmAsync(paymentId, Token));
            await Assert.ThrowsAsync<ForbiddenException>(() => s.ServiceProvider.GetRequiredService<ICustomerCreditService>().StatusAsync(e.FarmerId, "BLOCKED", new("reason"), Token));
        }
        Assert.Equal(before, await SnapshotAsync(e, paymentId));
        e.User.UserId = ownerId;
    }

    [CreditDbFact]
    public async Task A_debt_entry_of_another_store_is_not_found_for_the_owner_and_stays_untouched()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await e.FulfillAsync(10, Token);
        Guid foreignId;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var otherStore = new Store("OTHER", "Other", "Test", "Test", status: StoreStatus.Inactive);
            var account = new AgriSage.Domain.Features.Debt.Entities.DebtAccount(otherStore.Id, e.FarmerId);
            var posting = account.CreateManualAdjustmentEntry("FOREIGN", 100, new(2026, 11, 1), e.User.UserId!.Value, e.Clock.UtcNow);
            db.AddRange(otherStore, account, posting.Entry, posting.Transaction); await db.SaveChangesAsync(Token);
            foreignId = posting.Entry.Id;
        }
        using (var s = e.Scope())
        {
            var debt = s.ServiceProvider.GetRequiredService<IDebtService>();
            await Assert.ThrowsAsync<NotFoundException>(() => debt.ActionAsync(foreignId, "ADJUST", "reason", 10, null, Token));
            await Assert.ThrowsAsync<NotFoundException>(() => debt.ActionAsync(foreignId, "CANCEL", "reason", null, null, Token));
        }
        using var verify = e.Scope(); var check = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var foreign = await check.DebtEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == foreignId, Token);
        Assert.Equal(100, foreign.OutstandingAmount);
    }
}
