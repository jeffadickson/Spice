using Simulator.Client;
using System.Text.Json;
using Xunit;

/// <summary>Adversarial native-process envelopes, isolated from the real numerical engine.</summary>
public sealed class TransportContracts
{
    /// <summary>A normal EOF must complete before cancellation, rather than spin until a mutation-runner timeout.</summary>
    [Fact]
    public async Task SmallSuccessfulEnvelopeCompletesBeforeItsOwnDeadline()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var response = await Fixture().RunJsonAsync(Request(
            """{"schema_version":"1","status":"succeeded","complete":true}"""), deadline.Token).WaitAsync(deadline.Token);
        Assert.True(response.Succeeded);
    }
    [Fact]
    public async Task OneByteAboveDiagnosticLimitFails()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(
            JsonSerializer.Serialize(new { flood = "stderr", blocks = 4096, diagnostics = "x",
                output = """{"schema_version":"1","status":"succeeded","complete":true}""" }), deadline.Token));
        Assert.Contains("32 MiB", error.Message);
    }
    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public async Task RejectsInvalidUtf8OnEitherPipe(string stream)
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(
            JsonSerializer.Serialize(new { invalid_utf8 = stream, output = "" })));
        Assert.Contains("UTF-8", error.Message);
    }

    [Fact]
    public async Task MultibyteDiagnosticsUseTheByteLimit()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(
            JsonSerializer.Serialize(new { flood = "stderr-unicode", blocks = 2049,
                output = """{"schema_version":"1","status":"succeeded","complete":true}""" }), deadline.Token));
        Assert.Contains("32 MiB", error.Message);
    }

    [Fact]
    public async Task ExactRequestAndDiagnosticLimitsRemainAccepted()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = JsonSerializer.Serialize(new { flood = "stderr", blocks = 4096,
            output = """{"schema_version":"1","status":"succeeded","complete":true}""" });
        request += new string(' ', 1_048_576 - request.Length);
        var response = await Fixture().RunJsonAsync(request, deadline.Token);
        Assert.True(response.Succeeded);
    }
    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public async Task OversizedChildStreamsFailWithoutPipeDeadlock(string stream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = JsonSerializer.Serialize(new {
            flood = stream, output = """{"schema_version":"1","status":"succeeded","complete":true}"""
        });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Fixture().RunJsonAsync(request, deadline.Token));
        Assert.Contains("32 MiB", error.Message);
    }
    [Fact]
    public async Task PreCancelledRequestDoesNotStartAProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SimulationClient("nonexistent-engine").RunJsonAsync("{}", cancellation.Token));
    }

    [Fact]
    public async Task CancellationStopsTheStartedChild()
    {
        var parent = Path.GetTempPath();
        Assert.True(Directory.Exists(parent));
        var directory = Path.Combine(parent, "spice-cancel-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "pid.txt");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = Fixture().RunJsonAsync(JsonSerializer.Serialize(new {
            mode = "hang", pid_file = marker, output = ""
        }), cancellation.Token);
        try
        {
            // A fixture acknowledgement removes startup races: cancellation
            // happens after the actual child has started and read its request.
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(marker)) await Task.Delay(10, startup.Token);
            var pid = int.Parse(await File.ReadAllTextAsync(marker, startup.Token));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            try
            {
                using var child = System.Diagnostics.Process.GetProcessById(pid);
                Assert.True(child.HasExited, "Cancellation left the fixture process alive.");
            }
            catch (ArgumentException) { /* The exited process is already reaped. */ }
        }
        finally
        {
            cancellation.Cancel();
            try { await run; } catch (OperationCanceledException) { }
            if (File.Exists(marker)) File.Delete(marker);
            Directory.Delete(directory);
        }
    }
    private static SimulationClient Fixture()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "fixture",
            "Simulator.TransportFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(executable), "Build the transport fixture with the test project.");
        return new SimulationClient(executable);
    }

    private static string Request(string output, int exit = 0) =>
        JsonSerializer.Serialize(new { output, exit_code = exit });

    [Fact]
    public async Task AcceptsControlledCurrentEnvelope()
    {
        var response = await Fixture().RunJsonAsync(Request(
            """{"schema_version":"1","status":"succeeded","complete":true}"""));
        Assert.Equal("1", response.SchemaVersion);
        Assert.True(response.Succeeded);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("")]
    public async Task RejectsUnsupportedProtocolVersions(string version)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(
            Request($$"""{"schema_version":"{{version}}","status":"succeeded","complete":true}""")));
    }

    [Fact]
    public async Task RejectsMalformedJsonFromChild()
    {
        await Assert.ThrowsAnyAsync<JsonException>(() => Fixture().RunJsonAsync(Request("{")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"schema_version":1,"status":"succeeded","complete":true}""")]
    [InlineData("""{"schema_version":"1","status":"unknown","complete":true}""")]
    [InlineData("""{"schema_version":"1","status":"succeeded","complete":false}""")]
    [InlineData("""{"schema_version":"1","status":"succeeded","complete":"true"}""")]
    [InlineData("""{"schema_version":"1","status":"failed","complete":true,"error":{"code":"x","message":"m"}}""")]
    [InlineData("""{"schema_version":"1","status":"failed","complete":false}""")]
    [InlineData("""{"schema_version":"1","status":"failed","complete":false,"error":null}""")]
    [InlineData("""{"schema_version":"1","status":"failed","complete":false,"error":{"code":null,"message":"m"}}""")]
    [InlineData("""{"schema_version":"1","status":"succeeded","complete":true,"error":{"code":"x","message":"m"}}""")]
    public async Task RejectsMalformedOrContradictoryEnvelopes(string output)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(Request(output)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(17)]
    public async Task NonzeroExitCannotPublishSuccess(int exit)
    {
        var request = JsonSerializer.Serialize(new {
            output = """{"schema_version":"1","status":"succeeded","complete":true}""",
            diagnostics = "synthetic provider diagnostic",
            exit_code = exit
        });
        var error = await Assert.ThrowsAsync<IOException>(() => Fixture().RunJsonAsync(request));
        Assert.Contains(exit.ToString(), error.Message);
        Assert.Contains("synthetic provider diagnostic", error.Message);
    }

    [Fact]
    public async Task ZeroExitCannotPublishFailure()
    {
        await Assert.ThrowsAsync<IOException>(() => Fixture().RunJsonAsync(Request(
            """{"schema_version":"1","status":"failed","complete":false,"error":{"code":"invalid_input","message":"fixture rejection"}}""")));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(17)]
    public async Task FailedEnvelopePreservesNativeError(int exit)
    {
        var response = await Fixture().RunJsonAsync(Request(
            """{"schema_version":"1","status":"failed","complete":false,"error":{"code":"invalid_input","message":"fixture rejection"}}""", exit));
        Assert.False(response.Succeeded);
        Assert.Equal("invalid_input", response.Error!.Code);
        Assert.Equal("fixture rejection", response.Error.Message);
        Assert.Throws<InvalidOperationException>(() => response.ReadManifest());
    }
}
