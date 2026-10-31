#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;

// Completion gate for a fixed sample inventory. Exception entries are pinned to
// input bytes and expected outcomes; unexplained failures cannot become skips.
if(args is not [var primaryPath,var catalogPath,var inventoryPath,var repository,var output,..var replayPaths] ||
    !Directory.Exists(repository) ||
    !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
    throw new ArgumentException("Usage: <completed primary JSON> <adjudications JSON> <inventory JSON> <repository root> <output JSON> [completed replay JSON...]");
using var primary=JsonDocument.Parse(File.ReadAllText(primaryPath));
using var catalog=JsonDocument.Parse(File.ReadAllText(catalogPath));
using var inventory=JsonDocument.Parse(File.ReadAllText(inventoryPath));
RequireComplete(primary.RootElement);
if(primary.RootElement.GetProperty("inventory_sha256").GetString()!=Hash(inventoryPath))
    throw new InvalidDataException("Primary does not identify the authoritative inventory");
var expected=inventory.RootElement.GetProperty("rows").EnumerateArray()
    .Select(row=>"ngspice/"+row.GetProperty("path").GetString()).ToHashSet(StringComparer.Ordinal);
var git=new ProcessStartInfo("git"){RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
foreach(var argument in new[]{"-C",repository,"ls-files","-z","--","*.cir","*.sp","*.spice","*.ckt","*.deck","*.net"})git.ArgumentList.Add(argument);
using(var process=Process.Start(git)!)
{
    var text=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException(await error);
    foreach(var path in (await text).Split('\0',StringSplitOptions.RemoveEmptyEntries))expected.Add("repository/"+path);
}
if(!expected.SetEquals(primary.RootElement.GetProperty("rows").EnumerateArray().Select(Key)))
    throw new InvalidDataException("Primary membership differs from authoritative upstream/repository inventory");
var source=Fingerprint(primary.RootElement,"source_sha256");
var binaries=Fingerprint(primary.RootElement,"binary_sha256");
var replayDocuments=replayPaths.Select(p=>JsonDocument.Parse(File.ReadAllText(p))).ToArray();
var replayRows=new List<JsonElement>();
foreach(var document in replayDocuments)
{
    RequireComplete(document.RootElement);
    if(Fingerprint(document.RootElement,"source_sha256")!=source || Fingerprint(document.RootElement,"binary_sha256")!=binaries)
        throw new InvalidDataException("Replay parser source differs from primary campaign");
    replayRows.AddRange(document.RootElement.GetProperty("rows").EnumerateArray());
}
var entries=catalog.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(Key,StringComparer.Ordinal);
var outstanding=new List<object>();var excluded=new List<object>();
var required=0;var accepted=0;var replayAccepted=0;
var seen=new HashSet<string>(StringComparer.Ordinal);
foreach(var row in primary.RootElement.GetProperty("rows").EnumerateArray())
{
    var key=Key(row);if(!seen.Add(key))throw new InvalidDataException("Duplicate primary input");
    var result=row.GetProperty("result");var status=result.GetProperty("Status").GetString();
    var sha=result.GetProperty("Sha256").GetString();
    entries.TryGetValue(key,out var entry);
    var disposition=entry.ValueKind==JsonValueKind.Undefined?null:entry.GetProperty("disposition").GetString();
    if(disposition is not null)
    {
        if(sha!=entry.GetProperty("input_sha256").GetString() || status!=entry.GetProperty("expected_primary_status").GetString())
        {
            required++;outstanding.Add(new{key,status,reason="adjudication_bytes_or_expected_outcome_changed"});continue;
        }
        if(disposition is "non_deck_document" or "invalid_syntax_fixture")
        {
            excluded.Add(new{key,disposition,evidence=entry.GetProperty("evidence").GetString()});continue;
        }
        if(disposition!="legacy_encoded_deck")throw new InvalidDataException("Unknown adjudication disposition");
    }
    required++;
    if(status=="accepted_structure"){accepted++;continue;}
    // A missing hash never licenses a replay substitution. A fresh primary
    // attempt with identified bytes is required for such unavailable evidence.
    var match=sha is null?null:replayRows.Where(r=>Key(r)==key &&
        r.GetProperty("result").GetProperty("Sha256").GetString()==sha &&
        r.GetProperty("result").GetProperty("Status").GetString()=="accepted_structure" &&
        (disposition!="legacy_encoded_deck" ||
            (r.GetProperty("result").TryGetProperty("Encoding",out var encoding) &&
             encoding.GetString()=="latin1-byte-preserving"))).Cast<JsonElement?>().FirstOrDefault();
    if(match.HasValue){accepted++;replayAccepted++;continue;}
    outstanding.Add(new{key,status,reason="valid_input_acceptance_not_demonstrated"});
}
if(entries.Keys.Any(key=>!seen.Contains(key)))throw new InvalidDataException("Adjudication entry outside primary inventory");
var summary=new{status=outstanding.Count==0?"all_declared_valid_inputs_accepted":"outstanding_acceptance_gaps",
    inventory_inputs=seen.Count,required_inputs=required,accepted,accepted_via_explicit_replay=replayAccepted,excluded,outstanding,
    primary_sha256=Hash(primaryPath),catalog_sha256=Hash(catalogPath),replay_sha256=replayPaths.ToDictionary(p=>Path.GetFileName(p),Hash),
    limitations=new[]{"Structural acceptance only; embedded command/quoted languages and numerical support are separate contracts.",
        "Exception metadata is human-adjudicated against source evidence; new input bytes invalidate an exception."}};
File.WriteAllText(output,JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{summary.status,required,accepted,outstanding=outstanding.Count}));
foreach(var document in replayDocuments)document.Dispose();
if(outstanding.Count>0)Environment.ExitCode=2;

static string Key(JsonElement row)=>row.GetProperty("scope").GetString()+"/"+row.GetProperty("path").GetString();
static void RequireComplete(JsonElement report)
{
    if(report.GetProperty("state").GetString()!="completed_with_results" ||
        report.GetProperty("completed").GetInt32()!=report.GetProperty("inputs").GetInt32() ||
        report.GetProperty("rows").GetArrayLength()!=report.GetProperty("inputs").GetInt32())
        throw new InvalidDataException("Incomplete campaigns cannot close acceptance");
}
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static string Fingerprint(JsonElement report,string property)=>string.Join("\n",report.GetProperty(property).EnumerateObject()
    .OrderBy(pair=>pair.Name,StringComparer.Ordinal).Select(pair=>pair.Name+":"+pair.Value.GetString()));
