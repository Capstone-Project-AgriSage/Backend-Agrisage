namespace AgriSage.Domain.Features.Debt.Enums;

// How the store reminded the customer outside the system (F3.23). Stored as UPPER_SNAKE text (PHONE_CALL, ...).
public enum DebtFollowUpChannel
{
    PhoneCall,
    Visit,      // at home or in the field
    AtStore,    // the customer came to the counter
    Message,    // SMS or Zalo sent by hand
    Other
}
