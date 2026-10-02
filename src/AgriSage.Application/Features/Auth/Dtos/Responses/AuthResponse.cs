namespace AgriSage.Application.Features.Auth.Dtos.Responses;

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, AuthUserResponse User);
