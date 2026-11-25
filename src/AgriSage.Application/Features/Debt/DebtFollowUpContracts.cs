using AgriSage.Application.Common;
using AgriSage.Application.Common.Models;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Domain.Features.Debt.Enums;
using FluentValidation;

namespace AgriSage.Application.Features.Debt;

// F3.23 (FLOW_3 §8j): reminders made outside the system and logged afterwards. Internal to the store: no Farmer route.
public sealed record DebtFollowUpRequest(
    string Channel,
    string Outcome,
    string? Note = null,
    DateTimeOffset? ContactedAt = null,
    DateOnly? PromisedDate = null,
    decimal? PromisedAmount = null);

public sealed record DebtFollowUpListRequest : PaginationRequest;

public sealed record DebtFollowUpActor(Guid Id, string? Name, string? Role);

public sealed record DebtFollowUpResponse(
    Guid Id,
    DateTimeOffset ContactedAt,
    string Channel,
    string Outcome,
    string? Note,
    DateOnly? PromisedDate,
    decimal? PromisedAmount,
    string? PromiseStatus,
    decimal PaidSinceContact,
    decimal OutstandingAmount,
    decimal OverdueAmount,
    DebtFollowUpActor CreatedBy,
    DateTimeOffset CreatedAt,
    // A later reminder of the same customer exists: this record (and its promise) is no longer the latest word.
    bool HasLaterContact);

// The customer's latest record, on the staff debt entry lists ("Cần quyết định").
public sealed record DebtFollowUpSummary(
    DateTimeOffset ContactedAt,
    string Channel,
    string Outcome,
    DateOnly? PromisedDate,
    string? PromiseStatus,
    string? ByName);

// Owner dashboard: customers with undisputed overdue debt, those of them not reminded within WindowDays, and customers whose
// latest reminder is a promise that has passed unpaid (nobody has followed it up since).
public sealed record DebtFollowUpDashboard(int WindowDays, int OverdueCustomerCount, int NotContactedCount, int BrokenPromiseCount);

public interface IDebtFollowUpService
{
    Task<DebtFollowUpResponse> SendSmsAsync(Guid farmerProfileId, CancellationToken token);
    Task<DebtFollowUpResponse> RecordAsync(Guid farmerProfileId, DebtFollowUpRequest request, CancellationToken token);

    Task<PagedResult<DebtFollowUpResponse>> ListAsync(Guid farmerProfileId, DebtFollowUpListRequest request, CancellationToken token);
}

public sealed class DebtFollowUpRequestValidator : AbstractValidator<DebtFollowUpRequest>
{
    public DebtFollowUpRequestValidator()
    {
        RuleFor(r => r.Channel).NotEmpty().Must(v => EnumText.TryParse<DebtFollowUpChannel>(v, out _))
            .WithMessage("channel must be PHONE_CALL, VISIT, AT_STORE, MESSAGE or OTHER.");
        RuleFor(r => r.Outcome).NotEmpty().Must(v => EnumText.TryParse<DebtFollowUpOutcome>(v, out _))
            .WithMessage("outcome must be PROMISED, ASKED_MORE_TIME, NO_ANSWER, REFUSED or OTHER.");
        RuleFor(r => r.Note).MaximumLength(DebtFollowUp.MaxNoteLength);
        RuleFor(r => r.Note).NotEmpty().When(r => IsOther(r.Channel) || IsOther(r.Outcome))
            .WithMessage("A note is required when the channel or the outcome is OTHER.");
        RuleFor(r => r.PromisedDate).NotNull().When(r => IsPromised(r.Outcome))
            .WithMessage("A promise to pay needs the promised date.");
        RuleFor(r => r.PromisedDate).Null().When(r => !IsPromised(r.Outcome))
            .WithMessage("A promised date is only recorded with the outcome PROMISED.");
        RuleFor(r => r.PromisedAmount).Null().When(r => !IsPromised(r.Outcome))
            .WithMessage("A promised amount is only recorded with the outcome PROMISED.");
        RuleFor(r => r.PromisedAmount).Must(a => a > 0 && Credit.CreditMoney.Valid(a!.Value)).When(r => r.PromisedAmount is not null)
            .WithMessage("The promised amount must be positive with at most 2 decimals.");
    }

    private static bool IsPromised(string? outcome) =>
        EnumText.TryParse<DebtFollowUpOutcome>(outcome, out var value) && value == DebtFollowUpOutcome.Promised;

    private static bool IsOther(string? value) => string.Equals(value?.Trim(), "OTHER", StringComparison.OrdinalIgnoreCase);
}

public sealed class DebtFollowUpListRequestValidator : AbstractValidator<DebtFollowUpListRequest>
{
    public DebtFollowUpListRequestValidator() => Include(new Common.Validators.PaginationRequestValidator());
}
