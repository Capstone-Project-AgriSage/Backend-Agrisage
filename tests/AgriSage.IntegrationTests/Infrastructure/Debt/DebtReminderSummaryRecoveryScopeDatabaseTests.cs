using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Notifications;
using AgriSage.Domain.Features.Customers.Entities;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Domain.Features.Identity.Enums;
using AgriSage.Domain.Features.Notifications.Entities;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.20, recovery of lost summaries (continued): no limit of days (from T0), batches that always finish, and everything inside the
// store the job runs for (the figures, the recipients, the "already written" check).
public sealed partial class DebtReminderSummaryRecoveryDatabaseTests
{
    private static string KeyOf(CreditTestDatabase e, int month, int day, string milestone, Guid? store = null) =>
        $"debt-reminder-staff:{(store ?? e.StoreId):N}:2026{month:00}{day:00}:{milestone}";

    private static async Task SeedAsync(CreditTestDatabase e, Guid user, string key, string type = "DEBT_DUE_SOON")
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        db.Notifications.Add(new Notification(user, type, "Nhắc nợ", "Nội dung", "{}", key));
        await db.SaveChangesAsync(Token);
    }

    private static async Task<Guid> SalesMemberAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var sale = await db.Roles.SingleOrDefaultAsync(r => r.Code == RoleCode.SalesStaff, Token) ?? new Role(RoleCode.SalesStaff, "Sale");
        var user = new User(sale.Id, "Staff", "hash", $"{Guid.NewGuid():N}@example.test", null);
        if (db.Entry(sale).State == EntityState.Detached) db.Add(sale);
        db.AddRange(user, new StoreMember(e.StoreId, user.Id));
        await db.SaveChangesAsync(Token);
        return user.Id;
    }

    private static async Task TwoGroupsDueAsync(CreditTestDatabase e, DateOnly due)
    {
        var other = await SecondFarmerAsync(e);
        foreach (var farmer in new[] { e.FarmerId, other })
            await Run(e, sp => sp.GetRequiredService<IDebtService>().ManualAsync(farmer, new(500_000m, due, "Nợ"), Token));
    }

    private static async Task StartRemindersAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        await ReminderStartHelper.EnsureAsync(s.ServiceProvider.GetRequiredService<AgriSageDbContext>(), e.StoreId, Token);
    }

    // No limit of days: a run 15 days after the failure still recovers the summary of day D, once, with D's figures only.
    [CreditDbFact]
    public async Task A_summary_lost_on_day_D_is_recovered_on_day_D_plus_15_exactly_once()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await StartRemindersAsync(e);
        At(e, 2026, 11, 4);
        await TwoGroupsDueAsync(e, new DateOnly(2026, 11, 4));
        await RunFailingAsync(e, batch: 1);
        Assert.Empty(await SummariesAsync(e));

        At(e, 2026, 11, 19);                                                                 // D+15: N4 (D+14) of both groups is also due now
        await RunAsync(e);
        var summaries = await SummariesAsync(e);
        var forD = summaries.Where(n => n.Key == KeyOf(e, 11, 4, "N3")).ToList();
        var today = summaries.Where(n => n.Key == KeyOf(e, 11, 19, "N4")).ToList();
        Assert.NotEmpty(forD);
        Assert.All(forD, n => Assert.StartsWith("Ngày 04/11/2026: 2 nhóm nợ vừa đến hạn", n.Message));
        Assert.All(forD, n => Assert.Contains("(tổng hợp lúc 12:00 19/11/2026)", n.Message));        // when the summary was made (the figures are as of then)
        Assert.NotEmpty(today);
        Assert.All(today, n => Assert.StartsWith("2 nhóm nợ đã quá hạn 14 ngày", n.Message));   // today's own figures, not added to D's
        Assert.Equal(2, summaries.Select(n => n.Key).Distinct().Count());

        var before = await NoticesAsync(e);
        await RunAsync(e);
        Assert.Equal(before, await NoticesAsync(e));
        Assert.Equal(summaries.Count, (await SummariesAsync(e)).Count);
    }

    // Two stores: the figures, the recipients and the "already written" check stay inside the store the job runs for.
    [CreditDbFact]
    public async Task Two_stores_do_not_add_up_or_receive_each_others_summaries()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await StartRemindersAsync(e);
        At(e, 2026, 11, 4);
        await TwoGroupsDueAsync(e, new DateOnly(2026, 11, 4));
        await RunFailingAsync(e, batch: 1);

        // Store B (inactive, so A stays the active store): an owner, a Farmer with a debt account, that Farmer's reminder of the
        // same day and milestone, and B's own summary of that day already written for B's owner.
        Guid storeB, ownerB;
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var store = new Store("STORE-B", "Store B", "2 Street", "Province", status: StoreStatus.Inactive);
            var ownerRole = await db.Roles.SingleAsync(r => r.Code == RoleCode.StoreOwner, Token);
            var farmerRole = await db.Roles.SingleAsync(r => r.Code == RoleCode.Farmer, Token);
            var owner = new User(ownerRole.Id, "Owner B", "hash", $"{Guid.NewGuid():N}@example.test", null);
            var farmerUser = new User(farmerRole.Id, "Farmer B", "hash", $"{Guid.NewGuid():N}@example.test", null);
            var farmer = new FarmerProfile(farmerUser.Id);
            db.AddRange(store, owner, farmerUser, farmer, new StoreMember(store.Id, owner.Id), new DebtAccount(store.Id, farmer.Id));
            await db.SaveChangesAsync(Token);
            (storeB, ownerB) = (store.Id, owner.Id);
            await SeedAsync(e, farmerUser.Id, $"debt-reminder:{farmer.Id}:20261104:N3");
            await SeedAsync(e, ownerB, KeyOf(e, 11, 4, "N3", storeB));
        }

        At(e, 2026, 11, 5);
        await RunAsync(e);
        var summaries = await SummariesAsync(e);
        var forA = summaries.Where(n => n.Key == KeyOf(e, 11, 4, "N3")).ToList();
        Assert.NotEmpty(forA);                                                                // B's summary of that day did not stop A's
        Assert.All(forA, n => Assert.StartsWith("Ngày 04/11/2026: 2 nhóm nợ", n.Message));    // B's reminder was not counted (not 3)
        Assert.DoesNotContain(forA, n => n.User == ownerB);                                   // and B's owner did not receive A's
        var forB = summaries.Where(n => n.Key.StartsWith($"debt-reminder-staff:{storeB:N}:", StringComparison.Ordinal)).ToList();
        var seededForB = Assert.Single(forB);                                                 // B's own summary stayed as seeded, once
        Assert.Equal((KeyOf(e, 11, 4, "N3", storeB), ownerB), (seededForB.Key, seededForB.User));
    }

    // A member who left before the late run does not receive the historic summary; the others do. A member who joined after day D
    // does not either.
    [CreditDbFact]
    public async Task A_member_who_was_removed_does_not_receive_the_recovered_summary()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await StartRemindersAsync(e);
        At(e, 2026, 11, 4);
        var leaver = await SalesMemberAsync(e);
        var stayer = await SalesMemberAsync(e);
        await TwoGroupsDueAsync(e, new DateOnly(2026, 11, 4));
        await RunFailingAsync(e, batch: 1);

        At(e, 2026, 11, 10);
        var joinedLater = await SalesMemberAsync(e);
        At(e, 2026, 11, 12);
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            (await db.StoreMembers.SingleAsync(m => m.UserId == leaver, Token)).Leave(new DateOnly(2026, 11, 12));
            await db.SaveChangesAsync(Token);
        }

        await RunAsync(e);
        var forD = (await SummariesAsync(e)).Where(n => n.Key == KeyOf(e, 11, 4, "N3")).Select(n => n.User).ToList();
        Assert.Contains(stayer, forD);
        Assert.DoesNotContain(leaver, forD);                                                  // no longer a member when it was written
        Assert.DoesNotContain(joinedLater, forD);                                             // not there on day D
        Assert.Equal(forD.Count, forD.Distinct().Count());
    }

    // Many days missing at once: each run takes at most summaryBatch of them, oldest first; the rest follow in the next runs, none is
    // skipped or written twice, and once everything is written another run adds nothing.
    [CreditDbFact]
    public async Task Missing_summaries_are_recovered_in_batches_oldest_first_until_all_are_written()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            if (!await db.DebtAccounts.AnyAsync(a => a.FarmerProfileId == e.FarmerId, Token)) db.DebtAccounts.Add(new DebtAccount(e.StoreId, e.FarmerId));
            await db.SaveChangesAsync(Token);
        }

        await StartRemindersAsync(e);
        for (var day = 1; day <= 7; day++)                                                    // Farmer reminders saved on 01..07/11, no summaries
        {
            At(e, 2026, 11, day);
            await SeedAsync(e, e.FarmerUserId, $"debt-reminder:{e.FarmerId}:2026010{day}:N3");
        }

        At(e, 2026, 11, 20);
        var farmerNotices = (await NoticesAsync(e)).Where(n => n.Key.StartsWith("debt-reminder:", StringComparison.Ordinal)).ToList();
        async Task<List<string>> KeysDoneAsync() => (await SummariesAsync(e)).Select(n => n.Key).Distinct().Order(StringComparer.Ordinal).ToList();

        await RunAsync(e, summaryBatch: 2);
        Assert.Equal([KeyOf(e, 11, 1, "N3"), KeyOf(e, 11, 2, "N3")], await KeysDoneAsync());            // the two oldest days
        await RunAsync(e, summaryBatch: 2);
        Assert.Equal(Enumerable.Range(1, 4).Select(d => KeyOf(e, 11, d, "N3")).ToList(), await KeysDoneAsync());
        await RunAsync(e, summaryBatch: 2);
        await RunAsync(e, summaryBatch: 2);
        Assert.Equal(Enumerable.Range(1, 7).Select(d => KeyOf(e, 11, d, "N3")).ToList(), await KeysDoneAsync());

        var summaries = await SummariesAsync(e);
        Assert.Equal(summaries.Count, summaries.Select(n => (n.Key, n.User)).Distinct().Count());       // none written twice
        Assert.All(summaries, n => Assert.StartsWith("Ngày ", n.Message));
        Assert.All(summaries, n => Assert.Contains("1 nhóm nợ", n.Message));
        await RunAsync(e, summaryBatch: 2);
        Assert.Equal(summaries.Count, (await SummariesAsync(e)).Count);
        Assert.Equal(farmerNotices, (await NoticesAsync(e)).Where(n => n.Key.StartsWith("debt-reminder:", StringComparison.Ordinal)).ToList());
    }
}
