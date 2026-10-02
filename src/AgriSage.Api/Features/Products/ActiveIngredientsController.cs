using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Products.Dtos.Requests;
using AgriSage.Application.Features.Products.Dtos.Responses;
using AgriSage.Application.Features.Products.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Products;

[ApiController]
[Route("api/active-ingredients")]
[Authorize(Roles = ApiRoles.Read)]
public sealed class ActiveIngredientsController(IActiveIngredientService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ActiveIngredientResponse>>> List(
        [FromQuery] ActiveIngredientListRequest request, CancellationToken cancellationToken) =>
        await service.ListAsync(request, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ActiveIngredientResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await service.GetAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType<ActiveIngredientResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(ActiveIngredientRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<ActionResult<ActiveIngredientResponse>> Update(
        Guid id, ActiveIngredientRequest request, CancellationToken cancellationToken) =>
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

    // Soft delete; refused while products use the ingredient.
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ApiRoles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
