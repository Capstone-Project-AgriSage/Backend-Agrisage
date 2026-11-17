using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Inventory;

[ApiController, Route("api/inventory/seasonal-rules"), Authorize(Roles = ApiRoles.Manage)]
public sealed class SeasonalRulesController(ISeasonalRuleService service) : ControllerBase
{
    [HttpGet]
    public Task<SeasonalRulePage> List([FromQuery] SeasonalRuleListRequest request, CancellationToken token) => service.ListAsync(request, token);
    [HttpGet("{id:guid}")]
    public Task<SeasonalRuleResponse> Get(Guid id, CancellationToken token) => service.GetAsync(id, token);
    [HttpPost]
    public async Task<IActionResult> Create(SeasonalRuleWriteRequest request, CancellationToken token)
    {
        var result = await service.SaveAsync(null, request, token);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:guid}")]
    public Task<SeasonalRuleResponse> Update(Guid id, SeasonalRuleWriteRequest request, CancellationToken token) => service.SaveAsync(id, request, token);
    [HttpPost("{id:guid}/deactivate")]
    public Task<SeasonalRuleResponse> Deactivate(Guid id, CancellationToken token) => service.DeactivateAsync(id, token);
}
