using AgriSage.Domain.Features.Debt.Entities;
using FluentValidation;

namespace AgriSage.Application.Features.Debt;

// F3.12: the store debt policy (CS-05, CS-08, CS-09, CS-14, D4, D5). Reminder offsets are days from the due date: N2 before
// it (negative), N4 and N5 after it.
public sealed record DebtReminderOffsetsDto(int N2, int N4, int N5);

// IsDefault: the store has not saved a policy yet; the values are the documented defaults and Version is 0.
public sealed record DebtPolicyResponse(
    int DefaultGraceDays,
    DebtReminderOffsetsDto ReminderOffsets,
    int StaffRolloverLimit,
    int OwnerReviewAfterRollovers,
    int OnTimeSeasonsForLimitRaise,
    int? MaxCreditTermDays,
    int MaxTotalExtensionDays,
    long Version,
    bool IsDefault,
    DateOnly? RemindersStartDate = null);

// Replaces the whole policy (owner only). Version is the one last read (0 while the store has no policy).
public sealed record UpdateDebtPolicyRequest(
    int DefaultGraceDays,
    DebtReminderOffsetsDto ReminderOffsets,
    int StaffRolloverLimit,
    int OwnerReviewAfterRollovers,
    int OnTimeSeasonsForLimitRaise,
    int? MaxCreditTermDays,
    int MaxTotalExtensionDays,
    long Version,
    string? Reason = null);

public interface IDebtPolicyService
{
    Task<DebtPolicyResponse> GetAsync(CancellationToken token);
    Task<DebtPolicyResponse> UpdateAsync(UpdateDebtPolicyRequest request, CancellationToken token);
}

// The same rules as StoreDebtPolicy.Update, checked early for a 400 with the field name.
public sealed class UpdateDebtPolicyRequestValidator : AbstractValidator<UpdateDebtPolicyRequest>
{
    public UpdateDebtPolicyRequestValidator()
    {
        RuleFor(r => r.DefaultGraceDays).InclusiveBetween(0, CropSeason.MaxGraceDays);
        RuleFor(r => r.ReminderOffsets).NotNull();
        When(r => r.ReminderOffsets is not null, () =>
        {
            RuleFor(r => r.ReminderOffsets.N2).LessThan(0).OverridePropertyName("reminderOffsets.n2");
            RuleFor(r => r.ReminderOffsets.N4).GreaterThan(0).OverridePropertyName("reminderOffsets.n4");
            RuleFor(r => r.ReminderOffsets.N5).GreaterThan(r => r.ReminderOffsets.N4).OverridePropertyName("reminderOffsets.n5");
        });
        RuleFor(r => r.StaffRolloverLimit).GreaterThanOrEqualTo(0);
        RuleFor(r => r.OwnerReviewAfterRollovers).GreaterThanOrEqualTo(r => r.StaffRolloverLimit).GreaterThanOrEqualTo(1);
        RuleFor(r => r.OnTimeSeasonsForLimitRaise).GreaterThanOrEqualTo(1);
        RuleFor(r => r.MaxCreditTermDays).GreaterThanOrEqualTo(1).When(r => r.MaxCreditTermDays.HasValue);
        RuleFor(r => r.MaxTotalExtensionDays).GreaterThanOrEqualTo(1);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(0);
        RuleFor(r => r.Reason).MaximumLength(1000);
    }
}
