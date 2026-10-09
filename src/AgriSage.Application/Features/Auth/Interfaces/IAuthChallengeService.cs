using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Application.Features.Auth.Interfaces;

public interface IAuthChallengeService
{
    Task RequestResetAsync(string identifier, CancellationToken token);
    Task ResetPasswordAsync(string tokenValue, string newPassword, CancellationToken token);
    Task RequestVerificationAsync(AuthDeliveryChannel channel, CancellationToken token);
    Task ConfirmEmailAsync(string tokenValue, CancellationToken token);
    Task ConfirmPhoneAsync(string code, CancellationToken token);
}
