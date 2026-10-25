// Usage: dotnet run --file scripts/Run-NgspiceOracle.cs -- <ngspice.exe> <fixture.cir>
// Runs a fixture in an isolated build directory and emits provenance plus the
// complete human-readable batch log. It does not modify the fixture or its source checkout.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

if (args.Length != 2) throw new ArgumentException("Expected ngspice executable and fixture path.");
var executable = Path.GetFullPath(args[0]);
var fixture = Path.GetFullPath(args[1]);
if (!File.Exists(executable)) throw new FileNotFoundException("ngspice executable", executable);
if (!File.Exists(fixture)) throw new FileNotFoundException("fixture", fixture);

var directory = Path.Combine("build", "ngspice-oracles",
    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture))).ToLowerInvariant()[..16]);
Directory.CreateDirectory(directory);
var log = Path.GetFullPath(Path.Combine(directory, "ngspice.log"));
var start = new ProcessStartInfo(executable) {
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
    WorkingDirectory = directory
};
start.ArgumentList.Add("-b");
start.ArgumentList.Add("-o");
start.ArgumentList.Add(log);
start.ArgumentList.Add(fixture);
using var process = Process.Start(start) ?? throw new IOException("Could not start ngspice.");
var standardOutput = await process.StandardOutput.ReadToEndAsync();
var standardError = await process.StandardError.ReadToEndAsync();
await process.WaitForExitAsync();
var text = File.Exists(log) ? await File.ReadAllTextAsync(log) : standardOutput + standardError;
Console.WriteLine(JsonSerializer.Serialize(new {
    executable,
    fixture,
    fixture_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture))).ToLowerInvariant(),
    exit_code = process.ExitCode,
    log,
    output = text
}, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
Environment.ExitCode = process.ExitCode;
