#:property PublishAot=false
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
// Run at repo root: dotnet run --file scripts/Mutation.cs
// Copies owned source into an ignored sandbox; never edits the working source.
var root = Directory.GetCurrentDirectory();
if (args.Length > 1 || (args.Length == 1 && args[0] != "--list"))
    throw new ArgumentException("Only --list is supported.");
// Cold overnight builds may need a larger per-test limit; the outer campaign still owns its total deadline.
var timeoutText = Environment.GetEnvironmentVariable("SPICE_MUTATION_TIMEOUT_SECONDS");
var testTimeoutSeconds = timeoutText is null ? 180 : int.Parse(timeoutText, System.Globalization.CultureInfo.InvariantCulture);
if (testTimeoutSeconds is <= 0 or > 900) throw new ArgumentException("Mutation timeout must be in 1..900 seconds.");
var sandbox = Path.GetFullPath(Path.Combine(root, "work", "mutation-sandbox"));
if (!sandbox.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Sandbox escaped repository.");
if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
foreach (var required in new[] { "src", "tests", "examples", "compat/ngspice" })
    if (!Directory.Exists(Path.Combine(root, required))) throw new DirectoryNotFoundException(required);
if (!Directory.Exists(sandbox)) Directory.CreateDirectory(sandbox);
var files = new[] { "Cargo.toml", "Cargo.lock" }.Concat(
    Directory.EnumerateFiles(Path.Combine(root, "src"), "*.rs", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.rs", SearchOption.AllDirectories))
        .Concat(Directory.EnumerateFiles(Path.Combine(root, "examples"), "*.json"))
        .Concat(Directory.EnumerateFiles(Path.Combine(root, "compat", "ngspice"), "*.*", SearchOption.AllDirectories))
        .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))).ToArray();
var snapshots = files.ToDictionary(p => p, p => File.ReadAllText(Path.Combine(root, p)));
void Restore()
{
    foreach (var (path, text) in snapshots)
    {
        var target = Path.Combine(sandbox, path);
        var parent = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(target, text);
    }
}
async Task<(int Exit, string Log)> Test(string? target = null)
{
    var start = new ProcessStartInfo("cargo") { WorkingDirectory = sandbox,
        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var arg in new[] { "test", "--locked" }) start.ArgumentList.Add(arg);
    if (target is not null) { start.ArgumentList.Add("--test"); start.ArgumentList.Add(target); }
    start.Environment["CARGO_TARGET_DIR"] = Environment.GetEnvironmentVariable("SPICE_MUTATION_TARGET_DIR") ?? Path.Combine(root, "target", "mutation");
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(testTimeoutSeconds));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); return (-1, "timeout"); }
    return (process.ExitCode, await stdout + await stderr);
}
var mutants = new (string Name, string File, string Pattern, string Replacement)[] {
    ("stamp-sign", "src/compile.rs", @"a\.add_stamp\(i,\s*i,\s*g\)", "a.add_stamp(i,i,-g)"),
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
    ("missing-assertion", "src/assertion.rs", @"_\s*=>\s*Outcome::Indeterminate", "_ => Outcome::Pass"),
    ("variation-grid-limit", "src/variation.rs", @"product\s*>\s*64", "false"),
    ("diode-current-sign", "src/model.rs", @"exponential\s*-\s*1\.0", "1.0 - exponential"),
    // The backend-neutral seam must reject nonfinite coefficients before an adapter receives them.
    ("sparse-contract-nonfinite", "src/solver_contract.rs", @"!value\.is_finite\(\)", "false"),
    // Continuation must not silently jump from zero to the final source scale.
    ("continuation-stage-skip", "src/newton.rs", @"let target_scale = \(source_scale \+ step\)\.min\(1\.0\);", "let target_scale = 1.0;"),
    // A regularized warm start must never masquerade as a physical operating point.
    ("gmin-final-shunt", "src/newton.rs", @"final_options\.gmin_siemens = None;", "final_options.gmin_siemens = Some(gmin);"),
    ("gmin-stage-budget", "src/newton.rs", @"stage\.gmin_siemens > continuation\.minimum_siemens", "false"),
    // Independent audits must detect polarity, energy and acceptance defects.
    ("audit-kcl-polarity", "src/audit.rs", @"sums\[n\.0\] -= current;", "sums[n.0] += current;"),
    ("audit-capacitor-current", "src/audit.rs", @"\*current = farads \* \(v - old_v\) / step_s;", "*current = 0.;"),
    ("audit-api-enforcement", "src/api.rs", @"c\.status == crate::audit::CheckStatus::Failed", "false"),
    ("audit-check-acceptance", "src/audit.rs", @"discrepancy\.abs\(\) <= tolerance", "true"),
    // Campaign 335–344 guards challenge finite norms, provider contracts,
    // thermal trial bounds, source assembly and complete recovery accounting.
    ("newton-nan-norm", "src/newton.rs", @"if values\.iter\(\)\.any\(\|value\| !value\.is_finite\(\)\) \{", "if false {"),
    ("gmin-zero-reduction", "src/newton.rs", @"self\.reduction_factor <= 0\.", "self.reduction_factor < 0."),
    ("be-provider-correction", "src/newton.rs", @"validate_correction\(plan, &update\)\?;", "let _ = &update;"),
    ("limited-direction-backtrack", "src/newton.rs", @"let scale = alpha \* limit;", "let scale = alpha.min(limit);"),
    ("thermal-trial-bound", "src/newton.rs", @"\(2\. \* diode\.model\.thermal_volts\)\.min\(0\.5\)", "0.5"),
    ("be-requested-rhs", "src/newton.rs", @"let mut system = linearize_with_rhs\(plan, values, rhs\)\?;", "let mut system = linearize(plan, values)?;"),
    ("recovery-work-total", "src/newton.rs", @"report\.linear_solves = counted\.calls\.load\(std::sync::atomic::Ordering::Relaxed\);", "report.linear_solves = report.iterations;")
};
// The baseline runs the full suite; each operator then uses its focused regression target.
var targets = new Dictionary<string,string> {
    ["stamp-sign"]="dc",["stored-charge"]="transient",["inclusive-boundary"]="analyses",
    ["milli-scale"]="analyses",["residual-acceptance"]="faults",["cancel-check"]="transient",
    ["state-commit"]="transient",["flux-sign"]="dynamic",["adaptive-acceptance"]="adaptive",
    ["sparse-row-order"]="providers",["pwl-interpolation"]="edge_cases",["missing-assertion"]="analyses",
    ["variation-grid-limit"]="variation",["diode-current-sign"]="nonlinear",
    ["sparse-contract-nonfinite"]="matrix_conformance",["continuation-stage-skip"]="nonlinear",
    ["gmin-final-shunt"]="continuation",["gmin-stage-budget"]="continuation",
    ["audit-kcl-polarity"]="consistency",["audit-capacitor-current"]="consistency",
    ["audit-api-enforcement"]="consistency",["audit-check-acceptance"]="consistency",
    ["newton-nan-norm"]="continuation_hardening",["gmin-zero-reduction"]="continuation_hardening",
    ["be-provider-correction"]="continuation_hardening",["limited-direction-backtrack"]="continuation_hardening",
    ["thermal-trial-bound"]="continuation_hardening",["be-requested-rhs"]="continuation_hardening",
    ["recovery-work-total"]="continuation_hardening"
};
// Validate every site before paying for a baseline build. List mode is a real
// source-pattern preflight, not mutation execution or a passing mutation score.
foreach (var mutant in mutants) {
    if (!targets.ContainsKey(mutant.Name) || !Regex.IsMatch(snapshots[mutant.File], mutant.Pattern))
        throw new InvalidOperationException("Mutation target missing: " + mutant.Name);
}
if (args.Length == 1) {
    foreach (var mutant in mutants) Console.WriteLine(mutant.Name + ": " + targets[mutant.Name]);
    return;
}
Restore();
var baseline = await Test();
if (baseline.Exit != 0) throw new Exception("Mutation baseline failed: " + baseline.Log);
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
if (!Directory.Exists("evidence")) Directory.CreateDirectory("evidence");
File.WriteAllText("evidence/mutation.json", JsonSerializer.Serialize(new {
    strategy = "selected deliberate source mutations, isolated copied tree; not exhaustive cargo-mutants campaign",
    source_hashes = snapshots.Where(p => p.Key.StartsWith("src/") || p.Key.StartsWith("tests/"))
        .ToDictionary(p => p.Key, p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.Value))).ToLowerInvariant()),
    baseline = "passed", killed, total = mutants.Length, mutants = reports
}, new JsonSerializerOptions { WriteIndented = true }));
if (killed != mutants.Length) Environment.ExitCode = 1;
