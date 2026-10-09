using System.Text;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgriSage.Infrastructure.Authentication;

// HS256 JWT access token: sub = users.id, role = role code, jti, iat. Lifetime = Jwt:AccessTokenMinutes.
public sealed class AccessTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock) : IAccessTokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    // Kept for signature/authorization tests; a token with an unpersisted sid fails the live session check.
    public AccessToken Issue(Guid userId, string roleCode) => Issue(userId, roleCode, Guid.NewGuid(), 0);

    public AccessToken Issue(Guid userId, string roleCode, Guid sessionId, long securityVersion)
    {
        var jwt = options.Value;
        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(jwt.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [AgriSageClaimTypes.Subject] = userId.ToString(),
                [AgriSageClaimTypes.Role] = roleCode,
                ["sid"] = sessionId.ToString(),
                ["sv"] = securityVersion,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
