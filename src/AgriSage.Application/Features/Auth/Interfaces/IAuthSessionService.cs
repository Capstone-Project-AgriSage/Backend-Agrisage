using AgriSage.Application.Features.Auth.Dtos.Responses;

namespace AgriSage.Application.Features.Auth.Interfaces;

public interface IAuthSessionService
{
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken token);
    Task<IReadOnlyList<AuthSessionResponse>> ListAsync(CancellationToken token);
    Task LogoutAsync(CancellationToken token);
    Task LogoutAllAsync(CancellationToken token);
    Task RevokeAsync(Guid sessionId, CancellationToken token);
}
