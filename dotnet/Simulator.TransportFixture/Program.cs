using System.Text.Json;

// Synthetic subprocess protocol fixture. Input controls only its own output;
// these envelopes are deliberately not numerical simulator qualification.
using var request = JsonDocument.Parse(await Console.In.ReadToEndAsync());
var root = request.RootElement;
if (root.TryGetProperty("diagnostics", out var diagnostics))
    await Console.Error.WriteAsync(diagnostics.GetString());
await Console.Out.WriteAsync(root.GetProperty("output").GetString());
return root.TryGetProperty("exit_code", out var exit) ? exit.GetInt32() : 0;
