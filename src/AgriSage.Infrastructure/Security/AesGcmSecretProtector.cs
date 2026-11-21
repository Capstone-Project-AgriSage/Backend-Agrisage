using System.Security.Cryptography;
using System.Text;
using AgriSage.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace AgriSage.Infrastructure.Security;

// Encrypts secrets kept in the database (the AI provider's API key) with AES-256-GCM. The master key comes from configuration
// (SecretEncryption:Key = 32 random bytes in base64, in User Secrets or an environment variable) and is never stored in the
// database, so a copy of the database alone does not reveal the secret. Stored form: "v1:" + base64(nonce | ciphertext | tag).
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string ConfigurationKey = "SecretEncryption:Key";
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[]? key;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        var configured = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(configured.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{ConfigurationKey} must be 32 bytes in base64.");
        }

        if (bytes.Length != 32)
        {
            throw new InvalidOperationException($"{ConfigurationKey} must be 32 bytes in base64 (it has {bytes.Length}).");
        }

        key = bytes;
    }

    public bool IsConfigured => key is not null;

    public string Protect(string plaintext)
    {
        var master = key ?? throw new InvalidOperationException($"{ConfigurationKey} is not configured.");
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(master, TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        return Prefix + Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    public string Unprotect(string protectedValue)
    {
        var master = key ?? throw new InvalidOperationException($"{ConfigurationKey} is not configured.");
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unknown secret format.");
        }

        var data = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        if (data.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("The secret is too short.");
        }

        var nonce = data.AsSpan(0, NonceSize);
        var cipher = data.AsSpan(NonceSize, data.Length - NonceSize - TagSize);
        var tag = data.AsSpan(data.Length - TagSize);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(master, TagSize))
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }

        return Encoding.UTF8.GetString(plain);
    }
}
