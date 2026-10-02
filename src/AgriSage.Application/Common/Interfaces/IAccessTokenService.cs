namespace AgriSage.Application.Common.Interfaces;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

// Issues the JWT access token (claims: sub = users.id, role = role code). JWT details live in Infrastructure.
public interface IAccessTokenService
{
    AccessToken Issue(Guid userId, string roleCode);
}
