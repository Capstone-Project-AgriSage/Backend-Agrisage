using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Products;

// Staff-facing categories. Read: all staff roles; write: Admin and Store Owner.
[ApiController]
[Route("api/categories")]
[Authorize(Roles = ApiRoles.Read)]
public sealed class CategoriesController(ICategoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CategoryResponse>>> List(
        [FromQuery] CategoryListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("tree")]
    public async Task<ActionResult<IReadOnlyList<CategoryTreeNode>>> Tree(CancellationToken cancellationToken) =>
        Ok(await service.GetTreeAsync(activeOnly: false, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<CategoryResponse>> Update(
        Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
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

    // Soft delete; refused while the category has sub-categories or products.
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
