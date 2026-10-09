using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.Application.Features.Auth.Interfaces;

public sealed record AuthMessage(AuthDeliveryChannel Channel, string Destination,
    AuthChallengePurpose Purpose, string Token, DateTimeOffset ExpiresAt);

public interface IAuthMessageSender
{
    bool IsConfigured(AuthDeliveryChannel channel);
    Task SendAsync(AuthMessage message, CancellationToken cancellationToken);
}
