#:property PublishAot=false
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Bounded evidence only: do not copy raw decks, binaries or full worker logs.
if(args is not [var baselinePath,var finalPath,var coveragePath,var mutationPath,var destination,..var gatePaths] || gatePaths.Length>1 || !Directory.Exists(destination))
    throw new ArgumentException("Usage: <baseline JSON> <final JSON> <cobertura XML> <mutation JSON> <existing evidence directory> [completed acceptance JSON]");
var baseline=JsonNode.Parse(File.ReadAllText(baselinePath))!;
var final=JsonNode.Parse(File.ReadAllText(finalPath))!;
if(final["state"]?.GetValue<string>()!="completed_with_results" || final["completed"]!.GetValue<int>()!=final["inputs"]!.GetValue<int>())
    throw new InvalidDataException("Final campaign is not complete");
var counts=final["counts"]!.AsObject();
if(counts.Sum(pair=>pair.Value!.GetValue<int>())!=final["inputs"]!.GetValue<int>())throw new InvalidDataException("Count mismatch");
var xml=XDocument.Load(coveragePath);
var coverage=new List<object>();
foreach(var scope in new[]{"handwritten","generated"})
{
    // Both separator styles occur in tool reports; select the scope explicitly.
    var lines=xml.Descendants("class").Where(c=>Regex.IsMatch((string)c.Attribute("filename")!,@"(^|[/\\])obj[/\\]")== (scope=="generated"))
        .SelectMany(c=>c.Element("lines")!.Elements("line")).ToArray();
    var covered=lines.Count(l=>(int)l.Attribute("hits")!>0);
    var branchCovered=0;var branches=0;
    foreach(var line in lines.Where(l=>bool.TryParse((string?)l.Attribute("branch"),out var branch) && branch))
    {
        var match=Regex.Match((string)line.Attribute("condition-coverage")!,@"\((\d+)/(\d+)\)");
        if(!match.Success)throw new InvalidDataException("Unrecognized branch metric");
        branchCovered+=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
        branches+=int.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture);
    }
    coverage.Add(new{scope,lines_covered=covered,lines=lines.Length,branches_covered=branchCovered,branches});
}
using var mutations=JsonDocument.Parse(File.ReadAllText(mutationPath));
var statuses=mutations.RootElement.GetProperty("files").EnumerateObject().SelectMany(f=>f.Value.GetProperty("mutants").EnumerateArray())
    .GroupBy(m=>m.GetProperty("status").GetString()!).ToDictionary(g=>g.Key,g=>g.Count());
var options=new JsonSerializerOptions{WriteIndented=true};
// Keep the source hashes and bounded row diagnostics; replace local absolute root.
final["corpus_root"]="<local ngspice checkout>";
File.WriteAllText(Path.Combine(destination,"parser-only-corpus.json"),final.ToJsonString(options));
File.WriteAllText(Path.Combine(destination,"parser-only-verification.json"),JsonSerializer.Serialize(new{
    coverage,mutations=statuses,
    input_reports=new{baseline_sha256=Hash(baselinePath),final_sha256=Hash(finalPath),coverage_sha256=Hash(coveragePath),mutation_sha256=Hash(mutationPath)},
    baseline_counts=baseline["counts"],baseline_source=baseline["source_sha256"],
    limitations=new[]{"Generated recognizer and handwritten boundary coverage are separate; campaign CLI/PowerShell coverage is not included in this test collector.",
        "Stryker mutates the selected handwritten C# files, not ANTLR grammar productions or generated recognition code.",
        "Historical intermediate baseline source was uncommitted; hashes record actual bytes but are not a reproducible committed baseline."}
},options));
var remainingBaseline=baseline["inputs"]!.GetValue<int>()-baseline["counts"]!["accepted_structure"]!.GetValue<int>();
var remainingFinal=final["inputs"]!.GetValue<int>()-final["counts"]!["accepted_structure"]!.GetValue<int>();
var total=final["inputs"]!.GetValue<int>();
var chartTitle="Inputs without structural acceptance";
var chartScope=$"Same {total}-input inventory; includes gaps, rejections and unavailable evidence";
if(gatePaths.Length==1)
{
    var gate=JsonNode.Parse(File.ReadAllText(gatePaths[0]))!;
    if(gate["status"]?.GetValue<string>()!="all_declared_valid_inputs_accepted" || gate["primary_sha256"]?.GetValue<string>()!=Hash(finalPath) ||
        baseline["inventory_sha256"]?.GetValue<string>()!=final["inventory_sha256"]?.GetValue<string>())
        throw new InvalidDataException("A completed same-inventory acceptance gate is required for the valid-input chart");
    var excluded=gate["excluded"]!.AsArray().Select(row=>row!["key"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
    remainingBaseline=baseline["rows"]!.AsArray().Count(row=>!excluded.Contains(row!["scope"]+"/"+row["path"]) && row["result"]!["Status"]!.GetValue<string>()!="accepted_structure");
    remainingFinal=gate["outstanding"]!.AsArray().Count;total=gate["required_inputs"]!.GetValue<int>();
    chartTitle="Declared valid inputs without acceptance";
    chartScope=$"{total} valid inputs; {excluded.Count} pinned exclusions; {gate["accepted_via_explicit_replay"]} explicit legacy replay acceptances";
}
var widthBaseline=remainingBaseline*620d/total;var widthFinal=remainingFinal*620d/total;
var svg=FormattableString.Invariant($"""
<svg xmlns="http://www.w3.org/2000/svg" width="900" height="230" viewBox="0 0 900 230" role="img" aria-labelledby="title desc">
<title id="title">Parser structural-acceptance backlog</title>
<desc id="desc">{remainingBaseline} inputs without structural acceptance initially; {remainingFinal} in the final run, out of {total}. This is not numerical qualification.</desc>
<rect width="900" height="230" fill="#f5f7fb"/>
<g font-family="Segoe UI, sans-serif" fill="#18243a">
<text x="24" y="32" font-size="21">{chartTitle}</text>
<text x="24" y="56" font-size="13">{chartScope}</text>
<text x="24" y="104">Initial</text><rect x="130" y="80" width="{widthBaseline:F2}" height="32" fill="#64748b"/>
<text x="{140+widthBaseline:F2}" y="102">{remainingBaseline}</text>
<text x="24" y="154">Final</text><rect x="130" y="130" width="{widthFinal:F2}" height="32" fill="#2563eb"/>
<text x="{140+widthFinal:F2}" y="152">{remainingFinal}</text>
<text x="24" y="198" font-size="13">Structural acceptance does not mean complete grammar conformance or simulation support.</text>
</g></svg>
""");
XDocument.Parse(svg); // Validate generated XML before retaining it.
File.WriteAllText(Path.Combine(destination,"parser-only-burndown.svg"),svg);
Console.WriteLine(JsonSerializer.Serialize(new{coverage,mutations=statuses,remainingBaseline,remainingFinal}));
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
