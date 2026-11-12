using AgriSage.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgriSage.IntegrationTests.Infrastructure.BackgroundJobs;

public class WorkerShutdownTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("operations")]
    [InlineData("outbox")]
    public async Task Shutdown_does_not_retry_or_log_a_scope_disposed_during_cancellation(string kind)
    {
        using var scopes = new FailingScopes(waitForShutdown: true);
        var logs = new CapturedLogs();
        using var worker = CreateWorker(kind, scopes, logs);
        try
        {
            await worker.StartAsync(Token);
            await scopes.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            // Reproduces a scope failure while the host is stopping and its logger has been disposed.
            logs.IsDisposed = true;
            var stopped = worker.StopAsync(Token);
            scopes.Release.Set();
            await stopped.WaitAsync(TimeSpan.FromSeconds(5), Token);
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), Token);

            Assert.Equal(1, scopes.Attempts);
            Assert.Equal(0, logs.Writes);
            Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        }
        finally
        {
            scopes.Release.Set();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("operations")]
    [InlineData("outbox")]
    public async Task Failures_while_running_are_still_logged_and_worker_remains_available_for_retry(string kind)
    {
        using var scopes = new FailingScopes(waitForShutdown: false);
        var logs = new CapturedLogs();
        using var worker = CreateWorker(kind, scopes, logs);
        try
        {
            await worker.StartAsync(Token);
            await logs.Written.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.False(worker.ExecuteTask!.IsCompleted);
            await worker.StopAsync(Token);
            await worker.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5), Token);

            Assert.Equal(1, logs.Writes);
            Assert.Contains("IOException", logs.LastMessage);
            Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    private static BackgroundService CreateWorker(string kind, IServiceScopeFactory scopes, CapturedLogs logs)
    {
        var jobs = Options.Create(new BackgroundJobOptions
        {
            Enabled = true, ExpireLots = true, DebtReminders = false, InventoryAlerts = false,
            ReconcilePayments = false, ExpireAuthentication = false
        });
        return kind == "operations"
            ? new OperationsWorker(scopes, jobs, TimeProvider.System, new TestLogger<OperationsWorker>(logs))
            : new RealtimeOutboxWorker(scopes, new RealtimeWakeSignal(), jobs,
                Options.Create(new RealtimeOptions()), new TestLogger<RealtimeOutboxWorker>(logs));
    }

    private sealed class FailingScopes(bool waitForShutdown) : IServiceScopeFactory, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public int Attempts;
        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref Attempts);
            Entered.TrySetResult();
            if (waitForShutdown)
            {
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test did not release the worker.");
                throw new ObjectDisposedException("ServiceProvider");
            }
            throw new IOException("Temporary dependency failure.");
        }
        public void Dispose() => Release.Dispose();
    }

    private sealed class CapturedLogs
    {
        public volatile bool IsDisposed;
        public int Writes;
        public string LastMessage = "";
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Write(string message)
        {
            Interlocked.Increment(ref Writes);
            if (IsDisposed) throw new ObjectDisposedException("EventLogInternal");
            LastMessage = message;
            Written.TrySetResult();
        }
    }

    private sealed class TestLogger<T>(CapturedLogs logs) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => logs.Write(formatter(state, exception));
    }
}
