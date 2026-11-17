using FluentValidation;

namespace AgriSage.Application.Features.Debt;

// F3.20: starting debt reminders for the store. DryRun only counts; it writes nothing and sends nothing.
public sealed record DebtReminderStartRequest(bool DryRun = true, string? Reason = null);

// Debts already past the milestones before T0 are NOT reminded late: the milestone that would have been sent as a backlog
// is counted in Skipped (by code N1..N5). They stay on the debt screens and reports; only automatic reminding is skipped.
public sealed record DebtReminderStartResponse(
    bool DryRun,
    bool AlreadyStarted,
    DateOnly StartDate,
    int Farmers,
    int ActiveGroups,
    IReadOnlyDictionary<string, int> Skipped,
    int SkippedTotal,
    int GroupsWithLaterMilestones);

public interface IDebtReminderStartService
{
    Task<DebtReminderStartResponse> StartAsync(DebtReminderStartRequest request, CancellationToken token);
}

public sealed class DebtReminderStartRequestValidator : AbstractValidator<DebtReminderStartRequest>
{
    public DebtReminderStartRequestValidator()
    {
        RuleFor(r => r.Reason).MaximumLength(1000);
    }
}
