using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Application.Features.Auth.Interfaces;

public interface IAuthChallengeService
{
    Task RequestResetAsync(string identifier, CancellationToken token);
    Task ResetPasswordAsync(string tokenValue, string newPassword, CancellationToken token);
    // SMS reset: the phone number and the 6-digit code the SMS carried.
    Task ResetPasswordWithCodeAsync(string identifier, string code, string newPassword, CancellationToken token);
    // Checks the SMS code without consuming it (a wrong code still uses one of its attempts); the reset that follows
    // sends the code again and is the call that decides.
    Task VerifyResetCodeAsync(string identifier, string code, CancellationToken token);
    Task RequestVerificationAsync(AuthDeliveryChannel channel, CancellationToken token);
    Task ConfirmEmailAsync(string tokenValue, CancellationToken token);
    Task ConfirmPhoneAsync(string code, CancellationToken token);
}
