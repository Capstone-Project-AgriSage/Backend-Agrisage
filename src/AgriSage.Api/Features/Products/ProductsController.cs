using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Products;

// Staff-facing products with their packagings and ingredients. Read: all staff roles; write: Admin and Store Owner.
[ApiController]
[Route("api/products")]
[Authorize(Roles = ApiRoles.Read)]
public sealed class ProductsController(IProductService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductListItem>>> List(
        [FromQuery] ProductListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id, UpdateProductRequest request, CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ProductResponse>> ChangeStatus(
        Guid id, ChangeProductStatusRequest request, CancellationToken cancellationToken) =>
        await service.ChangeStatusAsync(id, request, cancellationToken);

    // Semantic delete: the product becomes DISCONTINUED and leaves the store sale list.
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/packagings")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ProductResponse>> AddPackaging(
        Guid id, PackagingRequest request, CancellationToken cancellationToken) =>
        await service.AddPackagingAsync(id, request, cancellationToken);

    [HttpPut("{id:guid}/packagings/{packagingId:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ProductResponse>> UpdatePackaging(
        Guid id, Guid packagingId, UpdatePackagingRequest request, CancellationToken cancellationToken) =>
        await service.UpdatePackagingAsync(id, packagingId, request, cancellationToken);

    [HttpDelete("{id:guid}/packagings/{packagingId:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ProductResponse>> DeletePackaging(
        Guid id, Guid packagingId, CancellationToken cancellationToken) =>
        await service.DeletePackagingAsync(id, packagingId, cancellationToken);

    // Replaces the whole ingredient list of the product.
    [HttpPut("{id:guid}/ingredients")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ProductResponse>> SetIngredients(
        Guid id, SetProductIngredientsRequest request, CancellationToken cancellationToken) =>
        await service.SetIngredientsAsync(id, request, cancellationToken);
}
