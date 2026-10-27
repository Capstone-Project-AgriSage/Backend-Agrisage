using System.Threading.Channels;

namespace AgriSage.Infrastructure.BackgroundJobs;

// Coalesce bursts into one wake-up; durable work is always read from PostgreSQL, never held in this channel.
public sealed class RealtimeWakeSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    public void Wake() => channel.Writer.TryWrite(true);
    public async Task WaitAsync(TimeSpan recoveryInterval, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(recoveryInterval);
        try { await channel.Reader.ReadAsync(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
    }
}
