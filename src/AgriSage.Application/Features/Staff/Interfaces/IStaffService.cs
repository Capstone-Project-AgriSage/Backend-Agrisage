using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Staff.Dtos.Requests;
using AgriSage.Application.Features.Staff.Dtos.Responses;

namespace AgriSage.Application.Features.Staff.Interfaces;

// Staff accounts of the single active store. Callers are Admin and Store Owner only; see StaffPolicy.
public interface IStaffService
{
    Task<StaffResponse> CreateAsync(CreateStaffRequest request, CancellationToken cancellationToken);

    Task<PagedResult<StaffResponse>> ListAsync(StaffListRequest request, CancellationToken cancellationToken);

    Task<StaffResponse> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<StaffResponse> UpdateAsync(Guid userId, UpdateStaffRequest request, CancellationToken cancellationToken);

    Task LockAsync(Guid userId, CancellationToken cancellationToken);

    Task UnlockAsync(Guid userId, CancellationToken cancellationToken);

    Task ResetPasswordAsync(Guid userId, ResetStaffPasswordRequest request, CancellationToken cancellationToken);

    // Semantic delete: the member leaves the store and the account is locked (the user row is kept for history).
    Task RemoveAsync(Guid userId, CancellationToken cancellationToken);
}
