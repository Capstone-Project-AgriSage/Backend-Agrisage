using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Notifications;

[ApiController, Authorize, Route("api/me/notifications")]
public sealed class MeNotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationResponse>> List([FromQuery] NotificationListRequest request, CancellationToken token) =>
        notifications.ListAsync(request, token);
    [HttpGet("unread-count")]
    public Task<UnreadCountResponse> Count(CancellationToken token) => notifications.UnreadCountAsync(token);
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken token) { await notifications.ReadAsync(id, token); return NoContent(); }
    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken token) { await notifications.ReadAllAsync(token); return NoContent(); }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken token) { await notifications.ArchiveAsync(id, token); return NoContent(); }
}
