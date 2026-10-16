using AgriSage.Application.Common.Interfaces;

namespace AgriSage.Infrastructure.Messaging;

// In-memory list of the messages the simulated SMS provider "sent" (the last 50, newest first). It starts empty again when
// the API restarts; nothing is persisted.
public sealed class SimulatedSmsOutbox : ISimulatedSmsOutbox
{
    public const int Capacity = 50;

    private readonly object _gate = new();
    private readonly LinkedList<SimulatedSmsMessage> _messages = new();

    public void Add(SimulatedSmsMessage message)
    {
        lock (_gate)
        {
            _messages.AddFirst(message);
            while (_messages.Count > Capacity)
            {
                _messages.RemoveLast();
            }
        }
    }

    public IReadOnlyList<SimulatedSmsMessage> Recent()
    {
        lock (_gate)
        {
            return _messages.ToArray();
        }
    }
}
