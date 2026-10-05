using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Customers;

// A Farmer's own profile and addresses (FLOW_2 §3.1): the Farmer is the signed-in user; other people's addresses are "not found".
[ApiController]
[Route("api/me")]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeProfileController(IMyProfileService service) : ControllerBase
{
    [HttpGet("profile")]
    public Task<FarmerProfileResponse> GetProfile(CancellationToken token) => service.GetProfileAsync(token);

    [HttpPut("profile")]
    public Task<FarmerProfileResponse> UpdateProfile(UpdateMyProfileRequest request, CancellationToken token) =>
        service.UpdateProfileAsync(request, token);

    [HttpGet("addresses")]
    public Task<IReadOnlyList<AddressResponse>> ListAddresses(CancellationToken token) => service.ListAddressesAsync(token);

    [HttpPost("addresses")]
    public async Task<IActionResult> CreateAddress(AddressRequest request, CancellationToken token)
    {
        var response = await service.CreateAddressAsync(request, token);
        return Created($"/api/me/addresses/{response.Id}", response);
    }

    [HttpPut("addresses/{id:guid}")]
    public Task<AddressResponse> UpdateAddress(Guid id, AddressRequest request, CancellationToken token) =>
        service.UpdateAddressAsync(id, request, token);

    [HttpDelete("addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken token)
    {
        await service.DeleteAddressAsync(id, token);
        return NoContent();
    }

    [HttpPost("addresses/{id:guid}/set-default")]
    public Task<AddressResponse> SetDefaultAddress(Guid id, CancellationToken token) => service.SetDefaultAddressAsync(id, token);
}
