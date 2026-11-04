namespace AgriSage.Application.Features.Notifications;

// F3.18/F3.20: the deduplication keys of the debt reminders, built (and read) in one place so the writer of a reminder and the
// code that counts or recovers them cannot drift apart. A Farmer reminder has one key per (Farmer, season) for N1 and per
// (Farmer, due date) for N2..N5; a summary for the staff has one per (store, day, milestone) — the store is part of the key, so two
// stores never share or suppress each other's summaries.
internal static class DebtReminderKeys
{
    public const string FarmerPrefix = "debt-reminder:";

    public static string Season(Guid farmerProfileId, Guid cropSeasonId) => $"{FarmerPrefix}{farmerProfileId}:{cropSeasonId}:N1";

    public static string Group(Guid farmerProfileId, DateOnly dueDate, string milestone) =>
        $"{FarmerPrefix}{farmerProfileId}:{dueDate:yyyyMMdd}:{milestone}";

    public static string SummaryPrefix(Guid storeId) => $"debt-reminder-staff:{storeId:N}:";

    public static string Summary(Guid storeId, DateOnly day, string milestone) => $"{SummaryPrefix(storeId)}{day:yyyyMMdd}:{milestone}";

    // The milestone code ("N3") at the end of a key.
    public static string MilestoneOf(string key) => key[^2..];
}
