namespace AgriSage.Domain.Features.Debt.Enums;

public enum DebtEntryActionType
{
    Dispute,
    Keep,
    Adjust,
    Cancel,
    ChangeDueDate,
    Rollover // moves the collection season and due date (CS-07); amount unchanged
}
