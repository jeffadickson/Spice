using Simulator.Benchmarks;
using Xunit;
public sealed class ProcessContracts
{
    [Fact]
    public async Task PreCancelledOperationCannotStartAnExecutable()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.Run("missing-executable", [], null,
            AppContext.BaseDirectory, TimeSpan.FromSeconds(5), canceled.Token));
    }
    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public async Task InvalidUtf8IsRejectedOnEitherPipe(string pipe)
    {
        var request = System.Text.Json.JsonSerializer.Serialize(new { invalid_utf8 = pipe, output = "" });
        await Assert.ThrowsAsync<InvalidDataException>(() => ProcessRunner.Run(Fixture, [], request, AppContext.BaseDirectory, TimeSpan.FromSeconds(5)));
    }
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixture", "Simulator.TransportFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));
    [Fact]
    public async Task SuccessfulChildRetainsOutputAndExplicitMemoryAvailability()
    {
        var r = await ProcessRunner.Run(Fixture, [], """{"output":"named result","exit_code":0}""", AppContext.BaseDirectory, TimeSpan.FromSeconds(5));
        Assert.Equal("named result", r.Output);
        Assert.Equal(0, r.ExitCode);
        Assert.True(double.IsFinite(r.ElapsedMs) && r.ElapsedMs >= 0);
        Assert.True(r.PeakWorkingSetBytes is > 0 || r.MemoryStatus.StartsWith("unavailable", StringComparison.Ordinal));
    }
    [Fact]
    public async Task NonzeroExitIsNotSilentlySuccessful()
    {
        var r = await ProcessRunner.Run(Fixture, [], """{"output":"rejected","exit_code":17}""", AppContext.BaseDirectory, TimeSpan.FromSeconds(5));
        Assert.Equal(17, r.ExitCode);
    }
    [Fact]
    public async Task OversizedOutputIsRejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ProcessRunner.Run(Fixture, [],
            """{"flood":"stdout","blocks":2048,"output":""}""", AppContext.BaseDirectory, TimeSpan.FromSeconds(5)));
    }
    [Fact]
    public async Task ExpiredBudgetCannotSupplyTimingEvidence()
    {
        // An expired budget cannot become a valid timing measurement.
        await Assert.ThrowsAsync<TimeoutException>(() => ProcessRunner.Run(Fixture, [],
            """{"mode":"hang","pid_file":"not-an-existing-parent/pid","output":""}""", AppContext.BaseDirectory, TimeSpan.FromTicks(1)));
    }
    [Fact]
    public async Task HangingChildIsAcknowledgedAndReapedOnTimeout()
    {
        var parent = Path.GetTempPath();
        Assert.True(Directory.Exists(parent));
        var directory = Path.Combine(parent, "spice-benchmark-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "pid.txt");
        var request = System.Text.Json.JsonSerializer.Serialize(new { mode = "hang", pid_file = marker, output = "" });
        var running = ProcessRunner.Run(Fixture, [], request, directory, TimeSpan.FromSeconds(2));
        System.Diagnostics.Process? owned = null;
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(marker)) await Task.Delay(10, startup.Token);
            owned = System.Diagnostics.Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(marker, startup.Token)));
            _ = owned.Handle;
            await Assert.ThrowsAsync<TimeoutException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(owned.HasExited);
        }
        finally
        {
            if (owned is not null) { using (owned) if (!owned.HasExited) { owned.Kill(true); await owned.WaitForExitAsync(); } }
            File.Delete(marker); Directory.Delete(directory);
        }
    }
}
