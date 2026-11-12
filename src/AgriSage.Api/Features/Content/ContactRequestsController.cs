using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Content;

[ApiController]
[Route("api/contact-requests")]
[Authorize(Roles = ApiRoles.Operate)]
public sealed class ContactRequestsController(IContactRequestService service) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create(ContactRequestCreate request, CancellationToken token)
    {
        var result = await service.CreateAsync(request, token);
        // Guests have a receipt, but no public read endpoint for private contact information.
        return StatusCode(StatusCodes.Status201Created, result);
    }
    [HttpGet]
    public async Task<ActionResult<PagedResult<ContactRequestResponse>>> List([FromQuery] ContactRequestList request, CancellationToken token) =>
        await service.ListAsync(request, false, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContactRequestResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, false, token);
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContactRequestResponse>> Update(Guid id, ContactRequestUpdate request, CancellationToken token) => await service.UpdateAsync(id, request, token);
}

[ApiController]
[Route("api/me/contact-requests")]
[Authorize(Roles = "FARMER")]
public sealed class MeContactRequestsController(IContactRequestService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ContactRequestResponse>>> List([FromQuery] ContactRequestList request, CancellationToken token) =>
        await service.ListAsync(request, true, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContactRequestResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, true, token);
}
