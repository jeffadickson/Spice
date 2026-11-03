#:property PublishAot=false
#:project ../dotnet/Simulator.RequestedQualification/Simulator.RequestedQualification.csproj
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Frontend;
using Simulator.RequestedQualification;
using Simulator.Syntax;

// Offline analysis only. Primary run outcomes are immutable; this audit cannot
// promote a refused input to executable or numerically agreeing evidence.
if (args.Length != 3) throw new ArgumentException("Expected completed run directory, baseline summary and fresh output JSON.");
var run = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[2]);
if (!Directory.Exists(run) || !Directory.Exists(Path.GetDirectoryName(output))) throw new DirectoryNotFoundException();
if (File.Exists(output)) throw new IOException("Fresh output required.");
var summary = Path.Combine(run, "summary.json");
var before = Hash(summary);
using var document = JsonDocument.Parse(File.ReadAllText(summary));
var report = document.RootElement;
if (report.GetProperty("state").GetString() != "completed_with_results" || !report.GetProperty("provenance_valid").GetBoolean())
    throw new InvalidDataException("Completed, provenance-valid corpus evidence required.");
using var baseline = JsonDocument.Parse(File.ReadAllText(args[1]));
if (baseline.RootElement.GetProperty("state").GetString() != "completed_with_results" ||
    !baseline.RootElement.GetProperty("provenance_valid").GetBoolean() ||
    baseline.RootElement.GetProperty("inventory_sha256").GetString() != report.GetProperty("inventory_sha256").GetString())
    throw new InvalidDataException("Baseline must have matching inventory and valid provenance.");
var old = baseline.RootElement.GetProperty("rows").EnumerateArray().ToDictionary(r => Text(r, "id"), StringComparer.Ordinal);
var currentIds = report.GetProperty("rows").EnumerateArray().Select(r => Text(r, "id")).ToArray();
if (currentIds.Length != old.Count || !old.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(currentIds))
    throw new InvalidDataException("Baseline root identity set differs.");
var primary = new List<BlockerObservation>();
var structural = new List<BlockerObservation>();
var modelMetadata = new List<BlockerObservation>();
var transitions = new List<object>();
var auditRows = new List<object>();
var cached = new Dictionary<string, CircuitReadinessReport>(StringComparer.Ordinal);
var checkout = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var sourceChecks = 0;
var requiredSources = new[] { "Simulator.Syntax", "Simulator.Client", "Simulator.Frontend" }
    .SelectMany(project => Directory.GetFiles(Path.Combine(checkout, "dotnet", project))
        .Where(file => Path.GetExtension(file) is ".cs" or ".csproj" or ".json"))
    .Concat(Directory.GetFiles(Path.Combine(checkout, "dotnet/Simulator.Syntax/Grammar"), "*.g4"))
    .Select(file => Path.GetRelativePath(checkout, file).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
var verifiedSources = new HashSet<string>(StringComparer.Ordinal);
// The independent re-audit must use the same original frontend source bytes,
// even though its reporting helper is built in a separate writable checkout.
foreach (var entry in report.GetProperty("source_hashes").EnumerateObject())
{
    var path = entry.Name.Replace('\\', '/');
    var marker = path.IndexOf("/dotnet/Simulator.", StringComparison.Ordinal);
    if (marker < 0) continue;
    var relative = path[(marker + 1)..];
    if (Hash(Path.Combine(checkout, relative)) != entry.Value.GetString())
        throw new InvalidDataException("Audit frontend source differs from frozen census.");
    verifiedSources.Add(relative);
    sourceChecks++;
}
if (sourceChecks == 0) throw new InvalidDataException("Frozen frontend source manifest missing.");
if (!requiredSources.IsSubsetOf(verifiedSources)) throw new InvalidDataException("Frozen frontend source manifest incomplete.");
var index = 0;
foreach (var row in report.GetProperty("rows").EnumerateArray())
{
    var id = Text(row, "id"); var hash = Text(row, "expected_sha256");
    var prior = old[id];
    if (Text(prior, "expected_sha256") != Text(row, "expected_sha256")) throw new InvalidDataException("Identity hash changed.");
    if (Text(prior, "status") != Text(row, "status"))
        transitions.Add(new { id, before = Text(prior, "status"), after = Text(row, "status") });
    primary.Add(new(id, hash, row.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetString()!).ToArray()));
    var expanded = Path.Combine(run, "cases", index++.ToString("D4", CultureInfo.InvariantCulture), "expanded.cir");
    if (row.GetProperty("expanded_syntax_status").GetString() != "accepted_structure") continue;
    if (!File.Exists(expanded) || Hash(expanded) != Text(row, "expanded_sha256")) throw new InvalidDataException("Expanded input changed.");
    var expandedHash = Text(row, "expanded_sha256");
    if (!cached.TryGetValue(expandedHash, out var audit))
    {
        var syntax = DeckParser.Parse(File.ReadAllText(expanded), new ParserLimits(4_194_304, 2_000_000), collectDeclarations: true);
        if (syntax.Status != "accepted_structure") throw new InvalidDataException("Audit syntax differs from corpus result.");
        audit = CircuitReadiness.Audit(syntax);
        cached.Add(expandedHash, audit);
    }
    // Deferred resistor validation and unbound model observations are retained
    // as observations, never asserted to be definite missing implementations.
    structural.Add(new(id, hash, audit.Issues.Select(i => $"{i.Layer}/{i.Code}/{CanonicalKind(i.Kind)}").ToArray()));
    modelMetadata.Add(new(id, hash, audit.Models.Select(m => $"{m.Family}/{m.ModelFamily ?? "<unbound>"}").ToArray()));
    auditRows.Add(new { id, audit.ActiveGraphComplete, audit.ObservationsTruncated,
        issues = audit.Issues, models = audit.Models });
}
if (before != Hash(summary)) throw new InvalidDataException("Summary changed during audit.");
var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
File.WriteAllText(output, JsonSerializer.Serialize(new {
    source_commit = Text(report, "source_commit"), summary_sha256 = before,
    baseline_sha256 = Hash(args[1]), analyzer_sha256 = Hash(SourcePath()),
    ranking_sha256 = Hash(Path.Combine(checkout, "dotnet/Simulator.RequestedQualification/CorpusBlockerRanking.cs")),
    audit_source_files_verified = sourceChecks, unique_expanded_audits = cached.Count,
    frontend_assembly_sha256 = Hash(typeof(CircuitReadiness).Assembly.Location),
    syntax_assembly_sha256 = Hash(typeof(DeckParser).Assembly.Location),
    audited_inputs = structural.Count, total_inputs = primary.Count,
    primary_diagnostics = CorpusBlockerRanking.Rank(primary),
    structural_observations = CorpusBlockerRanking.Rank(structural),
    observed_model_families = CorpusBlockerRanking.Rank(modelMetadata),
    transitions, audit_rows = auditRows,
    limits = new[] { "Distinct path identities and unique original content hashes are separate; counts overlap.",
        "Primary diagnostics are reached refusals; structural observations include secondary issues and deferred validation.",
        "Model presence is not a missing-model verdict. Models may already be partially supported.",
        "Sole observed issue does not predict successful execution; scalar and numerical validation are not exhaustive.",
        "CircuitReadiness traversal/observation bounds and incomplete graphs remain explicit per root." }
}, options));
Console.WriteLine($"Ranked {primary.Count} roots; structurally audited {structural.Count}; transitions {transitions.Count}.");
static string Text(JsonElement row, string property) => row.GetProperty(property).GetString() ?? "";
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static string SourcePath([CallerFilePath] string path = "") => path;
// SPICE identifiers are case-insensitive. Both spellings designate the same
// options policy; retain original issue spelling in audit_rows for inspection.
static string CanonicalKind(string kind) => kind.ToLowerInvariant() is ".option" ? ".options" : kind.ToLowerInvariant();
