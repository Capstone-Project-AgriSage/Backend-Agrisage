namespace AgriSage.Application.Common.Interfaces;

// Sends a server-composed business message through the configured SMS transport.
public interface ISmsTextSender
{
    bool IsConfigured { get; }
    Task SendAsync(string destination, string text, string purpose, CancellationToken token);
}
