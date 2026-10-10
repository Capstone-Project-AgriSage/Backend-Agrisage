using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Staff.Dtos.Requests;
using AgriSage.Application.Features.Staff.Dtos.Responses;
using AgriSage.Application.Features.Staff.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Staff;

// HTTP boundary only. Role gates here: managing staff is Admin and Store Owner (ApiRoles.Manage); the list is also open
// to Sales staff (ApiRoles.Operate) so they can pick a driver for a delivery, but StaffService gives them delivery staff only.
// Which accounts each manager may manage is decided by StaffService (a Store Owner cannot manage another Store Owner or an Admin).
[ApiController]
[Route("api/staff")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class StaffController(IStaffService staffService) : ControllerBase
{
    [Authorize(Roles = ApiRoles.Manage)]
    [HttpPost]
    [ProducesResponseType<StaffResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var response = await staffService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    // Admin and Store Owner: every staff role. Sales: delivery staff only (the role filter is forced in the service).
    [HttpGet]
    public async Task<ActionResult<PagedResult<StaffResponse>>> List(
        [FromQuery] StaffListRequest request,
        CancellationToken cancellationToken) =>
        await staffService.ListAsync(request, cancellationToken);

    [Authorize(Roles = ApiRoles.Manage)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StaffResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await staffService.GetAsync(id, cancellationToken);

    [Authorize(Roles = ApiRoles.Manage)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StaffResponse>> Update(
        Guid id,
        UpdateStaffRequest request,
        CancellationToken cancellationToken) =>
        await staffService.UpdateAsync(id, request, cancellationToken);

    [Authorize(Roles = ApiRoles.Manage)]
    [HttpPost("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        await staffService.LockAsync(id, cancellationToken);

        return NoContent();
    }

    [Authorize(Roles = ApiRoles.Manage)]
    [HttpPost("{id:guid}/unlock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await staffService.UnlockAsync(id, cancellationToken);

        return NoContent();
    }

    [Authorize(Roles = ApiRoles.Manage)]
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

    // Review right for AI diagnoses (store_members.can_review_ai). Not a role: see AI_DIAGNOSIS.md section 9.
    [Authorize(Roles = ApiRoles.Manage)]
    [HttpPut("{id:guid}/ai-review")]
    public async Task<ActionResult<StaffResponse>> SetAiReview(
        Guid id,
        AgriSage.Application.Features.Diagnosis.SetAiReviewRequest request,
        CancellationToken cancellationToken) =>
        await staffService.SetAiReviewAsync(id, request, cancellationToken);

    // Semantic delete: the member leaves the store and the account is locked; the user row is kept.
    [Authorize(Roles = ApiRoles.Manage)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await staffService.RemoveAsync(id, cancellationToken);

        return NoContent();
    }
}
