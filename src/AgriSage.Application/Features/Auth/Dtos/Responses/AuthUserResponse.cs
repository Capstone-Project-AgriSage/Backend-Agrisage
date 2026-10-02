namespace AgriSage.Application.Features.Auth.Dtos.Responses;

public sealed record AuthUserResponse(Guid Id, string FullName, string? PhoneNumber, string? Email, string Role);
