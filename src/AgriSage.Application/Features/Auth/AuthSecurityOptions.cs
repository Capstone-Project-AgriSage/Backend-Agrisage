using System.ComponentModel.DataAnnotations;

namespace AgriSage.Application.Features.Auth;

public sealed class AuthSecurityOptions
{
    [Range(1, 90)] public int SessionDays { get; init; } = 30;
    [Range(1, 30)] public int ChallengeMinutes { get; init; } = 10;
    [Range(30, 600)] public int ResendCooldownSeconds { get; init; } = 60;
}
