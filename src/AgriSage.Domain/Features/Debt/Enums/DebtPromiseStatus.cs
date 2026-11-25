namespace AgriSage.Domain.Features.Debt.Enums;

// Whether a promise to pay was kept (F3.23). Derived when read, never stored; it only labels the record.
public enum DebtPromiseStatus
{
    Waiting,  // the promised date has not passed
    Kept,     // paid the promised amount since the contact, or owes nothing any more
    Broken    // the promised date passed without it ("Quá hẹn")
}
