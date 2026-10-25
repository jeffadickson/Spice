// Usage: dotnet run --file scripts/Compare-NgspiceOracle.cs -- <ngspice.exe> <simulator.exe>
// Compares the declared V1 branch-current observable for the curated res_simple
// fixture. Extend this script with explicit per-fixture extractors; never infer
// an oracle from unlabelled table columns.
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 2) throw new ArgumentException("Expected ngspice and simulator executable paths.");
static async Task<(int ExitCode, string Output)> Run(string executable, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException($"Could not start {executable}.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, await stdout + await stderr);
}
var root = Directory.GetCurrentDirectory();
var fixture = Path.Combine(root, "compat", "ngspice", "res_simple.cir");
var request = Path.Combine(root, "compat", "ngspice", "res_simple.request.json");
var oracle = await Run(args[0], "-b", "-o", Path.Combine(root, "build", "ngspice-oracles", "comparison.log"), fixture);
if (oracle.ExitCode != 0) throw new InvalidOperationException($"ngspice failed: {oracle.Output}");
var match = Regex.Matches(oracle.Output, @"\s\d+\s+[\deE+.-]+\s+([\deE+.-]+)")
    .Select(match => double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
    .LastOrDefault();
var simulator = new ProcessStartInfo(args[1]) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
using var process = Process.Start(simulator) ?? throw new IOException("Could not start simulator.");
await process.StandardInput.WriteAsync(await File.ReadAllTextAsync(request));
process.StandardInput.Close();
var json = await process.StandardOutput.ReadToEndAsync();
var errors = await process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();
if (process.ExitCode != 0) throw new InvalidOperationException($"simulator failed: {errors}");
using var document = JsonDocument.Parse(json);
var values = document.RootElement.GetProperty("samples")[0].GetProperty("values");
var observed = values[1].GetDouble();
const double tolerance = 1e-12;
var difference = Math.Abs(observed - match);
Console.WriteLine(JsonSerializer.Serialize(new { fixture, observable = "i(V1)", ngspice = match, simulator = observed, difference, tolerance, passed = difference <= tolerance }));
if (difference > tolerance) Environment.ExitCode = 1;
