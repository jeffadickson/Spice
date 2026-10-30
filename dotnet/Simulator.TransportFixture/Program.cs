using System.Text.Json;

// The protocol is UTF-8 on every platform, independent of the Windows console
// code page inherited by an apphost launched without an interactive console.
Console.InputEncoding = new System.Text.UTF8Encoding(false, true);
Console.OutputEncoding = new System.Text.UTF8Encoding(false);

// A descendant acknowledges its own PID before becoming deliberately idle.
// The test owns both handles and can reap it even when tree-kill is mutated.
if (args is ["--hang-descendant", var descendantMarker])
{
    await PublishPid(descendantMarker);
    await Task.Delay(Timeout.Infinite);
    return 0;
}

// Synthetic subprocess protocol fixture. Input controls only its own output;
// these envelopes are deliberately not numerical simulator qualification.
using var request = JsonDocument.Parse(await Console.In.ReadToEndAsync());
var root = request.RootElement;
if (root.TryGetProperty("inspect_console", out _) && OperatingSystem.IsWindows())
{
    // Observe the actual spawned process, rather than asserting client source or
    // ProcessStartInfo fields. No console is allocated by this fixture itself.
    await Console.Out.WriteAsync(JsonSerializer.Serialize(new {
        schema_version = "1", status = "succeeded", complete = true,
        has_console = ConsoleProbe.GetConsoleWindow() != IntPtr.Zero
    }));
    return 0;
}
if (root.TryGetProperty("flood", out var flood))
{
    var stream = flood.GetString()!;
    var writer = stream.StartsWith("stderr", StringComparison.Ordinal) ? Console.Error : Console.Out;
    var block = new string(stream.EndsWith("unicode", StringComparison.Ordinal) ? 'é' : 'x', 8192);
    var count = root.TryGetProperty("blocks", out var blocks) ? blocks.GetInt32() : 4097;
    for (var i = 0; i < count; i++) await writer.WriteAsync(block);
}
if (root.TryGetProperty("invalid_utf8", out var invalid))
{
    var stream = invalid.GetString() == "stderr" ? Console.OpenStandardError() : Console.OpenStandardOutput();
    await stream.WriteAsync(new byte[] { 0xc3, 0x28 });
    await stream.FlushAsync();
}
if (root.TryGetProperty("mode", out var mode) && mode.GetString() == "hang")
{
    var marker = root.GetProperty("pid_file").GetString()!;
    if (root.TryGetProperty("descendant_file", out var descendantFile))
    {
        var descendant = descendantFile.GetString()!;
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add("--hang-descendant"); start.ArgumentList.Add(descendant);
        using var child = System.Diagnostics.Process.Start(start) ?? throw new IOException("Descendant did not start.");
        // Parent acknowledgement means the entire requested tree is ready.
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(descendant)) await Task.Delay(10, startup.Token);
    }
    // Publish acknowledgement only after closing the writer; File.Exists
    // during WriteAllTextAsync is not a readiness guarantee on Windows.
    await PublishPid(marker);
    await Task.Delay(Timeout.Infinite);
}
if (root.TryGetProperty("diagnostics", out var diagnostics))
    await Console.Error.WriteAsync(diagnostics.GetString());
await Console.Out.WriteAsync(root.GetProperty("output").GetString());
return root.TryGetProperty("exit_code", out var exit) ? exit.GetInt32() : 0;

static async Task PublishPid(string marker)
{
    if (!Directory.Exists(Path.GetDirectoryName(marker))) throw new DirectoryNotFoundException();
    await File.WriteAllTextAsync(marker + ".tmp", Environment.ProcessId.ToString());
    File.Move(marker + ".tmp", marker);
}

internal static class ConsoleProbe
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    internal static extern IntPtr GetConsoleWindow();
}
