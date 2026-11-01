#:property PublishAot=false
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Stryker performs mutation and execution. This helper only reconciles stable
// source/location/operator/replacement identities across its two JSON reports.
if(args.Length is not (3 or 4) || !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(args[2]))))
    throw new ArgumentException("Usage: <original Stryker JSON> <replay Stryker JSON> <output with existing parent> [focused Stryker JSON]");
var originalPath=args[0];var replayPath=args[1];var output=args[2];
var original=Read(originalPath);var replay=Read(replayPath);
var lookup=replay.ToDictionary(m=>m.Identity,StringComparer.Ordinal);
var focused=args.Length==4?Read(args[3]):[];
var focusLookup=focused.ToDictionary(m=>m.Identity,StringComparer.Ordinal);
var transitions=original.Where(m=>m.Status is "Survived" or "NoCoverage" or "Timeout").Select(m=>
{
    var found=lookup.TryGetValue(m.Identity,out var repeated);
    var status=found?repeated!.Status:"not_replayed";var stage="broad_replay";
    // A later executed identity can refine an open result. Unselected/compile
    // outcomes cannot erase earlier evidence, and raw scope counts stay separate.
    if(status!="Killed"&&focusLookup.TryGetValue(m.Identity,out var targeted)&&targeted.Status is "Killed" or "Survived" or "NoCoverage" or "Timeout")
    {status=targeted.Status;stage="focused_replay";}
    return new{m.Identity,m.File,m.Id,m.Line,m.Column,m.Mutator,m.Replacement,original_status=m.Status,
        replay_status=status,replay_stage=stage,disposition=status=="Killed"?"detected_by_strengthened_tests":"review_needed"};
}).ToArray();
var report=new{framework="Stryker.NET 5.0.0",original_report_sha256=Hash(originalPath),replay_report_sha256=Hash(replayPath),
    focused_report_sha256=args.Length==4?Hash(args[3]):null,
    original_counts=Counts(original),replay_counts=Counts(replay),focused_counts=Counts(focused),targeted_original_identities=transitions.Length,
    replay_killed=transitions.Count(t=>t.replay_status=="Killed"),transitions,
    limitations=new[]{"Scopes differ: do not add campaign scores or relabel skipped/compile-error outcomes as kills.",
        "Timeouts remain separate, even if Stryker's displayed score treats them as detected.",
        "Conditional-equivalence/diagnostic dispositions require manual review; review_needed is not closure.",
        "Generated grammar productions and third-party ANTLR code were not mutation-tested."}};
File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Reconciled {transitions.Length} original open identities; {report.replay_killed} killed in replay.");

static Dictionary<string,int> Counts(IEnumerable<Mutation> rows)=>rows.GroupBy(m=>m.Status).ToDictionary(g=>g.Key,g=>g.Count());
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static Mutation[] Read(string path)
{
    using var document=JsonDocument.Parse(File.ReadAllText(path));var result=new List<Mutation>();
    foreach(var file in document.RootElement.GetProperty("files").EnumerateObject())
    {
        var name=Path.GetFileName(file.Name.Replace('\\','/'));
        var sourceHash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(file.Value.GetProperty("source").GetString()!)));
        foreach(var mutant in file.Value.GetProperty("mutants").EnumerateArray())
        {
            var location=mutant.GetProperty("location");var start=location.GetProperty("start");var end=location.GetProperty("end");
            var line=start.GetProperty("line").GetInt32();var column=start.GetProperty("column").GetInt32();
            var mutator=mutant.GetProperty("mutatorName").GetString()!;var replacement=mutant.GetProperty("replacement").GetString()!;
            var identity=JsonSerializer.Serialize(new{file=name,sourceHash,line,column,endLine=end.GetProperty("line").GetInt32(),endColumn=end.GetProperty("column").GetInt32(),mutator,replacement});
            identity=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            result.Add(new(identity,name,mutant.GetProperty("id").GetString()!,line,column,mutator,replacement,mutant.GetProperty("status").GetString()!));
        }
    }
    return result.ToArray();
}
internal sealed record Mutation(string Identity,string File,string Id,int Line,int Column,string Mutator,string Replacement,string Status);
