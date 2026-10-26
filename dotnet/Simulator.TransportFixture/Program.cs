using System.Text.Json;

// Synthetic subprocess protocol fixture. Input controls only its own output;
// these envelopes are deliberately not numerical simulator qualification.
using var request = JsonDocument.Parse(await Console.In.ReadToEndAsync());
var root = request.RootElement;
if (root.TryGetProperty("flood", out var flood))
{
    var writer = flood.GetString() == "stderr" ? Console.Error : Console.Out;
    var block = new string('x', 8192);
    for (var i = 0; i < 4097; i++) await writer.WriteAsync(block);
}
if (root.TryGetProperty("mode", out var mode) && mode.GetString() == "hang")
{
    var marker = root.GetProperty("pid_file").GetString()!;
    if (!Directory.Exists(Path.GetDirectoryName(marker))) throw new DirectoryNotFoundException();
    await File.WriteAllTextAsync(marker, Environment.ProcessId.ToString());
    await Task.Delay(Timeout.Infinite);
}
if (root.TryGetProperty("diagnostics", out var diagnostics))
    await Console.Error.WriteAsync(diagnostics.GetString());
await Console.Out.WriteAsync(root.GetProperty("output").GetString());
return root.TryGetProperty("exit_code", out var exit) ? exit.GetInt32() : 0;
