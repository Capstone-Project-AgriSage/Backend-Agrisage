namespace AgriSage.Domain.Features.Debt.Enums;

// What came of a manual reminder (F3.23). Stored as UPPER_SNAKE text (PROMISED, ...).
public enum DebtFollowUpOutcome
{
    Promised,       // promised to pay by a date (promised_date required)
    AskedMoreTime,  // asked for more time, hardship
    NoAnswer,       // could not be reached
    Refused,        // will not pay or disputes the amount
    Other
}
