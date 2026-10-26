// Usage: dotnet run --file scripts/Compare-NgspiceLowpassOracle.cs -- <ngspice.exe> <simulator.exe>
// Compares the explicitly named first-point V(2) AC observable for the copied
// ngspice filters/lowpass.cir fixture. It does not infer columns by position.
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

#pragma warning disable IL2026, IL3050

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
var fixture = Path.Combine(root, "compat", "ngspice", "lowpass.cir");
var request = Path.Combine(root, "compat", "ngspice", "lowpass.request.json");
var log = Path.Combine(root, "build", "ngspice-oracles", "lowpass-comparison.log");
Directory.CreateDirectory(Path.GetDirectoryName(log)!);
var oracle = await Run(args[0], "-b", "-o", log, fixture);
if (oracle.ExitCode != 0) throw new InvalidOperationException($"ngspice failed: {oracle.Output}");
var table = await File.ReadAllTextAsync(log);
var match = Regex.Match(table, @"(?m)^0\s+[\deE+.-]+\s+([\deE+.-]+),\s*([\deE+.-]+)");
if (!match.Success) throw new InvalidDataException("Could not find first V(2) AC row.");
var oracleReal = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
var oracleImaginary = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
var simulator = new ProcessStartInfo(args[1]) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
using var process = Process.Start(simulator) ?? throw new IOException("Could not start simulator.");
await process.StandardInput.WriteAsync(await File.ReadAllTextAsync(request));
process.StandardInput.Close();
var json = await process.StandardOutput.ReadToEndAsync();
var errors = await process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();
if (process.ExitCode != 0) throw new InvalidOperationException($"simulator failed: {errors}");
using var document = JsonDocument.Parse(json);
var value = document.RootElement.GetProperty("ac_samples")[0].GetProperty("values")[1];
var observedReal = value.GetProperty("real").GetDouble();
var observedImaginary = value.GetProperty("imag").GetDouble();
// ngspice's human-readable .print AC table rounds this fixture's components
// to seven decimal places. This bound is therefore evidence-format limited,
// not a claimed solver-accuracy target.
const double tolerance = 5e-8;
var realDifference = Math.Abs(observedReal - oracleReal);
var imaginaryDifference = Math.Abs(observedImaginary - oracleImaginary);
Console.WriteLine(JsonSerializer.Serialize(new {
    fixture, fixture_sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixture))).ToLowerInvariant(),
    observable = "v(2) at 1 kHz", ngspice = new { real = oracleReal, imaginary = oracleImaginary },
    simulator = new { real = observedReal, imaginary = observedImaginary }, real_difference = realDifference,
    imaginary_difference = imaginaryDifference, tolerance, passed = realDifference <= tolerance && imaginaryDifference <= tolerance,
}, new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
if (realDifference > tolerance || imaginaryDifference > tolerance) Environment.ExitCode = 1;
