using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriSage.Api.Features.Products;

// Public, read-only catalog for farmers and visitors (no sign-in). Only ACTIVE, sellable products are listed and
// nothing internal is returned. Rate limited per client IP.
[ApiController]
[Route("api/catalog")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.PublicPolicy)]
public sealed class CatalogController(ICatalogService service) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryTreeNode>>> Categories(CancellationToken cancellationToken) =>
        Ok(await service.GetCategoriesAsync(cancellationToken));

    [HttpGet("brands")]
    public async Task<ActionResult<PagedResult<PublicBrand>>> Brands(
        [FromQuery] PaginationRequest request, CancellationToken cancellationToken) =>
        await service.GetBrandsAsync(request, cancellationToken);

    [HttpGet("products")]
    public async Task<ActionResult<PagedResult<PublicProductListItem>>> Products(
        [FromQuery] CatalogProductListRequest request, CancellationToken cancellationToken) =>
        await service.GetProductsAsync(request, cancellationToken);

    // The id is the store-product id returned by the list.
    [HttpGet("products/{id:guid}")]
    public async Task<ActionResult<PublicProductResponse>> Product(Guid id, CancellationToken cancellationToken) =>
        await service.GetProductAsync(id, cancellationToken);
}
