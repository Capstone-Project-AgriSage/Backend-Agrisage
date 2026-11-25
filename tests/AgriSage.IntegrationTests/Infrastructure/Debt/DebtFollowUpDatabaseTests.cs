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

        // Part paid: still waiting. The date passes: "Quá hẹn". The rest of the promised amount arrives: kept.
        await PayAsync(e, 600_000);
        Assert.Equal(("WAITING", 600_000m), ((await ListAsync(e))[0].PromiseStatus, (await ListAsync(e))[0].PaidSinceContact));
        e.Clock.UtcNow = e.Clock.UtcNow.AddDays(4);
        Assert.Equal("BROKEN", (await ListAsync(e))[0].PromiseStatus);
        await PayAsync(e, 400_000);
        Assert.Equal(("KEPT", 1_000_000m), ((await ListAsync(e))[0].PromiseStatus, (await ListAsync(e))[0].PaidSinceContact));

        // Logging changed nothing on the debt: only the two payments did.
        using (var s = e.Scope())
        {
            var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
            Assert.Equal(2_000_000m, await db.DebtAccounts.Where(a => a.Id == e.AccountId).Select(a => a.CurrentBalance).SingleAsync(Token));
            Assert.Equal(2, await db.DebtFollowUps.CountAsync(Token));
        }
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

        // Two days on, the promise is broken; eight days on, nobody has reminded the customer within the week.
        e.Clock.UtcNow = e.Clock.UtcNow.AddDays(2);
        var broken = await DashboardAsync();
        Assert.Equal(new DebtFollowUpDashboard(7, 1, 0, 1), broken.FollowUp);
        Assert.Equal("BROKEN", Assert.Single(broken.NeedsDecision).LastFollowUp!.PromiseStatus);
        e.Clock.UtcNow = e.Clock.UtcNow.AddDays(6);
        Assert.Equal(new DebtFollowUpDashboard(7, 1, 1, 1), (await DashboardAsync()).FollowUp);
    }
}
