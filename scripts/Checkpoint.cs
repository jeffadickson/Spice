#:property PublishAot=false
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
// Run at repo root: dotnet run --file scripts/Checkpoint.cs -- 100
var iteration = args.Length == 1 && int.TryParse(args[0], out var number) ? number : throw new ArgumentException("Expected iteration number.");
// A campaign can begin between historical decade labels. The campaign ledger
// owns its ten-change cadence; this runner accepts the actual final iteration.
if (iteration <= 0) throw new ArgumentException("Checkpoint must be a positive iteration number.");
Directory.CreateDirectory("build/checkpoints");
var commands = new (string File, string[] Args)[] {
    ("rustc", ["--version"]), ("dotnet", ["--version"]),
    ("cargo", ["build","--locked"]), ("cargo", ["test","--locked"]),
    ("cargo", ["fmt","--all","--","--check"]),
    ("cargo", ["clippy","--all-targets","--locked","--","-D","warnings"]),
    ("dotnet", ["build","dotnet/Simulator.Conformance/Simulator.Conformance.csproj"]),
    ("dotnet", ["run","--no-build","--project","dotnet/Simulator.Conformance/Simulator.Conformance.csproj","--",
        Path.GetFullPath("target/debug/circuit-simulator" + (OperatingSystem.IsWindows() ? ".exe" : ""))]),
    ("dotnet", ["test","dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj"])
};
var results = new List<object>(); var rustTests = 0; var managedChecks = 0;
foreach (var (file, arguments) in commands)
{
    var start = new ProcessStartInfo(file) { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true, CreateNoWindow=true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException("Could not start " + file);
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException(file); }
    var log = await stdout + await stderr;
    var logPath = $"build/checkpoints/{iteration}-{results.Count}.log";
    File.WriteAllText(logPath, log);
    results.Add(new { command=file, arguments, exit_code=process.ExitCode, log=logPath });
    Console.WriteLine(file + " " + string.Join(" ",arguments) + " => " + process.ExitCode);
    if (process.ExitCode != 0) throw new Exception(log);
    foreach (Match match in Regex.Matches(log, @"test result: ok\. (\d+) passed")) rustTests += int.Parse(match.Groups[1].Value);
    var managed = Regex.Match(log, @"Managed conformance: (\d+) checks passed");
    if (managed.Success) managedChecks = int.Parse(managed.Groups[1].Value);
}
var hashes = Directory.EnumerateFiles("src","*.rs").Concat(Directory.EnumerateFiles("tests","*.rs"))
    .ToDictionary(p => p.Replace('\\','/'),p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant());
File.WriteAllText($"evidence/checkpoint-{iteration}.json", JsonSerializer.Serialize(new {
    iteration, status="passed", rust_tests_passed=rustTests, managed_checks_passed=managedChecks,
    commands=results, source_hashes=hashes, scope="bounded reference prototype; not full release qualification"
},new JsonSerializerOptions { WriteIndented=true }));

