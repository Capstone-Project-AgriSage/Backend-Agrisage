using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.UnitTests.Domain.Features.Debt;

// F3.8 part 2: origin season vs collection season (D9), rollover (CS-07, CS-08, D5), reason codes on reductions (CS-11)
// and the store debt policy.
public class DebtRolloverTests
{
    private static readonly DateTimeOffset Now = new(2027, 4, 28, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly SeasonDue = new(2027, 4, 14);
    private static readonly DateOnly NextSeasonDue = new(2027, 9, 14);
    private static readonly Guid DongXuan = Guid.NewGuid();
    private static readonly Guid HeThu = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();

    private readonly DebtAccount _account = new(Guid.NewGuid(), Guid.NewGuid());

    private DebtEntry Entry(Guid? season = null) => _account.CreateCreditSaleEntry("DE-0001", DebtEntrySourceType.Pickup,
        Guid.NewGuid(), Guid.NewGuid(), 1_200_000, 0, SeasonDue, Staff, Now, originCropSeasonId: season ?? DongXuan).Entry;

    [Fact]
    public void A_new_entry_records_its_origin_season_original_due_date_and_no_rollover()
    {
        var entry = Entry();
        Assert.Equal((DongXuan, DongXuan, SeasonDue, SeasonDue, 0),
            (entry.OriginCropSeasonId!.Value, entry.CollectionCropSeasonId!.Value, entry.OriginalDueDate, entry.DueDate, entry.RolloverCount));
    }

    [Fact]
    public void An_entry_keeps_the_due_basis_it_was_created_with()
    {
        const string basis = "{\"rule\":\"SEASON\",\"seasonCode\":\"DX-2026\",\"harvestEnd\":\"2027-03-15\",\"graceDays\":30}";
        var entry = _account.CreateCreditSaleEntry("DE-0003", DebtEntrySourceType.Pickup, Guid.NewGuid(), Guid.NewGuid(),
            1_000, 0, SeasonDue, Staff, Now, originCropSeasonId: DongXuan, dueBasis: basis).Entry;
        Assert.Equal(basis, entry.DueBasis);
        Assert.Null(Entry().DueBasis);
    }

    [Fact]
    public void An_entry_without_a_season_keeps_both_seasons_empty()
    {
        var entry = _account.CreateManualAdjustmentEntry("DE-0002", 500_000, SeasonDue, Staff, Now).Entry;
        Assert.Null(entry.OriginCropSeasonId);
        Assert.Null(entry.CollectionCropSeasonId);
        Assert.Equal(SeasonDue, entry.OriginalDueDate);
    }

    [Fact]
    public void Rollover_moves_the_collection_season_and_due_date_but_never_the_origin_or_the_amount()
    {
        var entry = Entry();
        var action = entry.Rollover(HeThu, NextSeasonDue, DebtReasonCode.MatMua, "Khách xin trả sau vụ Hè Thu", Staff);

        Assert.Equal((DongXuan, HeThu, SeasonDue, NextSeasonDue, 1),
            (entry.OriginCropSeasonId!.Value, entry.CollectionCropSeasonId!.Value, entry.OriginalDueDate, entry.DueDate, entry.RolloverCount));
        Assert.Equal((1_200_000m, DebtEntryStatus.Open), (entry.OutstandingAmount, entry.Status));
        Assert.Equal(DebtEntryActionType.Rollover, action.ActionType);
        Assert.Equal((DongXuan, HeThu, SeasonDue, NextSeasonDue), (action.OldCropSeasonId!.Value, action.NewCropSeasonId!.Value, action.OldDueDate!.Value, action.NewDueDate!.Value));
        Assert.Equal(DebtReasonCode.MatMua, action.ReasonCode);
        Assert.Null(action.AdjustmentAmount);
    }

    [Fact]
    public void A_second_rollover_counts_twice_and_keeps_the_original_due_date()
    {
        var entry = Entry();
        var later = Guid.NewGuid();
        entry.Rollover(HeThu, NextSeasonDue, DebtReasonCode.MatMua, "Lần 1", Staff);
        entry.Rollover(later, new DateOnly(2028, 1, 14), DebtReasonCode.ThienTai, "Lần 2", Staff);
        Assert.Equal((2, SeasonDue, later), (entry.RolloverCount, entry.OriginalDueDate, entry.CollectionCropSeasonId!.Value));
    }

    [Fact]
    public void Rollover_is_refused_for_settled_or_disputed_entries()
    {
        var paid = Entry();
        _account.AdjustEntry(paid, 1_200_000, "Trả đủ", Staff, Now);
        Assert.Throws<DomainException>(() => paid.Rollover(HeThu, NextSeasonDue, DebtReasonCode.MatMua, "x", Staff));

        var cancelled = Entry();
        _account.CancelEntry(cancelled, "Xóa", Staff, Now, DebtReasonCode.ThienTai);
        Assert.Throws<DomainException>(() => cancelled.Rollover(HeThu, NextSeasonDue, DebtReasonCode.MatMua, "x", Staff));

        var disputed = Entry();
        disputed.Dispute("Khách khiếu nại", Staff);
        Assert.Throws<DomainException>(() => disputed.Rollover(HeThu, NextSeasonDue, DebtReasonCode.MatMua, "x", Staff));
    }

    [Fact]
    public void Rollover_needs_a_later_due_date_another_season_a_hardship_code_and_a_note()
    {
        var entry = Entry();
        Assert.Throws<DomainException>(() => entry.Rollover(HeThu, SeasonDue, DebtReasonCode.MatMua, "x", Staff));
        Assert.Throws<DomainException>(() => entry.Rollover(HeThu, SeasonDue.AddDays(-1), DebtReasonCode.MatMua, "x", Staff));
        Assert.Throws<DomainException>(() => entry.Rollover(DongXuan, NextSeasonDue, DebtReasonCode.MatMua, "x", Staff));
        Assert.Throws<DomainException>(() => entry.Rollover(Guid.Empty, NextSeasonDue, DebtReasonCode.MatMua, "x", Staff));
        Assert.Throws<DomainException>(() => entry.Rollover(HeThu, NextSeasonDue, DebtReasonCode.SaiSot, "x", Staff));
        Assert.Throws<DomainException>(() => entry.Rollover(HeThu, NextSeasonDue, DebtReasonCode.MatMua, " ", Staff));
        Assert.Equal((0, SeasonDue), (entry.RolloverCount, entry.DueDate));
        Assert.Empty(entry.Actions);
    }

    [Fact]
    public void Reductions_record_their_reason_code_and_reject_an_undefined_one()
    {
        var adjusted = Entry();
        _account.AdjustEntry(adjusted, 200_000, "Bỏ phần lẻ", Staff, Now, DebtReasonCode.TriAn);
        Assert.Equal(DebtReasonCode.TriAn, adjusted.Actions.Single().ReasonCode);

        var cancelled = Entry();
        _account.CancelEntry(cancelled, "Mất vụ do lũ", Staff, Now, DebtReasonCode.ThienTai);
        Assert.Equal(DebtReasonCode.ThienTai, cancelled.Actions.Single().ReasonCode);

        var other = Entry();
        Assert.Throws<DomainException>(() => _account.AdjustEntry(other, 100, "x", Staff, Now, (DebtReasonCode)99));
        Assert.Equal(1_200_000m, other.OutstandingAmount);
    }

    // D7: actions recorded before the policy have no code; the domain keeps accepting a missing code so legacy callers
    // keep working until the application makes it mandatory (F3.14).
    [Fact]
    public void A_reduction_without_a_code_stays_possible_for_existing_callers()
    {
        var entry = Entry();
        _account.AdjustEntry(entry, 100, "Legacy", Staff, Now);
        Assert.Null(entry.Actions.Single().ReasonCode);
    }

    [Fact]
    public void Store_debt_policy_has_the_documented_defaults()
    {
        var policy = new StoreDebtPolicy(Guid.NewGuid());
        Assert.Equal((30, -7, 14, 30, 1, 2, 2, (int?)null, 365),
            (policy.DefaultGraceDays, policy.ReminderOffsets.N2, policy.ReminderOffsets.N4, policy.ReminderOffsets.N5,
             policy.StaffRolloverLimit, policy.OwnerReviewAfterRollovers, policy.OnTimeSeasonsForLimitRaise,
             policy.MaxCreditTermDays, policy.MaxTotalExtensionDays));
    }

    [Theory]
    [InlineData(-1, -7, 14, 30, 1, 2, 2, null, 365)]   // grace 0..365
    [InlineData(30, 0, 14, 30, 1, 2, 2, null, 365)]    // N2 before the due date
    [InlineData(30, -7, 0, 30, 1, 2, 2, null, 365)]    // N4 after the due date
    [InlineData(30, -7, 14, 14, 1, 2, 2, null, 365)]   // N5 after N4
    [InlineData(30, -7, 14, 30, -1, 2, 2, null, 365)]  // staff limit not negative
    [InlineData(30, -7, 14, 30, 3, 2, 2, null, 365)]   // owner review not before the staff limit is used up
    [InlineData(30, -7, 14, 30, 1, 2, 0, null, 365)]   // at least one on-time season
    [InlineData(30, -7, 14, 30, 1, 2, 2, 0, 365)]      // cap at least one day
    [InlineData(30, -7, 14, 30, 1, 2, 2, null, 0)]     // extension limit at least one day
    public void Invalid_policy_values_are_rejected(int grace, int n2, int n4, int n5, int staff, int owner, int onTime, int? cap, int extension)
    {
        var policy = new StoreDebtPolicy(Guid.NewGuid());
        Assert.Throws<DomainException>(() => policy.Update(grace, new(n2, n4, n5), staff, owner, onTime, cap, extension));
        Assert.Equal(30, policy.DefaultGraceDays);
    }

    // CS-08: Sale staff may roll over an entry that has not been rolled over yet; from the second time the owner decides.
    // The total extension from the ORIGINAL due date is limited for staff; the owner may exceed it with a reason.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Staff_may_roll_over_only_within_the_staff_limit(int rolloverCount, bool allowed) =>
        Assert.Equal(allowed, new StoreDebtPolicy(Guid.NewGuid()).StaffMayRollOver(rolloverCount));

    [Fact]
    public void Total_extension_is_measured_from_the_original_due_date()
    {
        var policy = new StoreDebtPolicy(Guid.NewGuid());
        Assert.False(policy.ExceedsTotalExtension(SeasonDue, SeasonDue.AddDays(365)));
        Assert.True(policy.ExceedsTotalExtension(SeasonDue, SeasonDue.AddDays(366)));
    }
}
