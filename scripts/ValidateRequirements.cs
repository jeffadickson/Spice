#:property PublishAot=false
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

// StrictDoc owns grammar parsing. This additional repository-wide gate checks
// stable identity and parent resolution, which standalone SDReader calls omit.
if (args is ["--self-test"])
{
    var good = new[] { ("a", "UID: A\n"), ("b", "UID: B\n- TYPE: Parent\n  VALUE: A\n") };
    if (Identifiers(good) != (2, 1)) throw new InvalidDataException("Identity regression.");
    foreach (var bad in new[] {
        new[] { ("a", "UID: A\n"), ("b", "UID: A\n") },
        new[] { ("a", "UID: A\n- TYPE: Parent\n  VALUE: MISSING\n") }
    })
    {
        try { Identifiers(bad); throw new InvalidOperationException("Invalid identity graph accepted."); }
        catch (InvalidDataException) { }
    }
    Console.WriteLine("Requirement identity, duplicate and unresolved-parent contracts passed.");
    return;
}
if (args is not ["--python", var python, "--report", var output] || !File.Exists(python) ||
    !Directory.Exists("docs") || !File.Exists("Cargo.toml"))
    throw new ArgumentException("From checkout: --python <StrictDoc Python> --report <JSON path>.");
var parent = Path.GetDirectoryName(Path.GetFullPath(output))!;
if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
var files = Directory.GetFiles("docs", "*.sdoc").Order(StringComparer.Ordinal).ToArray();
if (files.Length == 0) throw new InvalidDataException("No requirements.");
foreach (var file in files) if (new FileInfo(file).Length > 1_048_576) throw new InvalidDataException("Oversized requirements document.");
var hashes = files.ToDictionary(file => file.Replace('\\', '/'), Hash);
var counts = Identifiers(files.Select(file => (file, File.ReadAllText(file))));
var start = new ProcessStartInfo(python) { UseShellExecute = false, RedirectStandardOutput = true,
    RedirectStandardError = true, CreateNoWindow = true };
start.ArgumentList.Add("-c");
start.ArgumentList.Add("from pathlib import Path; from strictdoc.backend.sdoc.reader import SDReader; from importlib.metadata import version; import json; files=sorted(Path('docs').glob('*.sdoc')); [SDReader.read(p.read_text(encoding='utf-8-sig'),str(p)) for p in files]; print(json.dumps({'strictdoc_version':version('strictdoc'),'documents':len(files)}))");
using var process = Process.Start(start) ?? throw new IOException("Could not start StrictDoc.");
var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(120));
try { await process.WaitForExitAsync(limit.Token); }
catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("StrictDoc parser timed out."); }
var diagnostics = await stderr;
if (process.ExitCode != 0) throw new InvalidDataException(diagnostics);
using var parsed = JsonDocument.Parse(await stdout);
if (parsed.RootElement.GetProperty("strictdoc_version").GetString() != "0.30.4" ||
    parsed.RootElement.GetProperty("documents").GetInt32() != files.Length)
    throw new InvalidDataException("StrictDoc pin/document inventory mismatch.");
foreach (var pair in hashes) if (Hash(pair.Key) != pair.Value) throw new IOException("Requirements changed during validation.");
File.WriteAllText(output, JsonSerializer.Serialize(new { status = "passed", parser = parsed.RootElement,
    unique_ids = counts.Ids, parent_relations = counts.Parents, source_hashes = hashes,
    helper_sha256 = Hash("scripts/ValidateRequirements.cs"), limitations = new[] {
        "Grammar, unique IDs and resolved parents only; not requirement acceptance or behavioral closure.",
        "Python is required by real StrictDoc; the orchestration helper executed managed, not NativeAOT." }
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"StrictDoc: {files.Length} documents, {counts.Ids} unique IDs, {counts.Parents} resolved parents.");

static (int Ids, int Parents) Identifiers(IEnumerable<(string Path, string Text)> sources)
{
    var ids = new HashSet<string>(StringComparer.Ordinal); var parents = new List<string>();
    foreach (var (path, text) in sources)
    {
        foreach (Match match in Regex.Matches(text, @"(?m)^UID: (\S+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            if (!ids.Add(match.Groups[1].Value)) throw new InvalidDataException("Duplicate requirement UID in " + path);
        parents.AddRange(Regex.Matches(text, @"(?m)^- TYPE: Parent\r?\n  VALUE: (\S+)", RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)).Select(match => match.Groups[1].Value));
    }
    if (parents.Any(parent => !ids.Contains(parent))) throw new InvalidDataException("Unresolved parent requirement.");
    return (ids.Count, parents.Count);
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
