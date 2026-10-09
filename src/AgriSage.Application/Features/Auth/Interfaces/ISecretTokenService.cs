namespace AgriSage.Application.Features.Auth.Interfaces;

public interface ISecretTokenService
{
    string GenerateToken();
    string GenerateOtp();
    string HashToken(string token);
    string HashChallenge(Guid challengeId, string token);
    bool VerifyChallenge(Guid challengeId, string token, string expectedHash);
}
