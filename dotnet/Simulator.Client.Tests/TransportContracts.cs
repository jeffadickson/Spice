using Simulator.Client;
using System.Text.Json;
using Xunit;

/// <summary>Adversarial native-process envelopes, isolated from the real numerical engine.</summary>
public sealed class TransportContracts
{
    /// <summary>A startup failure must reap a descendant even before the parent acknowledges readiness.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupFailureBeforeParentAcknowledgementReapsDescendant(bool syntheticFailure)
    {
        var parent = Path.GetTempPath();
        Assert.True(Directory.Exists(parent));
        var directory = Path.Combine(parent, "spice-startup-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "parent.txt");
        var descendant = Path.Combine(directory, "descendant.txt");
        if (!syntheticFailure) await File.WriteAllTextAsync(marker, "occupied");
        System.Diagnostics.Process? owned = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run = Fixture().RunJsonAsync(JsonSerializer.Serialize(new {
            mode = "hang", pid_file = marker, descendant_file = descendant,
            fail_before_ack = syntheticFailure, output = ""
        }), deadline.Token);
        try
        {
            while (!File.Exists(descendant)) await Task.Delay(10, deadline.Token);
            var pid = int.Parse(await File.ReadAllTextAsync(descendant, deadline.Token));
            try { owned = System.Diagnostics.Process.GetProcessById(pid); _ = owned.Handle; }
            catch (ArgumentException) { /* Already reaped by the fixture. */ }
            await Assert.ThrowsAnyAsync<JsonException>(() => run.WaitAsync(TimeSpan.FromSeconds(7)));
            if (syntheticFailure) Assert.False(File.Exists(marker), "Failure unexpectedly acknowledged readiness.");
            else Assert.Equal("occupied", await File.ReadAllTextAsync(marker));
            if (owned is not null) Assert.True(owned.HasExited, "Startup failure left its descendant alive.");
        }
        finally
        {
            deadline.Cancel();
            if (owned is not null)
            {
                using (owned)
                    if (!owned.HasExited) { owned.Kill(true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            }
            try { await run.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException or JsonException) { }
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
    /// <summary>Windows automation must not create a console window for the native worker.</summary>
    [Fact]
    public async Task WindowsWorkerHasNoConsoleWindow()
    {
        // CreateNoWindow has no corresponding Unix console-window contract.
        if (!OperatingSystem.IsWindows()) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response = await Fixture().RunJsonAsync("""{"inspect_console":true}""", deadline.Token);
        Assert.False(response.Payload.GetProperty("has_console").GetBoolean());
    }
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
            new BoundedTestClient("nonexistent-engine").RunJsonAsync("{}", cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationStopsTheStartedChild(bool includeDescendant)
    {
        var parent = Path.GetTempPath();
        Assert.True(Directory.Exists(parent));
        var directory = Path.Combine(parent, "spice-cancel-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, "pid.txt");
        var descendantMarker = Path.Combine(directory, "descendant.txt");
        var owned = new List<System.Diagnostics.Process>();
        var request = new Dictionary<string, object> { ["mode"] = "hang", ["pid_file"] = marker, ["output"] = "" };
        if (includeDescendant) request["descendant_file"] = descendantMarker;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = Fixture().RunJsonAsync(JsonSerializer.Serialize(request), cancellation.Token);
        try
        {
            // A fixture acknowledgement removes startup races: cancellation
            // happens after the actual child has started and read its request.
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(marker)) await Task.Delay(10, startup.Token);
            foreach (var file in includeDescendant ? new[] { marker, descendantMarker } : new[] { marker })
            {
                var pid = int.Parse(await File.ReadAllTextAsync(file, startup.Token));
                var child = System.Diagnostics.Process.GetProcessById(pid);
                owned.Add(child);
                // Retain identity before cancellation; never reopen a possibly
                // reused PID when asserting or cleaning up after the client.
                _ = child.Handle;
            }
            cancellation.Cancel();
            // The assertion deadline is independent of client cleanup. A broken
            // Kill/Wait path must fail this test instead of hanging the test host.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(owned[0].HasExited, "Cancellation left the direct fixture process alive.");
            if (includeDescendant)
            {
                // Kill(tree) and waiting for the direct child do not imply that
                // every descendant has already finished asynchronous OS exit.
                // Require bounded eventual exit without killing it in this assertion.
                await owned[1].WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(owned[1].HasExited, "Cancellation left the descendant alive.");
            }
        }
        finally
        {
            cancellation.Cancel();
            // Test-owned cleanup cannot rely on the very client path challenged
            // by mutation. Retain acknowledged process handles until reaping.
            foreach (var child in owned)
            {
                using (child)
                    if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            }
            try { await run.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException) { }
            if (File.Exists(marker)) File.Delete(marker);
            if (File.Exists(descendantMarker)) File.Delete(descendantMarker);
            Directory.Delete(directory);
        }
    }
    private static BoundedTestClient Fixture()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "fixture",
            "Simulator.TransportFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(executable), "Build the transport fixture with the test project.");
        return new BoundedTestClient(executable);
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
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(
            Request($$"""{"schema_version":"{{version}}","status":"succeeded","complete":true}""")));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
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
    [InlineData("""{"schema_version":"1","status":1,"complete":true}""")]
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
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(Request(output)));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    /// <summary>Wrong-type error fields must produce a transport diagnostic, not incidental JSON exceptions.</summary>
    [Theory]
    [InlineData("code", "17")]
    [InlineData("message", "17")]
    [InlineData("code", "[]")]
    [InlineData("message", "null")]
    [InlineData("code", "\"\"")]
    [InlineData("message", "\"  \"")]
    public async Task RejectsErrorFieldsIndependently(string field, string value)
    {
        var code = field == "code" ? value : "\"invalid_input\"";
        var message = field == "message" ? value : "\"fixture rejection\"";
        var output = "{\"schema_version\":\"1\",\"status\":\"failed\",\"complete\":false,\"error\":{\"code\":"
            + code + ",\"message\":" + message + "}}";
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(Request(output, 2)));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
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
