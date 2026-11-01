#:property PublishAot=false
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

// Synthetic corruption tests exercise the catalog evidence gate, not samples
// or simulator behavior. No metadata is interpreted as a command.
if(args is not [var repository,var artifacts] || !Directory.Exists(repository)||!Directory.Exists(artifacts))
    throw new ArgumentException("Usage: <existing repository> <existing artifacts>");
var root=Path.Combine(artifacts,"gate-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var passed=0;
foreach(var test in new[]{"valid","missing_payload","self_failure","wrong_profile","wrong_digest","bad_counts","duplicate_input","impossible_ambiguities","orphan_model_parameters"})
{
    var counts=JsonSerializer.SerializeToNode(new{Status="catalogued",Scopes=1,Models=1,Subcircuits=0,ScopeParameters=0,
        AssignedModelParameters=0,Resources=0,DeferredContexts=0,Ambiguities=0,SelfLookups=1,SelfLookupFailures=0})!;
    var row=new JsonObject{["scope"]="ngspice",["path"]="synthetic.cir",["result"]=new JsonObject{["Status"]="accepted_structure",["Sha256"]="owned-fixture",["Declarations"]=counts}};
    var primary=Report(new JsonArray(row));var replay=Report(new JsonArray());
    if(test=="missing_payload")row["result"]!["Declarations"]=null;
    if(test=="self_failure")counts["SelfLookupFailures"]=1;
    if(test=="wrong_profile")primary["limits"]!["collect_declarations"]=false;
    if(test=="bad_counts")counts["Scopes"]=2;
    if(test=="impossible_ambiguities"){counts["Status"]="ambiguous_declarations";counts["Ambiguities"]=1;}
    if(test=="orphan_model_parameters"){counts["Models"]=0;counts["SelfLookups"]=0;counts["AssignedModelParameters"]=1;}
    if(test=="duplicate_input")primary["rows"]!.AsArray().Add(row.DeepClone());
    var p=Path.Combine(root,test+"-primary.json");var r=Path.Combine(root,test+"-replay.json");var a=Path.Combine(root,test+"-acceptance.json");var output=Path.Combine(root,test+"-result.json");
    File.WriteAllText(p,primary.ToJsonString());File.WriteAllText(r,replay.ToJsonString());
    var acceptance=new JsonObject{["status"]="all_declared_valid_inputs_accepted",["primary_sha256"]=test=="wrong_digest"?"changed":Hash(p),
        ["replay_sha256"]=new JsonObject{["replay.json"]=Hash(r)},["excluded"]=new JsonArray(),["inventory_inputs"]=1,["required_inputs"]=1};
    File.WriteAllText(a,acceptance.ToJsonString());
    var start=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    foreach(var argument in new[]{"run","--file",Path.Combine(repository,"scripts/ValidateDeclarationCatalog.cs"),"--",p,r,a,output})start.ArgumentList.Add(argument);
    using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(120));
    try{await process.WaitForExitAsync(budget.Token);}catch(OperationCanceledException){process.Kill(true);throw new TimeoutException(test);}
    var log=await stdout+await stderr;File.WriteAllText(Path.Combine(root,test+".log"),log);
    if(test=="valid")
    {
        if(process.ExitCode!=0)throw new IOException("Valid fixture failed: "+log);
        using var result=JsonDocument.Parse(File.ReadAllText(output));
        if(result.RootElement.GetProperty("catalogued").GetInt32()!=1)throw new InvalidDataException("Valid fixture count");
    }
    else
    {
        if(process.ExitCode==0)throw new InvalidDataException("Corrupt report accepted: "+test);
        var expected=test switch{"missing_payload" or "self_failure"=>"Declaration catalog evidence remains outstanding", "wrong_profile"=>"Expected completed declaration-profile", "wrong_digest"=>"Syntax acceptance report does not match", "duplicate_input"=>"Duplicate primary input identity",_=>"Inconsistent catalog counts/status"};
        if(!log.Contains(expected,StringComparison.Ordinal))throw new InvalidDataException("Wrong refusal for "+test+": "+log);
    }
    passed++;
}
File.WriteAllText(Path.Combine(root,"summary.json"),JsonSerializer.Serialize(new{passed,failed=0,scope="Synthetic gate fixtures only"}));
Console.WriteLine($"Declaration gate: {passed} synthetic corruption/valid cases passed. Artifacts: {root}");

static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static JsonObject Report(JsonArray rows)=>new(){["state"]="completed_with_results",["limits"]=new JsonObject{["collect_declarations"]=true},["rows"]=rows,
    ["source_commit"]="synthetic",["source_dirty"]=false,["source_sha256"]=new JsonObject{["fixture"]="synthetic"},["binary_sha256"]=new JsonObject{["fixture"]="synthetic"},
    ["corpus_commit"]="synthetic",["corpus_dirty"]=false,["elapsed_seconds"]=0,["platform"]="synthetic"};
