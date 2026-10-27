namespace AgriSage.Application.Features.Notifications;

// Internal fan-out envelope; never contains business data, tokens or AI probabilities.
public sealed record RealtimeSignal(Guid EventId, string Topic, string Scope, Guid SubjectId);
public sealed record RealtimeChange(Guid EventId, string Topic);

public interface IRealtimePublisher
{
    Task PublishAsync(RealtimeSignal signal, CancellationToken token);
    Task ResynchronizeAsync(CancellationToken token);
}
