namespace AgriSage.Application.Common.Interfaces;

// The "phone" of the simulated SMS provider (MessageDelivery:SmsProvider = Simulated), for the test environment only: the
// messages the API would have sent as real SMS, newest first. It is registered in that mode alone, so code that asks for
// it gets null everywhere else.
public interface ISimulatedSmsOutbox
{
    IReadOnlyList<SimulatedSmsMessage> Recent();
}

// Code = the 6-digit code the message carries; Purpose = the AuthChallengePurpose name (PasswordReset, PhoneVerification).
public sealed record SimulatedSmsMessage(DateTimeOffset At, string Destination, string Purpose, string Code, string Text);
