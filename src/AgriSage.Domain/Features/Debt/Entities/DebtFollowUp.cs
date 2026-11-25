using AgriSage.Domain.Common;
using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Debt.Enums;

namespace AgriSage.Domain.Features.Debt.Entities;

// A reminder of a customer made outside the system and logged afterwards (F3.23, team task 3): a call, a visit, the customer
// at the counter or a message, with its outcome and an optional promise to pay. Created only by DebtAccount.RecordFollowUp.
// Append-only: nothing edits it, and recording one changes nothing else (no notification, no debt, limit or credit change).
public sealed class DebtFollowUp : SoftDeletableEntity
{
    public const int MaxNoteLength = 1000;

    // A visit may be logged on a later day, at most this long after it happened.
    public static readonly TimeSpan MaxBackdating = TimeSpan.FromDays(30);

    // Tolerance for a client clock slightly ahead of the server's.
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    private DebtFollowUp()
    {
    }

    internal DebtFollowUp(
        Guid debtAccountId,
        DebtFollowUpChannel channel,
        DebtFollowUpOutcome outcome,
        string? note,
        DateTimeOffset contactedAt,
        DateOnly? promisedDate,
        decimal? promisedAmount,
        decimal outstandingAmount,
        decimal overdueAmount,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(channel)) throw new DomainException("Unknown follow-up channel.");
        if (!Enum.IsDefined(outcome)) throw new DomainException("Unknown follow-up outcome.");

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > MaxNoteLength })
            throw new DomainException($"The note must be {MaxNoteLength} characters or fewer.");
        if (note is null && (channel == DebtFollowUpChannel.Other || outcome == DebtFollowUpOutcome.Other))
            throw new DomainException("A note is required when the channel or the outcome is OTHER.");

        if (contactedAt > now + ClockTolerance) throw new DomainException("The contact time cannot be in the future.");
        if (contactedAt < now - MaxBackdating)
            throw new DomainException($"A reminder can be logged at most {MaxBackdating.Days} days after it happened.");

        if (outstandingAmount <= 0) throw new DomainException("The customer owes nothing, so there is no debt to remind.");
        if (overdueAmount < 0 || overdueAmount > outstandingAmount)
            throw new DomainException("The overdue amount must be between zero and the outstanding amount.");

        if (outcome == DebtFollowUpOutcome.Promised)
        {
            if (promisedDate is null) throw new DomainException("A promise to pay needs the promised date.");
            if (promisedAmount is { } amount && (amount <= 0 || amount > outstandingAmount || decimal.Round(amount, 2) != amount))
                throw new DomainException("The promised amount must be positive, have at most 2 decimals and not exceed what is owed.");
        }
        else if (promisedDate is not null || promisedAmount is not null)
        {
            throw new DomainException("A promised date or amount is only recorded with the outcome PROMISED.");
        }

        DebtAccountId = Guard.NotEmpty(debtAccountId);
        Channel = channel;
        Outcome = outcome;
        Note = note;
        ContactedAt = contactedAt;
        PromisedDate = promisedDate;
        PromisedAmount = promisedAmount;
        OutstandingAmount = outstandingAmount;
        OverdueAmount = overdueAmount;
        CreatedBy = Guard.NotEmpty(createdBy);
    }

    public Guid DebtAccountId { get; private set; }

    public DebtFollowUpChannel Channel { get; private set; }

    public DebtFollowUpOutcome Outcome { get; private set; }

    public string? Note { get; private set; }

    // When the contact happened (may be before the record was made).
    public DateTimeOffset ContactedAt { get; private set; }

    public DateOnly? PromisedDate { get; private set; }

    public decimal? PromisedAmount { get; private set; }

    // The customer's balance and overdue amount when the record was made, so the history reads in context.
    public decimal OutstandingAmount { get; private set; }

    public decimal OverdueAmount { get; private set; }

    public Guid CreatedBy { get; private set; }

    // Append-only log: a wrong record is corrected by a new one, never deleted.
    protected override void EnsureCanBeDeleted() =>
        throw new DomainException("Debt follow-ups cannot be deleted; record a new one to correct it.");
}
