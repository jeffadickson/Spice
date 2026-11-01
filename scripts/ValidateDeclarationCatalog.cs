#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;

// Reuse the existing syntax acceptance gate's exact cohort and digest decisions.
// This additional gate checks catalog payloads, never simulation readiness.
if(args is not [var primaryPath,var replayPath,var acceptancePath,var output] || !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
    throw new ArgumentException("Usage: <primary JSON> <legacy JSON> <acceptance JSON> <output with existing parent>");
foreach(var path in new[]{primaryPath,replayPath,acceptancePath})if(new FileInfo(path).Length>128*1024*1024)throw new InvalidDataException("Report exceeds 128 MiB");
using var primary=JsonDocument.Parse(File.ReadAllText(primaryPath));using var replay=JsonDocument.Parse(File.ReadAllText(replayPath));
using var acceptance=JsonDocument.Parse(File.ReadAllText(acceptancePath));var gate=acceptance.RootElement;
if(gate.GetProperty("status").GetString()!="all_declared_valid_inputs_accepted" || gate.GetProperty("primary_sha256").GetString()!=Hash(primaryPath)
    || !gate.GetProperty("replay_sha256").EnumerateObject().Any(p=>p.Value.GetString()==Hash(replayPath)))
    throw new InvalidDataException("Syntax acceptance report does not match the input reports");
foreach(var document in new[]{primary.RootElement,replay.RootElement})
    if(document.GetProperty("state").GetString()!="completed_with_results" || !document.GetProperty("limits").GetProperty("collect_declarations").GetBoolean())
        throw new InvalidDataException("Expected completed declaration-profile campaigns");
var repeats=replay.RootElement.GetProperty("rows").EnumerateArray().ToDictionary(Key,StringComparer.Ordinal);
var excluded=gate.GetProperty("excluded").EnumerateArray().Select(e=>e.GetProperty("key").GetString()!).ToHashSet(StringComparer.Ordinal);
var seen=new HashSet<string>(StringComparer.Ordinal);var rows=new List<object>();var outstanding=new List<object>();
var totals=new Dictionary<string,long>();var statuses=new Dictionary<string,int>();
var required=0;var catalogued=0;
string[] counters=["Scopes","Models","Subcircuits","ScopeParameters","AssignedModelParameters","Resources","DeferredContexts","Ambiguities","SelfLookups","SelfLookupFailures"];
foreach(var original in primary.RootElement.GetProperty("rows").EnumerateArray())
{
    var key=Key(original);if(!seen.Add(key))throw new InvalidDataException("Duplicate primary input identity");
    if(excluded.Contains(key))continue;required++;
    var result=original.GetProperty("result");
    if(result.GetProperty("Status").GetString()!="accepted_structure")
    {
        if(!repeats.TryGetValue(key,out var repeated) || result.GetProperty("Sha256").GetString() is not { } digest || digest!=repeated.GetProperty("result").GetProperty("Sha256").GetString())
        {outstanding.Add(new{key,status="missing_matching_replay"});continue;}
        result=repeated.GetProperty("result");
    }
    if(result.GetProperty("Status").GetString()!="accepted_structure" || !result.TryGetProperty("Declarations",out var catalog)||catalog.ValueKind!=JsonValueKind.Object)
    {outstanding.Add(new{key,status="missing_catalog"});continue;}
    var counts=new Dictionary<string,int>();
    foreach(var name in counters)
        if(!catalog.TryGetProperty(name,out var number)||!number.TryGetInt32(out var count)||count<0)throw new InvalidDataException("Invalid catalog counter: "+name);
        else counts[name]=count;
    var status=catalog.GetProperty("Status").GetString();
    if(status is not ("catalogued" or "ambiguous_declarations") || (status=="catalogued")!=(counts["Ambiguities"]==0)
        || counts["Scopes"]!=(long)counts["Subcircuits"]+1 || counts["SelfLookupFailures"]>counts["SelfLookups"]
        || counts["Ambiguities"]>Math.Max(0,(long)counts["Models"]+counts["Subcircuits"]-1)
        || counts["Models"]==0&&counts["AssignedModelParameters"]!=0
        || counts["SelfLookups"]<(long)counts["Models"]+counts["Subcircuits"]
        || counts["SelfLookups"]>(long)counts["Models"]+counts["Subcircuits"]+counts["ScopeParameters"])
        throw new InvalidDataException("Inconsistent catalog counts/status");
    if(counts["SelfLookupFailures"]!=0){outstanding.Add(new{key,status="self_lookup_inconsistency"});continue;}
    foreach(var (name,count) in counts){totals.TryGetValue(name,out var prior);totals[name]=checked(prior+count);}
    statuses.TryGetValue(status,out var priorStatus);statuses[status]=priorStatus+1;catalogued++;
    rows.Add(new{key,status,counts});
}
if(seen.Count!=gate.GetProperty("inventory_inputs").GetInt32() || required!=gate.GetProperty("required_inputs").GetInt32()
    || excluded.Any(key=>!seen.Contains(key)))throw new InvalidDataException("Acceptance cohort membership differs");
var complete=outstanding.Count==0&&catalogued==required;
var report=new{schema_version=1,status=complete?"all_required_declarations_catalogued":"outstanding_catalog_evidence",required,catalogued,
    excluded=excluded.Count,statuses,totals,rows,outstanding,primary_sha256=Hash(primaryPath),replay_sha256=Hash(replayPath),acceptance_sha256=Hash(acceptancePath),
    source_commit=primary.RootElement.GetProperty("source_commit"),source_dirty=primary.RootElement.GetProperty("source_dirty"),
    source_sha256=primary.RootElement.GetProperty("source_sha256"),binary_sha256=primary.RootElement.GetProperty("binary_sha256"),
    corpus_commit=primary.RootElement.GetProperty("corpus_commit"),corpus_dirty=primary.RootElement.GetProperty("corpus_dirty"),
    elapsed_seconds=primary.RootElement.GetProperty("elapsed_seconds"),platform=primary.RootElement.GetProperty("platform"),
    limitations=new[]{"Catalog export and self-index consistency only; not instance binding, parameter values, flattened topology or executable support.",
        "Unused and conditional declarations are included; ambiguity is a conservative catalog outcome, not a ngspice invalidity verdict.",
        "External resources/conditions defer lookup globally, including unrelated scopes; this is not qualified ngspice visibility semantics.",
        "Inputs, source and binary hashes are inherited from the completed worker campaign and syntax gate; raw input bodies remain local.",
        "Control/quoted languages stay opaque; no includes, library files or commands were loaded/executed."}};
File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{report.status,required,catalogued,outstanding=outstanding.Count,totals}));
if(!complete)throw new InvalidDataException("Declaration catalog evidence remains outstanding");
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static string Key(JsonElement row)=>row.GetProperty("scope").GetString()+"/"+row.GetProperty("path").GetString();
