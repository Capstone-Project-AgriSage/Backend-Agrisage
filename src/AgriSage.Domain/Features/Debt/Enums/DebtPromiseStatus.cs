namespace AgriSage.Domain.Features.Debt.Enums;

// Whether a promise to pay was kept (F3.23). Derived when read, never stored; it only labels the record.
public enum DebtPromiseStatus
{
    Waiting,  // the promised date has not passed
    Kept,     // the promised amount was paid by the end of the promised day ("Đã trả theo hẹn")
    Late,     // paid in full, but after the promised day ("Trả trễ hẹn")
    Cleared,  // nothing is owed any more without that much being paid: a return, a reduction or a write-off ("Hết nợ")
    Broken    // the promised date passed without it and something is still owed ("Quá hẹn")
}
