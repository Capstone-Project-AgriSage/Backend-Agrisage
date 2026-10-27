using AgriSage.Application.Features.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace AgriSage.Api.Realtime;

public sealed class SignalRPublisher(IHubContext<EventsHub> hub, RealtimeAudience audience) : IRealtimePublisher
{
    public async Task PublishAsync(RealtimeSignal signal, CancellationToken token)
    {
        var recipients = await audience.RecipientsAsync(signal, token);
        foreach (var chunk in recipients.Distinct().Chunk(500))
            await hub.Clients.Users(chunk.Select(id => id.ToString()).ToArray())
                .SendAsync("Changed", new RealtimeChange(signal.EventId, signal.Topic), token);
    }

    public Task ResynchronizeAsync(CancellationToken token) =>
        hub.Clients.All.SendAsync("Changed", new RealtimeChange(Guid.NewGuid(), "resync"), token);
}
