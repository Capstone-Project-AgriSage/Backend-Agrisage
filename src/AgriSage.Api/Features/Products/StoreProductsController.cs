using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Products;

// Products offered by the store. Read: all staff roles; write: Admin and Store Owner.
[ApiController]
[Route("api/store-products")]
[Authorize(Roles = ApiRoles.Read)]
public sealed class StoreProductsController(IStoreProductService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<StoreProductResponse>>> List(
        [FromQuery] StoreProductListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StoreProductResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType<StoreProductResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateStoreProductRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<StoreProductResponse>> Update(
        Guid id, UpdateStoreProductRequest request, CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/mark-sellable")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> MarkSellable(Guid id, CancellationToken cancellationToken)
    {
        await service.SetSellableAsync(id, true, cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/mark-not-sellable")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> MarkNotSellable(Guid id, CancellationToken cancellationToken)
    {
        await service.SetSellableAsync(id, false, cancellationToken);

        return NoContent();
    }

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

    // Semantic delete: deactivates the store product (the row is kept).
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.SetActiveAsync(id, false, cancellationToken);

        return NoContent();
    }
}
