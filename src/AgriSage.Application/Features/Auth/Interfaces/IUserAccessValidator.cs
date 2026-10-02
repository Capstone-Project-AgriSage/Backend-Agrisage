namespace AgriSage.Application.Features.Auth.Interfaces;

// Checked on every authenticated request so that locking an account takes effect immediately
// instead of when the (up to 60 minute) access token expires.
public interface IUserAccessValidator
{
    Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken);
}
