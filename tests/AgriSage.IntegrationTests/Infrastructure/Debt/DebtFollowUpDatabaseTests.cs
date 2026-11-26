using AgriSage.Application.Common;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.23 (FLOW_3 §8j) on PostgreSQL, each test in its own schema: reminders made outside the system are logged per customer,
// listed newest first, labelled by the payments since the contact, shown on the staff lists and dashboard, never to the Farmer.
public sealed class DebtFollowUpDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateOnly Today(CreditTestDatabase e) => BusinessCalendar.Today(e.Clock.UtcNow);

    // 3 000 000 owed, due tomorrow; the clock then moves `days` ahead.
    private static async Task OweAsync(CreditTestDatabase e, int days)
    {
        using (var s = e.Scope())
            await s.ServiceProvider.GetRequiredService<IDebtService>().ManualAsync(e.FarmerId, new(3_000_000, Today(e).AddDays(1), "Nợ vụ trước"), Token);
        e.Clock.UtcNow = e.Clock.UtcNow.AddDays(days);
    }

    private static async Task<DebtFollowUpResponse> RecordAsync(CreditTestDatabase e, DebtFollowUpRequest request)
    {
        using var s = e.Scope();
        return await s.ServiceProvider.GetRequiredService<IDebtFollowUpService>().RecordAsync(e.FarmerId, request, Token);
    }

    private static async Task<IReadOnlyList<DebtFollowUpResponse>> ListAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        return (await s.ServiceProvider.GetRequiredService<IDebtFollowUpService>().ListAsync(e.FarmerId, new(), Token)).Items;
    }

    private static async Task PayAsync(CreditTestDatabase e, decimal amount)
    {
        using var s = e.Scope();
        await s.ServiceProvider.GetRequiredService<IPaymentService>().ReceiveCashAsync(new("DEBT_REPAYMENT", amount, FarmerProfileId: e.FarmerId), Token);
    }

    [CreditDbFact]
    public async Task A_reminder_is_logged_listed_newest_first_and_its_promise_follows_the_payments()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await OweAsync(e, days: 11); // 10 days overdue
        int Notifications() { using var s = e.Scope(); return s.ServiceProvider.GetRequiredService<AgriSageDbContext>().Notifications.Count(); }
        var notificationsBefore = Notifications();

        var call = await RecordAsync(e, new("PHONE_CALL", "NO_ANSWER", ContactedAt: e.Clock.UtcNow.AddDays(-1)));
        var visit = await RecordAsync(e, new("VISIT", "PROMISED", "Hẹn bán lúa xong trả", PromisedDate: Today(e).AddDays(3), PromisedAmount: 1_000_000));
        Assert.Null(call.PromiseStatus);
        Assert.Equal(("WAITING", 3_000_000m, 3_000_000m), (visit.PromiseStatus, visit.OutstandingAmount, visit.OverdueAmount));
        Assert.Equal(("VISIT", "PROMISED", "Hẹn bán lúa xong trả"), (visit.Channel, visit.Outcome, visit.Note));
        Assert.Equal((e.User.UserId, "STORE_OWNER"), (visit.CreatedBy.Id, visit.CreatedBy.Role));
        Assert.False(string.IsNullOrEmpty(visit.CreatedBy.Name));
        Assert.Equal(notificationsBefore, Notifications()); // logging sends nothing

        var list = await ListAsync(e);
        Assert.Equal(new[] { visit.Id, call.Id }, list.Select(f => f.Id));

        Assert.Equal((false, true), (list[0].HasLaterContact, list[1].HasLaterContact)); // the call came before the visit

        // Part paid: still waiting. The date passes: "Quá hẹn". The rest arrives after the date: "Trả trễ hẹn", not kept.
        await PayAsync(e, 600_000);
        Assert.Equal(("WAITING", 600_000m), ((await ListAsync(e))[0].PromiseStatus, (await ListAsync(e))[0].PaidSinceContact));
        e.Clock.UtcNow = e.Clock.UtcNow.AddDays(4);
        Assert.Equal("BROKEN", (await ListAsync(e))[0].PromiseStatus);
        await PayAsync(e, 400_000);
        Assert.Equal(("LATE", 1_000_000m), ((await ListAsync(e))[0].PromiseStatus, (await ListAsync(e))[0].PaidSinceContact));

        // Logging changed nothing on the debt: only the two payments did.
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            Assert.Equal(2_000_000m, await db.DebtAccounts.Where(a => a.Id == e.AccountId).Select(a => a.CurrentBalance).SingleAsync(Token));
            Assert.Equal(2, await db.DebtFollowUps.CountAsync(Token));
        }

        // A promise paid on its day is kept, and the older promise now has a later contact.
        var onTime = await RecordAsync(e, new("AT_STORE", "PROMISED", PromisedDate: Today(e), PromisedAmount: 500_000));
        await PayAsync(e, 500_000);
        Assert.Equal("KEPT", (await ListAsync(e)).Single(f => f.Id == onTime.Id).PromiseStatus);
        Assert.True((await ListAsync(e)).Single(f => f.Id == visit.Id).HasLaterContact);

        // The owner writes off the rest: the open promise is "Hết nợ", not "Đã trả theo hẹn"; the late one stays late.
        var forgiven = await RecordAsync(e, new("PHONE_CALL", "PROMISED", PromisedDate: Today(e).AddDays(5), PromisedAmount: 1_000_000));
        using (var s = e.Scope())
        {
            var entryId = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.Select(x => x.Id).SingleAsync(Token);
            await s.ServiceProvider.GetRequiredService<IDebtService>().ActionAsync(entryId, "CANCEL", "Khách khó khăn, xóa phần còn lại", null, null, "KHO_KHAN", Token);
        }
        var final = await ListAsync(e);
        Assert.Equal("CLEARED", final.Single(f => f.Id == forgiven.Id).PromiseStatus);
        Assert.Equal("LATE", final.Single(f => f.Id == visit.Id).PromiseStatus);
        Assert.Equal("KEPT", final.Single(f => f.Id == onTime.Id).PromiseStatus);
    }

    [CreditDbFact]
    public async Task A_payment_taken_just_before_logging_a_reminder_does_not_count_toward_its_promise()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await OweAsync(e, days: 11);
        var minute = e.Clock.UtcNow; // the test clock starts on a whole minute
        Assert.Equal(0, minute.Second);

        // Seen live on 2026-11-26: cash at :42, then a reminder for "now" saved at :53. The form sends the contact time to the
        // minute (:00), so the payment fell after the contact time although it came before the reminder.
        e.Clock.UtcNow = minute.AddSeconds(42);
        await PayAsync(e, 100_000);
        e.Clock.UtcNow = minute.AddSeconds(53);
        var now = await RecordAsync(e, new("AT_STORE", "PROMISED", PromisedDate: Today(e).AddDays(3), PromisedAmount: 100_000, ContactedAt: minute));
        Assert.Equal(("WAITING", 0m), (now.PromiseStatus, now.PaidSinceContact));

        // A visit written down later counts every payment since the visit, including one taken before it was written down.
        var visit = await RecordAsync(e, new("VISIT", "PROMISED", PromisedDate: Today(e).AddDays(3), PromisedAmount: 100_000, ContactedAt: minute.AddHours(-2)));
        Assert.Equal(("KEPT", 100_000m), (visit.PromiseStatus, visit.PaidSinceContact));

        // The next payment, after the reminder was saved, keeps the "now" promise.
        e.Clock.UtcNow = minute.AddSeconds(75);
        await PayAsync(e, 100_000);
        var list = await ListAsync(e);
        Assert.Equal(("KEPT", 100_000m), (list.Single(f => f.Id == now.Id).PromiseStatus, list.Single(f => f.Id == now.Id).PaidSinceContact));
        Assert.Equal(200_000m, list.Single(f => f.Id == visit.Id).PaidSinceContact);
    }

    [CreditDbFact]
    public async Task Wrong_or_pointless_reminders_are_refused_and_the_table_checks_hold()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAsync(e, new("PHONE_CALL", "NO_ANSWER"))); // owes nothing yet
        await OweAsync(e, days: 11);

        await Assert.ThrowsAsync<DomainException>(() => RecordAsync(e, new("PHONE_CALL", "NO_ANSWER", ContactedAt: e.Clock.UtcNow.AddDays(-31))));
        await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAsync(e, new("PHONE_CALL", "PROMISED", PromisedDate: Today(e).AddDays(-1))));
        await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAsync(e, new("PHONE_CALL", "PROMISED", PromisedDate: Today(e).AddDays(366))));
        await Assert.ThrowsAsync<DomainException>(() => RecordAsync(e, new("PHONE_CALL", "PROMISED", PromisedDate: Today(e), PromisedAmount: 3_000_001)));
        using (var s = e.Scope())
            await Assert.ThrowsAsync<NotFoundException>(() => s.ServiceProvider.GetRequiredService<IDebtFollowUpService>()
                .RecordAsync(Guid.NewGuid(), new("PHONE_CALL", "NO_ANSWER"), Token));

        // The database refuses a promise without its date and a promise on another outcome, whatever the code does.
        var followUp = await RecordAsync(e, new("AT_STORE", "ASKED_MORE_TIME"));
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            var promiseWithoutDate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE debt_follow_ups SET outcome = 'PROMISED' WHERE id = {followUp.Id}", Token));
            Assert.Equal("ck_debt_follow_ups_promise", promiseWithoutDate.ConstraintName);
            var dateOnOtherOutcome = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE debt_follow_ups SET promised_date = DATE '2026-12-01' WHERE id = {followUp.Id}", Token));
            Assert.Equal("ck_debt_follow_ups_promise", dateOnOtherOutcome.ConstraintName);
        }

        // Nothing owed: nothing to remind.
        await PayAsync(e, 3_000_000);
        await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAsync(e, new("PHONE_CALL", "NO_ANSWER")));
    }

    [CreditDbFact]
    public async Task Staff_lists_and_the_dashboard_show_the_latest_reminder_and_the_Farmer_never_sees_it()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await OweAsync(e, days: 40); // past due + N5 (30 days): "Cần quyết định"

        async Task<DebtDashboardResponse> DashboardAsync()
        {
            using var s = e.Scope();
            return await s.ServiceProvider.GetRequiredService<IDebtService>().DashboardAsync(Token);
        }

        var before = await DashboardAsync();
        Assert.Equal(new DebtFollowUpDashboard(7, 1, 1, 0), before.FollowUp);
        Assert.Null(Assert.Single(before.NeedsDecision).LastFollowUp);

        await RecordAsync(e, new("PHONE_CALL", "NO_ANSWER", ContactedAt: e.Clock.UtcNow.AddHours(-2)));
        await RecordAsync(e, new("MESSAGE", "PROMISED", "Nhắn Zalo", PromisedDate: Today(e).AddDays(1)));
        var after = await DashboardAsync();
        Assert.Equal(new DebtFollowUpDashboard(7, 1, 0, 0), after.FollowUp);
        var last = Assert.Single(after.NeedsDecision).LastFollowUp!;
        Assert.Equal(("MESSAGE", "PROMISED", Today(e).AddDays(1), "WAITING"), (last.Channel, last.Outcome, last.PromisedDate, last.PromiseStatus));
        Assert.False(string.IsNullOrEmpty(last.ByName));
        using (var s = e.Scope())
        {
            var staffList = await s.ServiceProvider.GetRequiredService<IDebtService>().EntriesAsync(new() { NeedsDecision = true }, Token);
            Assert.Equal("PROMISED", Assert.Single(staffList.Items).LastFollowUp!.Outcome);
        }

        // The Farmer's own list: same entry, no internal reminder log.
        var owner = (e.User.UserId, e.User.Role);
        (e.User.UserId, e.User.Role) = (e.FarmerUserId, "FARMER");
        using (var s = e.Scope())
            Assert.Null(Assert.Single((await s.ServiceProvider.GetRequiredService<IDebtService>().OwnEntriesAsync(new(), Token)).Items).LastFollowUp);
        (e.User.UserId, e.User.Role) = owner;

        // Two days on, the promise is broken and nobody has followed it up: it counts.
        e.Clock.UtcNow = e.Clock.UtcNow.AddDays(2);
        var broken = await DashboardAsync();
        Assert.Equal(new DebtFollowUpDashboard(7, 1, 0, 1), broken.FollowUp);
        Assert.Equal("BROKEN", Assert.Single(broken.NeedsDecision).LastFollowUp!.PromiseStatus);

        // A later record takes over (here one correcting a wrong promise): the broken promise no longer counts.
        await RecordAsync(e, new("OTHER", "OTHER", "Lần trước ghi nhầm, khách chưa hứa ngày nào"));
        var corrected = await DashboardAsync();
        Assert.Equal(new DebtFollowUpDashboard(7, 1, 0, 0), corrected.FollowUp);
        Assert.Equal("OTHER", Assert.Single(corrected.NeedsDecision).LastFollowUp!.Outcome);

        // Eight days on, nobody has reminded the customer within the week.
        e.Clock.UtcNow = e.Clock.UtcNow.AddDays(8);
        Assert.Equal(new DebtFollowUpDashboard(7, 1, 1, 0), (await DashboardAsync()).FollowUp);

        // A disputed debt is not one to remind of, as on "Cần quyết định".
        using (var s = e.Scope())
        {
            var entryId = await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().DebtEntries.Select(x => x.Id).SingleAsync(Token);
            await s.ServiceProvider.GetRequiredService<IDebtService>().ActionAsync(entryId, "DISPUTE", "Khách nói đã trả tiền mặt", null, null, null, Token);
        }
        Assert.Equal(new DebtFollowUpDashboard(7, 0, 0, 0), (await DashboardAsync()).FollowUp);
    }
}
