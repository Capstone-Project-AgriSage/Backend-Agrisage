using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Dtos.Responses;

namespace AgriSage.Application.Features.Auth.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterFarmerAsync(RegisterFarmerRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken);

    // Changes the caller's own password; the current password must be supplied.
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken);
}
