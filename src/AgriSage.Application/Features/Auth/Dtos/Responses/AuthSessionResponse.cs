namespace AgriSage.Application.Features.Auth.Dtos.Responses;

public sealed record AuthSessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt, bool IsCurrent);
