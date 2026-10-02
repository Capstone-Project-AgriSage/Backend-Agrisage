using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Staff.Dtos.Requests;
using AgriSage.Application.Features.Staff.Dtos.Responses;
using AgriSage.Application.Features.Staff.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Staff;

// HTTP boundary only. Role gate here (Admin, Store Owner); which accounts each of them may manage is decided
// by StaffService (a Store Owner cannot manage another Store Owner or an Admin).
[ApiController]
[Route("api/staff")]
[Authorize(Roles = "ADMIN,STORE_OWNER")]
public sealed class StaffController(IStaffService staffService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var response = await staffService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<StaffResponse>>> List(
        [FromQuery] StaffListRequest request,
        CancellationToken cancellationToken) =>
        await staffService.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StaffResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await staffService.GetAsync(id, cancellationToken);

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StaffResponse>> Update(
        Guid id,
        UpdateStaffRequest request,
        CancellationToken cancellationToken) =>
        await staffService.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        await staffService.LockAsync(id, cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/unlock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await staffService.UnlockAsync(id, cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        ResetStaffPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await staffService.ResetPasswordAsync(id, request, cancellationToken);

        return NoContent();
    }

    // Semantic delete: the member leaves the store and the account is locked; the user row is kept.
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await staffService.RemoveAsync(id, cancellationToken);

        return NoContent();
    }
}
