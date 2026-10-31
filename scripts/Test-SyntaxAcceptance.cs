#:property PublishAot=false
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

// Synthetic report corruption tests exercise the evidence gate, never the
// sample parser. They cannot be substituted for a real corpus campaign.
if(args is not [var repository,var scratch] || !Directory.Exists(repository) || !Directory.Exists(scratch))
    throw new ArgumentException("Usage: <repository> <existing scratch directory>");
var root=Path.Combine(scratch,"acceptance-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var inventory=Path.Combine(root,"inventory.json");File.WriteAllText(inventory,"{\"rows\":[{\"path\":\"synthetic.cir\"}]}");
var catalog=Path.Combine(root,"catalog.json");File.WriteAllText(catalog,"{\"entries\":[]}");
var git=new ProcessStartInfo("git"){RedirectStandardOutput=true,CreateNoWindow=true};
foreach(var a in new[]{"-C",repository,"ls-files","-z","--","*.cir","*.sp","*.spice","*.ckt","*.deck","*.net"})git.ArgumentList.Add(a);
using var listing=Process.Start(git)!;var paths=await listing.StandardOutput.ReadToEndAsync();await listing.WaitForExitAsync();
if(listing.ExitCode!=0)throw new IOException("Git inventory failed");
var rows=new JsonArray();rows.Add(Row("ngspice","synthetic.cir"));
foreach(var path in paths.Split('\0',StringSplitOptions.RemoveEmptyEntries))rows.Add(Row("repository",path));
var report=new JsonObject{["state"]="completed_with_results",["inputs"]=rows.Count,["completed"]=rows.Count,
    ["inventory_sha256"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(inventory))),
    ["source_sha256"]=new JsonObject{["grammar"]="synthetic-source"},
    ["binary_sha256"]=new JsonObject{["parser"]="synthetic-binary"},["rows"]=rows};
await Check("positive",report,0);
var changed=(JsonObject)report.DeepClone();changed["state"]="running";await Check("unfinished",changed,1);
changed=(JsonObject)report.DeepClone();changed["inventory_sha256"]="wrong";await Check("wrong-inventory",changed,1);
changed=(JsonObject)report.DeepClone();((JsonArray)changed["rows"]!).RemoveAt(0);changed["inputs"]=rows.Count-1;changed["completed"]=rows.Count-1;await Check("truncated-membership",changed,1);
changed=(JsonObject)report.DeepClone();changed["rows"]![0]!["result"]!["Status"]="grammar_gap";await Check("unexplained-gap",changed,2);
// A matching replay can supply acceptance; changing either provenance map
// must prevent exactly the same apparent success.
var replay=Path.Combine(root,"replay.json");File.WriteAllText(replay,report.ToJsonString());
await Check("matching-replay",changed,0,replay);
var other=(JsonObject)report.DeepClone();other["binary_sha256"]!["parser"]="different";File.WriteAllText(replay,other.ToJsonString());
await Check("different-binary",changed,1,replay);
other=(JsonObject)report.DeepClone();other["source_sha256"]!["grammar"]="different";File.WriteAllText(replay,other.ToJsonString());
await Check("different-source",changed,1,replay);
var exception=new JsonObject{["scope"]="ngspice",["path"]="synthetic.cir",["disposition"]="invalid_syntax_fixture",
    ["input_sha256"]=new string('a',64),["expected_primary_status"]="syntax_rejected",["evidence"]="Synthetic negative-test fixture only"};
var exceptions=new JsonObject{["entries"]=new JsonArray(exception)};
File.WriteAllText(catalog,exceptions.ToJsonString());
changed=(JsonObject)report.DeepClone();changed["rows"]![0]!["result"]!["Status"]="syntax_rejected";
await Check("pinned-exclusion",changed,0);
exception["input_sha256"]=new string('b',64);File.WriteAllText(catalog,exceptions.ToJsonString());
await Check("changed-exception-bytes",changed,2);
exception["input_sha256"]=new string('a',64);exception["disposition"]="legacy_encoded_deck";exception["expected_primary_status"]="encoding_rejected";
File.WriteAllText(catalog,exceptions.ToJsonString());changed["rows"]![0]!["result"]!["Status"]="encoding_rejected";
File.WriteAllText(replay,report.ToJsonString());await Check("legacy-without-explicit-encoding",changed,2,replay);
other=(JsonObject)report.DeepClone();other["rows"]![0]!["result"]!["Encoding"]="latin1-byte-preserving";File.WriteAllText(replay,other.ToJsonString());
await Check("explicit-legacy-replay",changed,0,replay);
Console.WriteLine("Passed 12 synthetic acceptance-gate scenarios; raw logs: "+root);

static JsonObject Row(string scope,string path)=>new(){["scope"]=scope,["path"]=path,["result"]=new JsonObject{
    ["Status"]="accepted_structure",["Sha256"]=new string('a',64),["Encoding"]="utf-8"}};
async Task Check(string name,JsonObject data,int expected,string? replayPath=null)
{
    var input=Path.Combine(root,name+".json");File.WriteAllText(input,data.ToJsonString());
    var start=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    foreach(var a in new[]{"run","--file",Path.Combine(repository,"scripts/ValidateSyntaxAcceptance.cs"),"--",input,catalog,inventory,repository,Path.Combine(root,name+"-result.json")})start.ArgumentList.Add(a);
    if(replayPath is not null)start.ArgumentList.Add(replayPath);
    using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
    File.WriteAllText(input+".log",await stdout+await stderr);
    // Unhandled validation exceptions have a runtime-specific nonzero code.
    if(expected==1?process.ExitCode is 0 or 2:process.ExitCode!=expected)throw new InvalidDataException(name+" unexpected exit "+process.ExitCode);
}
