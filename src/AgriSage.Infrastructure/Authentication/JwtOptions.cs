using System.ComponentModel.DataAnnotations;

namespace AgriSage.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public const int MinSigningKeyLength = 32;

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    // Secret: supply via User Secrets (Development) or environment variable Jwt__SigningKey. Never commit it.
    [Required]
    [MinLength(MinSigningKeyLength)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 60;
}
