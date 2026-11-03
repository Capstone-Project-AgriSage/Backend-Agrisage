using AgriSage.Application.Common;
using AgriSage.Domain.Features.Debt.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Debt;

// F3.14: extend one debt entry to a later season (CS-07, CS-08, D5, D9). ReasonCode is a hardship code (MAT_MUA, THIEN_TAI,
// KHO_KHAN, KHAC) and Note is always required (CS-11). Without TargetCropSeasonId the entry goes to the next open season
// whose due date is later than its own. OverrideExtensionLimit: the owner confirms going past the store's total extension
// limit, counted from the original due date; staff never may.
public sealed record DebtRolloverRequest(
    string ReasonCode,
    string Note,
    Guid? TargetCropSeasonId = null,
    bool OverrideExtensionLimit = false);

public interface IDebtRolloverService
{
    Task<DebtEntryResponse> RolloverAsync(Guid entryId, DebtRolloverRequest request, CancellationToken token);
}

public sealed class DebtRolloverRequestValidator : AbstractValidator<DebtRolloverRequest>
{
    public const string NotForRollover = "A rollover needs a hardship reason code: MAT_MUA, THIEN_TAI, KHO_KHAN or KHAC.";

    public DebtRolloverRequestValidator()
    {
        RuleFor(r => r.ReasonCode).NotEmpty()
            .Must(text => EnumText.TryParse<DebtReasonCode>(text, out var code) && DebtReasonCodes.AllowsRollover(code))
            .WithMessage(NotForRollover);
        RuleFor(r => r.Note).NotEmpty().Must(n => !string.IsNullOrWhiteSpace(n)).MaximumLength(1000);
        RuleFor(r => r.TargetCropSeasonId).NotEqual(Guid.Empty).When(r => r.TargetCropSeasonId.HasValue);
    }
}
