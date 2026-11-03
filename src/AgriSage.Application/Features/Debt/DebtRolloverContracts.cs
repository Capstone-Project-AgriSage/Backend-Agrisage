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

// F3.15: roll a season's debt over to a later season. Each entry follows the F3.14 rules; an entry that does not qualify is
// skipped with a reason, the rest moves in one transaction. Without EntryIds: every open entry collected in the season. At
// most 500 entries per call. DryRun reports the same result and writes nothing.
public sealed record CropSeasonRolloverRequest(
    string ReasonCode,
    string Note,
    Guid? TargetCropSeasonId = null,
    IReadOnlyList<Guid>? EntryIds = null,
    bool DryRun = false,
    bool OverrideExtensionLimit = false);

public sealed record CropSeasonRolledOverEntry(Guid EntryId, string EntryNumber, Guid FarmerProfileId, DateOnly OldDueDate,
    DateOnly NewDueDate, decimal OutstandingAmount);

// Reason: NOT_FOUND (not a debt entry of this store), NOT_IN_SEASON (not collected in the source season), NOTHING_OWED,
// DISPUTED, NOT_LATER (its due date is not before the target season's), NEEDS_OWNER (Sale staff past the store's rollover
// limit), EXCEEDS_TOTAL_EXTENSION (past the total extension limit without the owner's override).
public sealed record CropSeasonSkippedEntry(Guid EntryId, string? EntryNumber, string Reason);

public sealed record CropSeasonRolloverResponse(Guid SourceSeasonId, DebtSeasonReference TargetSeason, DateOnly NewDueDate,
    bool DryRun, IReadOnlyList<CropSeasonRolledOverEntry> RolledOver, IReadOnlyList<CropSeasonSkippedEntry> Skipped);

public interface IDebtRolloverService
{
    Task<DebtEntryResponse> RolloverAsync(Guid entryId, DebtRolloverRequest request, CancellationToken token);
    Task<CropSeasonRolloverResponse> RolloverSeasonAsync(Guid seasonId, CropSeasonRolloverRequest request, CancellationToken token);
}

public sealed class CropSeasonRolloverRequestValidator : AbstractValidator<CropSeasonRolloverRequest>
{
    public const int MaxEntries = 500;

    public CropSeasonRolloverRequestValidator()
    {
        RuleFor(r => r.ReasonCode).NotEmpty()
            .Must(text => EnumText.TryParse<DebtReasonCode>(text, out var code) && DebtReasonCodes.AllowsRollover(code))
            .WithMessage(DebtRolloverRequestValidator.NotForRollover);
        RuleFor(r => r.Note).NotEmpty().Must(n => !string.IsNullOrWhiteSpace(n)).MaximumLength(1000);
        RuleFor(r => r.TargetCropSeasonId).NotEqual(Guid.Empty).When(r => r.TargetCropSeasonId.HasValue);
        RuleFor(r => r.EntryIds!).NotEmpty().Must(ids => ids.Count <= MaxEntries)
            .WithMessage($"At most {MaxEntries} entries per call.").When(r => r.EntryIds is not null);
        RuleForEach(r => r.EntryIds).NotEqual(Guid.Empty).When(r => r.EntryIds is not null);
    }
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
