using Simulator.Client;
using Xunit;

public sealed class TransportCancellationRaceTests
{
    [Fact]
    public void NativeLaunchUsesHeadlessRedirectedTransport()
    {
        var start = SimulationClient.CreateStartInfo("engine path");
        Assert.Equal("engine path", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
        Assert.True(start.RedirectStandardError);
    }

    [Fact]
    public async Task CompletedReadStillObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellingRead(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SimulationClient.ReadBoundedAsync(stream, cancellation.Token));
        Assert.Equal(1, stream.Reads);
    }

    [Fact]
    public async Task ReadyTasksStillObserveCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SimulationClient.DrainAsync([Task.CompletedTask], cancellation.Token));
    }

    // Model a pipe read that wins the IO race while the caller's token is cancelled.
    private sealed class CancellingRead(CancellationTokenSource cancellation) : MemoryStream
    {
        public int Reads { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++Reads != 1) return ValueTask.FromResult(0);
            buffer.Span[0] = (byte)'x';
            cancellation.Cancel();
            return ValueTask.FromResult(1);
        }
    }
}
