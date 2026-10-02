namespace AgriSage.Application.Common;

// The store works in Vietnam time (UTC+7, no daylight saving). "Today", document numbers and date filters use it,
// while stored timestamps stay in UTC.
public static class BusinessCalendar
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateOnly Today(DateTimeOffset utcNow) => DateOnly.FromDateTime(utcNow.ToOffset(Offset).DateTime);

    // Start of a Vietnam calendar day, as a UTC instant (PostgreSQL timestamptz accepts only offset 0).
    public static DateTimeOffset StartOfDay(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Offset).ToUniversalTime();
}
