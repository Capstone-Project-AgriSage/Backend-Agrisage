using AgriSage.Application.Common.Interfaces;

namespace AgriSage.IntegrationTests.Infrastructure.Debt;

// The database tests run without an SMS transport, as an environment without SMS settings does: the services that need
// one (DebtFollowUpService, staff-triggered debt SMS) resolve, and sending reports "not configured". HTTP tests that send
// a message replace it with their own capturing sender.
public sealed class UnconfiguredSmsSender : ISmsTextSender
{
    public bool IsConfigured => false;

    public Task SendAsync(string destination, string text, string purpose, CancellationToken token) =>
        throw new InvalidOperationException("SMS is not configured in the database tests.");
}
