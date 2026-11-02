using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;

namespace AgriSage.Domain.Features.Debt.Entities;

// Reminder offsets in days from the due date (CS-09, D10): N2 before it, N4 and N5 after it. N1 is the season's harvest
// start and N3 the due date itself, so they need no setting.
public sealed record DebtReminderOffsets(int N2, int N4, int N5)
{
    public static DebtReminderOffsets Default { get; } = new(-7, 14, 30);
}

// One per store (store_debt_policies): the owner's debt settings, with the defaults of the policy document.
public sealed class StoreDebtPolicy : SoftDeletableEntity, IHasConcurrencyVersion
{
    private StoreDebtPolicy()
    {
    }

    public StoreDebtPolicy(Guid storeId)
    {
        StoreId = Guard.NotEmpty(storeId);
        Update(30, DebtReminderOffsets.Default, staffRolloverLimit: 1, ownerReviewAfterRollovers: 2,
            onTimeSeasonsForLimitRaise: 2, maxCreditTermDays: null, maxTotalExtensionDays: 365);
    }

    public Guid StoreId { get; private set; }

    // CS-05: grace days after the harvest, proposed for a new season.
    public int DefaultGraceDays { get; private set; }

    public DebtReminderOffsets ReminderOffsets { get; private set; } = DebtReminderOffsets.Default;

    // CS-08: Sale staff may roll an entry over while it has fewer rollovers than this; then the owner decides.
    public int StaffRolloverLimit { get; private set; }

    // CS-08: from this many rollovers the entry appears in "Cần quyết định" for the owner.
    public int OwnerReviewAfterRollovers { get; private set; }

    // CS-14: on-time seasons in a row before suggesting a higher credit limit.
    public int OnTimeSeasonsForLimitRaise { get; private set; }

    // D4: optional cap on a debt's term from its delivery day (off by default, for year-round crops).
    public int? MaxCreditTermDays { get; private set; }

    // D5: limit of the total extension, counted from the ORIGINAL due date. Staff stay within it; the owner may go beyond
    // with a reason.
    public int MaxTotalExtensionDays { get; private set; }

    public long Version { get; private set; }

    public void Update(int defaultGraceDays, DebtReminderOffsets reminderOffsets, int staffRolloverLimit,
        int ownerReviewAfterRollovers, int onTimeSeasonsForLimitRaise, int? maxCreditTermDays, int maxTotalExtensionDays)
    {
        if (defaultGraceDays is < 0 or > CropSeason.MaxGraceDays)
            throw new DomainException($"Default grace days must be between 0 and {CropSeason.MaxGraceDays}.");
        ArgumentNullException.ThrowIfNull(reminderOffsets);
        if (reminderOffsets.N2 >= 0 || reminderOffsets.N4 <= 0 || reminderOffsets.N5 <= reminderOffsets.N4)
            throw new DomainException("Reminders need N2 before the due date, N4 after it and N5 after N4.");
        if (staffRolloverLimit < 0)
            throw new DomainException("The staff rollover limit cannot be negative.");
        if (ownerReviewAfterRollovers < staffRolloverLimit)
            throw new DomainException("Owner review cannot start before the staff rollover limit is used up.");
        if (onTimeSeasonsForLimitRaise < 1)
            throw new DomainException("At least one on-time season is needed before suggesting a higher limit.");
        if (maxCreditTermDays is < 1)
            throw new DomainException("The maximum credit term must be at least one day.");
        if (maxTotalExtensionDays < 1)
            throw new DomainException("The total extension limit must be at least one day.");

        DefaultGraceDays = defaultGraceDays;
        ReminderOffsets = reminderOffsets;
        StaffRolloverLimit = staffRolloverLimit;
        OwnerReviewAfterRollovers = ownerReviewAfterRollovers;
        OnTimeSeasonsForLimitRaise = onTimeSeasonsForLimitRaise;
        MaxCreditTermDays = maxCreditTermDays;
        MaxTotalExtensionDays = maxTotalExtensionDays;
    }

    public bool StaffMayRollOver(int rolloverCount) => rolloverCount < StaffRolloverLimit;

    public bool ExceedsTotalExtension(DateOnly originalDueDate, DateOnly newDueDate) =>
        newDueDate.DayNumber - originalDueDate.DayNumber > MaxTotalExtensionDays;
}
