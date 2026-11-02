using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Advisory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Advisory;

// Links between a problem and a product (api-flows/ADVISORY.md section 6). Reading: Admin, Store Owner, Sales; writing and
// approving: Admin and Store Owner. DELETE only deactivates the link.
[ApiController]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class DiseaseProductsController(IDiseaseProductService service) : ControllerBase
{
    [HttpGet("api/diseases/{id:guid}/products")]
    [Authorize(Roles = ApiRoles.Operate)]
    public async Task<ActionResult<IReadOnlyList<DiseaseProductResponse>>> List(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(id, cancellationToken));

    [HttpPost("api/diseases/{id:guid}/products")]
    [ProducesResponseType<DiseaseProductResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid id, CreateDiseaseProductRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(id, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPut("api/disease-products/{id:guid}")]
    public async Task<ActionResult<DiseaseProductResponse>> Update(
        Guid id, DiseaseProductRequest request, CancellationToken cancellationToken) =>
        await service.UpdateAsync(id, request, cancellationToken);

    // A person other than the one who wrote the content should approve it; the permission is separate from editing.
    [HttpPost("api/disease-products/{id:guid}/approve")]
    public async Task<ActionResult<DiseaseProductResponse>> Approve(Guid id, CancellationToken cancellationToken) =>
        await service.ApproveAsync(id, cancellationToken);

    [HttpDelete("api/disease-products/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeactivateAsync(id, cancellationToken);

        return NoContent();
    }
}
