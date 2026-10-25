#:property PublishAot=false
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
// Run at repo root: dotnet run --file scripts/Mutation.cs
// Copies owned source into an ignored sandbox; never edits the working source.
var root = Directory.GetCurrentDirectory();
var sandbox = Path.GetFullPath(Path.Combine(root, "work", "mutation-sandbox"));
if (!sandbox.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Sandbox escaped repository.");
Directory.CreateDirectory(sandbox);
var files = new[] { "Cargo.toml", "Cargo.lock" }.Concat(
    Directory.EnumerateFiles(Path.Combine(root, "src"), "*.rs", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.rs", SearchOption.AllDirectories))
        .Concat(Directory.EnumerateFiles(Path.Combine(root, "examples"), "*.json"))
        .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))).ToArray();
var snapshots = files.ToDictionary(p => p, p => File.ReadAllText(Path.Combine(root, p)));
void Restore()
{
    foreach (var (path, text) in snapshots)
    {
        var target = Path.Combine(sandbox, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text);
    }
}
async Task<(int Exit, string Log)> Test(string? target = null)
{
    var start = new ProcessStartInfo("cargo") { WorkingDirectory = sandbox,
        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var arg in new[] { "test", "--locked" }) start.ArgumentList.Add(arg);
    if (target is not null) { start.ArgumentList.Add("--test"); start.ArgumentList.Add(target); }
    start.Environment["CARGO_TARGET_DIR"] = Path.Combine(root, "target", "mutation");
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); return (-1, "timeout"); }
    return (process.ExitCode, await stdout + await stderr);
}
Restore();
var baseline = await Test();
if (baseline.Exit != 0) throw new Exception("Mutation baseline failed: " + baseline.Log);
var mutants = new (string Name, string File, string Pattern, string Replacement)[] {
    ("stamp-sign", "src/compile.rs", @"a\.add\(i,\s*i,\s*g\)", "a.add(i,i,-g)"),
    ("stored-charge", "src/transient.rs", @"plan\.charge\.at\(i,\s*j\)\s*/\s*h", "0.0"),
    ("inclusive-boundary", "src/assertion.rs", @"v\s*<=\s*high", "v < high"),
    ("milli-scale", "src/quantity.rs", @"\(""m"",\s*1e-3\)", "(\"m\",1e3)"),
    ("residual-acceptance", "src/dc.rs", @"worst\s*>\s*1e-10", "worst > 1e10"),
    ("cancel-check", "src/transient.rs", @"if cancel\.load", "if false && cancel.load"),
    ("state-commit", "src/transient.rs", @"previous\s*=\s*accepted;", "previous = vec![0.0; previous.len()];"),
    ("flux-sign", "src/compile.rs", @"charge\.add\(branch,\s*branch,\s*-henries\)", "charge.add(branch,branch,*henries)"),
    ("adaptive-acceptance", "src/configured.rs", @"error\s*>\s*1\.", "error > 1e9"),
    ("sparse-row-order", "src/sparse.rs", @"w\[0\]\s*>=\s*w\[1\]", "false"),
    ("pwl-interpolation", "src/waveform.rs", @"\(1\.\s*-\s*f\)\s*\*\s*a\.value\s*\+\s*f\s*\*\s*b\.value", "a.value"),
    ("missing-assertion", "src/assertion.rs", @"_\s*=>\s*Outcome::Indeterminate", "_ => Outcome::Pass")
};
// The baseline runs the full suite; each operator then uses its focused regression target.
var targets = new Dictionary<string,string> {
    ["stamp-sign"]="dc",["stored-charge"]="transient",["inclusive-boundary"]="analyses",
    ["milli-scale"]="analyses",["residual-acceptance"]="faults",["cancel-check"]="transient",
    ["state-commit"]="transient",["flux-sign"]="dynamic",["adaptive-acceptance"]="adaptive",
    ["sparse-row-order"]="providers",["pwl-interpolation"]="edge_cases",["missing-assertion"]="analyses"
};
var reports = new List<object>(); var killed = 0;
foreach (var mutant in mutants)
{
    Restore();
    var source = snapshots[mutant.File];
    var regex = new Regex(mutant.Pattern);
    if (!regex.IsMatch(source)) throw new Exception("Mutation target missing: " + mutant.Name);
    File.WriteAllText(Path.Combine(sandbox, mutant.File), regex.Replace(source, mutant.Replacement, 1));
    var target = targets[mutant.Name];
    var test = await Test(target);
    var status = test.Exit == -1 ? "timeout" : test.Exit == 0 ? "survived" :
        test.Log.Contains("test result: FAILED") ? "killed" : "invalid";
    if (status == "killed") killed++;
    reports.Add(new { name = mutant.Name, status, exit_code = test.Exit, test_target=target });
    Console.WriteLine(mutant.Name + ": " + status);
}
Restore();
Directory.CreateDirectory("evidence");
File.WriteAllText("evidence/mutation.json", JsonSerializer.Serialize(new {
    strategy = "selected deliberate source mutations, isolated copied tree; not exhaustive cargo-mutants campaign",
    source_hashes = snapshots.Where(p => p.Key.StartsWith("src/") || p.Key.StartsWith("tests/"))
        .ToDictionary(p => p.Key, p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.Value))).ToLowerInvariant()),
    baseline = "passed", killed, total = mutants.Length, mutants = reports
}, new JsonSerializerOptions { WriteIndented = true }));
if (killed != mutants.Length) Environment.ExitCode = 1;

