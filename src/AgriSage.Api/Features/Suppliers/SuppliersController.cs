using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Suppliers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Suppliers;

// Read: Admin, Store Owner, Sales. Write: Admin and Store Owner.
[ApiController]
[Route("api/suppliers")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class SuppliersController(ISupplierService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierResponse>>> List(
        [FromQuery] SupplierListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType<SupplierResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SupplierRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<SupplierResponse>> Update(Guid id, SupplierRequest request, CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        await service.SetActiveAsync(id, true, cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await service.SetActiveAsync(id, false, cancellationToken);

        return NoContent();
    }

    // Soft delete; refused once a goods receipt used the supplier.
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
