namespace AgriSage.Application.Features.Customers;

// FLOW_2 §3.1: a Farmer's own profile and addresses (/api/me/...). The Farmer is the signed-in user, never a client value.

// DeliveryAddressRequest (README §2) + addressType + isDefault.
public sealed record AddressRequest(
    string RecipientName,
    string RecipientPhone,
    string AddressLine,
    string Province,
    string AddressType,
    string? Ward = null,
    string? District = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    bool IsDefault = false);

public sealed record AddressResponse(
    Guid Id,
    string RecipientName,
    string RecipientPhone,
    string AddressLine,
    string? Ward,
    string? District,
    string Province,
    decimal? Latitude,
    decimal? Longitude,
    string AddressType,
    bool IsDefault,
    DateTimeOffset CreatedAt);

// Phone and email are Auth concerns; farmer_profiles.notes is a staff note the Farmer never sees or edits.
public sealed record UpdateMyProfileRequest(string FullName, DateOnly? DateOfBirth = null, string? Gender = null);

public sealed record FarmerProfileResponse(
    Guid FarmerProfileId,
    Guid UserId,
    string FullName,
    string? PhoneNumber,
    string? Email,
    DateOnly? DateOfBirth,
    string? Gender,
    CustomerReference? CustomerGroup,
    DateTimeOffset CreatedAt);

// PostgreSQL partial uniqueness is immediate: pre-clear the old default before EF saves the new one (same pattern as
// ICustomerGroupDefaultSwitcher). Runs inside the caller's transaction; never saves or commits.
public interface IUserAddressDefaultSwitcher
{
    Task ClearPreviousAsync(Guid userId, Guid addressId, CancellationToken cancellationToken);
}

public interface IMyProfileService
{
    Task<FarmerProfileResponse> GetProfileAsync(CancellationToken cancellationToken);

    Task<FarmerProfileResponse> UpdateProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AddressResponse>> ListAddressesAsync(CancellationToken cancellationToken);

    Task<AddressResponse> CreateAddressAsync(AddressRequest request, CancellationToken cancellationToken);

    Task<AddressResponse> UpdateAddressAsync(Guid id, AddressRequest request, CancellationToken cancellationToken);

    Task DeleteAddressAsync(Guid id, CancellationToken cancellationToken);

    Task<AddressResponse> SetDefaultAddressAsync(Guid id, CancellationToken cancellationToken);
}
