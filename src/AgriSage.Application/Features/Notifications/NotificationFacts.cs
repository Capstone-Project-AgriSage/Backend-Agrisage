namespace AgriSage.Application.Features.Notifications;

// The figures a business notification names (money, dates, document numbers, the Farmer's name). The dispatcher reads
// them from the audit snapshot of the event and from rows that do not change afterwards (numbers, names, payment amount),
// so the text says what happened at that moment. A missing figure falls back to the generic text.
public sealed record NotificationFacts(
    string? EntryNumber = null,
    string? OrderNumber = null,
    decimal? Amount = null,
    DateOnly? DueDate = null,
    decimal? Outstanding = null,
    string? FarmerName = null,
    decimal? CreditLimit = null,
    decimal? PreviousCreditLimit = null,
    string? Reason = null);
