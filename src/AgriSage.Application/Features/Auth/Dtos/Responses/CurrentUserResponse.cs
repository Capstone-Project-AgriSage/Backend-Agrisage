namespace AgriSage.Application.Features.Auth.Dtos.Responses;

public sealed record CurrentUserResponse(
    Guid Id,
    string FullName,
    string? PhoneNumber,
    string? Email,
    string Role,
    string Status,
    bool PhoneVerified,
    bool EmailVerified);
