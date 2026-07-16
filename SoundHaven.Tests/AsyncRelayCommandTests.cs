using SoundHaven.Commands;

namespace SoundHaven.Tests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecuteAsync_PreventsConcurrentReentry()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int executionCount = 0;

        var command = new AsyncRelayCommand(async () =>
        {
            Interlocked.Increment(ref executionCount);
            entered.SetResult();
            await release.Task;
        });

        Task first = command.ExecuteAsync();
        await entered.Task;
        Task second = command.ExecuteAsync();

        await second;
        Assert.Equal(1, Volatile.Read(ref executionCount));
        Assert.True(command.IsRunning);

        release.SetResult();
        await first;
        Assert.False(command.IsRunning);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsExceptionsAtCommandBoundary()
    {
        Exception? reported = null;
        var expected = new InvalidOperationException("Expected failure");
        var command = new AsyncRelayCommand(
            () => Task.FromException(expected),
            onException: exception => reported = exception);

        await command.ExecuteAsync();

        Assert.Same(expected, reported);
        Assert.False(command.IsRunning);
    }
}
