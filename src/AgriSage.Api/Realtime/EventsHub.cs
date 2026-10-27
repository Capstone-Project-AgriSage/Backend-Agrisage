using AgriSage.Application.Features.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AgriSage.Api.Realtime;

// No client-supplied user/store subscriptions and no business mutations on the hub.
[Authorize]
public sealed class EventsHub(IPermissionEvaluator permissions) : Hub
{
    public override async Task OnConnectedAsync()
    {
        try { await permissions.CurrentAsync(Context.ConnectionAborted); }
        catch { Context.Abort(); return; }
        await base.OnConnectedAsync();
    }
}

public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        Guid.TryParse(connection.User?.FindFirst("sub")?.Value, out var id) ? id.ToString() : null;
}
