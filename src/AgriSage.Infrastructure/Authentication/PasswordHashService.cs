using AgriSage.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace AgriSage.Infrastructure.Authentication;

// ASP.NET Core's PBKDF2 password hasher (salted, versioned format, upgradeable work factor).
// The hasher does not use the user object, so a plain string stands in for it.
public sealed class PasswordHashService : IPasswordHashService
{
    private static readonly PasswordHasher<string> Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword(string.Empty, Guid.NewGuid().ToString("N"));

    public string Hash(string password) => Hasher.HashPassword(string.Empty, password);

    public PasswordVerification Verify(string? passwordHash, string password)
    {
        if (passwordHash is null)
        {
            Hasher.VerifyHashedPassword(string.Empty, DummyHash, password);
            return PasswordVerification.Failed;
        }

        return Hasher.VerifyHashedPassword(string.Empty, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed
        };
    }
}
