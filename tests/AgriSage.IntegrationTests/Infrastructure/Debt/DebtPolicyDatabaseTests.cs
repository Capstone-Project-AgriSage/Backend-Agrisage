using System.Text.Json;
using AgriSage.Application.Common.Exceptions;
using AgriSage.Application.Features.Debt;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// F3.12 against real PostgreSQL: the store debt policy (CS-05, CS-08, CS-09, CS-14, D4, D5).
public sealed class DebtPolicyDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly UpdateDebtPolicyRequest Changed =
        new(45, new(-10, 7, 21), 2, 3, 3, 120, 200, Version: 0, Reason: "Chính sách vụ mới");

    private static async Task<T> Run<T>(CreditTestDatabase e, Func<IDebtPolicyService, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider.GetRequiredService<IDebtPolicyService>());
    }

    private static async Task<(int Rows, int Audits)> CountAsync(CreditTestDatabase e)
    {
        using var s = e.Scope();
        var db = s.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        return (await db.StoreDebtPolicies.IgnoreQueryFilters().CountAsync(Token),
            await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == "DEBT_POLICY_CHANGED", Token));
    }

    [CreditDbFact]
    public async Task A_store_without_a_policy_reads_the_documented_defaults_without_saving_them()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);

        var policy = await Run(e, s => s.GetAsync(Token));

        Assert.Equal((30, -7, 14, 30, 1, 2, 2, (int?)null, 365, 0L, true),
            (policy.DefaultGraceDays, policy.ReminderOffsets.N2, policy.ReminderOffsets.N4, policy.ReminderOffsets.N5,
                policy.StaffRolloverLimit, policy.OwnerReviewAfterRollovers, policy.OnTimeSeasonsForLimitRaise,
                policy.MaxCreditTermDays, policy.MaxTotalExtensionDays, policy.Version, policy.IsDefault));
        Assert.Equal((0, 0), await CountAsync(e));
    }

    [CreditDbFact]
    public async Task The_owner_saves_the_policy_once_and_then_updates_it_with_its_version()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);

        // Creating needs the version the default was read with.
        await Assert.ThrowsAsync<ConflictException>(() => Run(e, s => s.UpdateAsync(Changed with { Version = 3 }, Token)));
        Assert.Equal((0, 0), await CountAsync(e));

        var saved = await Run(e, s => s.UpdateAsync(Changed, Token));
        Assert.Equal((45, -10, 7, 21, 2, 3, 3, (int?)120, 200, 1L, false),
            (saved.DefaultGraceDays, saved.ReminderOffsets.N2, saved.ReminderOffsets.N4, saved.ReminderOffsets.N5,
                saved.StaffRolloverLimit, saved.OwnerReviewAfterRollovers, saved.OnTimeSeasonsForLimitRaise,
                saved.MaxCreditTermDays, saved.MaxTotalExtensionDays, saved.Version, saved.IsDefault));
        Assert.Equal(saved, await Run(e, s => s.GetAsync(Token)));

        // A second editor still holding the default (version 0) does not overwrite the first save.
        await Assert.ThrowsAsync<ConflictException>(() => Run(e, s => s.UpdateAsync(Changed with { DefaultGraceDays = 10 }, Token)));

        var updated = await Run(e, s => s.UpdateAsync(Changed with { MaxCreditTermDays = null, Version = saved.Version, Reason = null }, Token));
        Assert.Equal(((int?)null, 2L), (updated.MaxCreditTermDays, updated.Version));
        await Assert.ThrowsAsync<ConflictException>(() => Run(e, s => s.UpdateAsync(Changed with { Version = saved.Version }, Token)));
        Assert.Equal((1, 2), await CountAsync(e));

        using var verify = e.Scope();
        var db = verify.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        var row = await db.StoreDebtPolicies.AsNoTracking().SingleAsync(Token);
        Assert.Equal((e.StoreId, 2L, 45), (row.StoreId, row.Version, row.DefaultGraceDays));
        // The fixture clock does not move: the first save is the one with a reason.
        var audits = (await db.AuditLogs.AsNoTracking().Where(a => a.Action == "DEBT_POLICY_CHANGED").ToListAsync(Token))
            .OrderBy(a => a.Reason is null).ToList();
        Assert.All(audits, a => Assert.Equal(("STORE_DEBT_POLICY", row.Id, e.StoreId, e.User.UserId), (a.EntityType, a.EntityId!.Value, a.StoreId!.Value, a.ActorUserId)));
        // jsonb normalizes the text, so the values are read as JSON.
        static JsonElement Json(string? value) => JsonDocument.Parse(value!).RootElement;
        Assert.Equal((30, -7, 45, -10), (Json(audits[0].OldValues).GetProperty("defaultGraceDays").GetInt32(),
            Json(audits[0].OldValues).GetProperty("reminderOffsets").GetProperty("n2").GetInt32(),
            Json(audits[0].NewValues).GetProperty("defaultGraceDays").GetInt32(),
            Json(audits[0].NewValues).GetProperty("reminderOffsets").GetProperty("n2").GetInt32()));
        Assert.Equal("Chính sách vụ mới", audits[0].Reason);
        Assert.Equal(120, Json(audits[1].OldValues).GetProperty("maxCreditTermDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, Json(audits[1].NewValues).GetProperty("maxCreditTermDays").ValueKind);
        Assert.Null(audits[1].Reason);
    }

    [CreditDbFact]
    public async Task Saving_the_same_values_changes_nothing()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var saved = await Run(e, s => s.UpdateAsync(Changed, Token));

        var same = await Run(e, s => s.UpdateAsync(Changed with { Version = saved.Version }, Token));

        Assert.Equal(saved, same);
        Assert.Equal((1, 1), await CountAsync(e));
    }

    [CreditDbFact]
    public async Task Saving_the_defaults_creates_the_row_without_changing_any_value()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var defaults = await Run(e, s => s.GetAsync(Token));

        var saved = await Run(e, s => s.UpdateAsync(new(defaults.DefaultGraceDays, defaults.ReminderOffsets, defaults.StaffRolloverLimit,
            defaults.OwnerReviewAfterRollovers, defaults.OnTimeSeasonsForLimitRaise, defaults.MaxCreditTermDays,
            defaults.MaxTotalExtensionDays, defaults.Version), Token));

        Assert.Equal(defaults with { IsDefault = false }, saved);
        Assert.Equal((1, 1), await CountAsync(e));
    }

    // The Domain guards every value even when the request validator is bypassed; a refused change saves nothing.
    [CreditDbFact]
    public async Task Invalid_values_are_refused_and_nothing_is_saved()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        UpdateDebtPolicyRequest[] invalid =
        [
            Changed with { DefaultGraceDays = -1 },
            Changed with { DefaultGraceDays = 366 },
            Changed with { ReminderOffsets = new(0, 7, 21) },
            Changed with { ReminderOffsets = new(-10, 0, 21) },
            Changed with { ReminderOffsets = new(-10, 21, 21) },
            Changed with { StaffRolloverLimit = -1, OwnerReviewAfterRollovers = 0 },
            Changed with { StaffRolloverLimit = 3, OwnerReviewAfterRollovers = 2 },
            // Review finding 3: at 0 every entry would need a decision.
            Changed with { StaffRolloverLimit = 0, OwnerReviewAfterRollovers = 0 },
            Changed with { OnTimeSeasonsForLimitRaise = 0 },
            Changed with { MaxCreditTermDays = 0 },
            Changed with { MaxTotalExtensionDays = 0 },
        ];

        // Before the first save: no row is created.
        foreach (var request in invalid)
        {
            await Assert.ThrowsAsync<DomainException>(() => Run(e, s => s.UpdateAsync(request, Token)));
        }

        Assert.Equal((0, 0), await CountAsync(e));
        Assert.True((await Run(e, s => s.GetAsync(Token))).IsDefault);

        // After it: the saved policy keeps its values and version.
        var saved = await Run(e, s => s.UpdateAsync(Changed, Token));
        foreach (var request in invalid)
        {
            await Assert.ThrowsAsync<DomainException>(() => Run(e, s => s.UpdateAsync(request with { Version = saved.Version }, Token)));
        }

        Assert.Equal(saved, await Run(e, s => s.GetAsync(Token)));
        Assert.Equal((1, 1), await CountAsync(e));
    }

    // CS-05: the default grace only proposes the grace of a new season; seasons already set keep theirs.
    [CreditDbFact]
    public async Task Changing_the_default_grace_leaves_existing_seasons_alone()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var dx = await RunSeason(e, s => s.CreateAsync(new("DX-2026", "Đông Xuân 2026–2027", new DateOnly(2027, 2, 15),
            new DateOnly(2027, 3, 15), null, new DateOnly(2026, 10, 1)), Token));
        Assert.Equal(30, dx.GraceDays);

        await Run(e, s => s.UpdateAsync(Changed, Token));

        var kept = await RunSeason(e, s => s.GetAsync(dx.Id, Token));
        Assert.Equal((30, new DateOnly(2027, 4, 14), dx.Version), (kept.GraceDays, kept.PaymentDueDate, kept.Version));
        var next = await RunSeason(e, s => s.CreateAsync(new("HT-2027", "Hè Thu 2027", new DateOnly(2027, 7, 15), new DateOnly(2027, 8, 15)), Token));
        Assert.Equal((45, new DateOnly(2027, 9, 29)), (next.GraceDays, next.PaymentDueDate));
    }

    [CreditDbFact]
    public async Task Sale_staff_read_the_policy_but_cannot_change_it()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        var saved = await Run(e, s => s.UpdateAsync(Changed, Token));
        var staff = await OwnerOnlyDebtDatabaseTests.AddStaffAsync(e, OwnerOnlyDebtDatabaseTests.NoGrant);
        var owner = e.User.UserId;
        e.User.UserId = staff.UserId; e.User.Role = "SALES_STAFF";
        try
        {
            Assert.Equal(saved, await Run(e, s => s.GetAsync(Token)));
            await Assert.ThrowsAsync<ForbiddenException>(() => Run(e, s => s.UpdateAsync(Changed with { Version = saved.Version, DefaultGraceDays = 10 }, Token)));
        }
        finally
        {
            e.User.UserId = owner; e.User.Role = "STORE_OWNER";
        }

        Assert.Equal(saved, await Run(e, s => s.GetAsync(Token)));
        Assert.Equal((1, 1), await CountAsync(e));
    }

    private static async Task<T> RunSeason<T>(CreditTestDatabase e, Func<ICropSeasonService, Task<T>> act)
    {
        using var s = e.Scope();
        return await act(s.ServiceProvider.GetRequiredService<ICropSeasonService>());
    }
}
