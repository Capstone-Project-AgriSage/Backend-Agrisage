using System.Security.Cryptography;
using System.Text;
using AgriSage.Application.Features.Auth.Interfaces;
using Microsoft.Extensions.Options;

namespace AgriSage.Infrastructure.Authentication;

public sealed class SecretTokenService(IOptions<JwtOptions> jwt) : ISecretTokenService
{
    public string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public string GenerateOtp() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public string HashChallenge(Guid challengeId, string token) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(jwt.Value.SigningKey), Encoding.UTF8.GetBytes($"agrisage:challenge:{challengeId:N}:{token}")));
    public bool VerifyChallenge(Guid challengeId, string token, string expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashChallenge(challengeId, token)),
            Encoding.ASCII.GetBytes(expectedHash));
}
