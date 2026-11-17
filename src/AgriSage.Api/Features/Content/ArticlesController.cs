using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Content;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Content;

[ApiController]
[Route("api/articles")]
[AllowAnonymous]
public sealed class ArticlesController(IArticleService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ArticleResponse>>> List([FromQuery] ArticleListRequest request, CancellationToken token) =>
        await service.ListAsync(request, true, token);

    [HttpGet("{slug}")]
    public async Task<ActionResult<ArticleResponse>> Get(string slug, CancellationToken token) => await service.GetPublishedAsync(slug, token);
}

[ApiController]
[Route("api/admin/articles")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class AdminArticlesController(IArticleService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ArticleResponse>>> List([FromQuery] ArticleListRequest request, CancellationToken token) =>
        await service.ListAsync(request, false, token);
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArticleResponse>> Get(Guid id, CancellationToken token) => await service.GetAsync(id, token);
    [HttpPost]
    public async Task<IActionResult> Create(ArticleRequest request, CancellationToken token)
    {
        var result = await service.CreateAsync(request, token);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ArticleResponse>> Update(Guid id, ArticleRequest request, CancellationToken token) => await service.UpdateAsync(id, request, token);
    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<ArticleResponse>> Status(Guid id, ArticleStatusRequest request, CancellationToken token) => await service.SetStatusAsync(id, request, token);
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token)
    {
        await service.DeleteAsync(id, token);
        return NoContent();
    }
}
