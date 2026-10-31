using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Syntax;
using SlyTrial;

// This report compares fragments only. Wrapping a fragment for the existing
// full-deck API is explicit; no candidate input is normalized or evaluated.
if(args is not [var repository,var output] || !Directory.Exists(repository) ||
    !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
    throw new ArgumentException("Usage: <existing repository> <existing-parent>/report.json");
var watch=Stopwatch.StartNew();
var rows=Cases.All.Select(f=>
{
    Outcome? candidate=null;string? constructionFailure=null;
    try{candidate=f.Family switch{"port"=>Trial.ParsePort(f.Input),"expression"=>Trial.ParseExpression(f.Input),_=>Trial.ParseControl(f.Input)};}
    catch(Exception e){constructionFailure=e.GetType().Name+": "+e.Message;}
    var body=f.Family=="expression"?"B1 out 0 V={"+f.Input+"}":f.Input;
    var baseline=DeckParser.Parse("Trial\n"+body+"\n.end\n");
    bool? baselineAccepted=baseline.Status switch{"accepted_structure"=>true,"syntax_rejected"=>false,_=>null};
    return new{f.Id,f.Family,f.Scope,input=f.Input,expected=f.Expected,candidate_accepted=candidate?.Accepted,
        baseline_accepted=baselineAccepted,baseline_status=baseline.Status,
        candidate_matches_expectation=candidate is null?(bool?)null:candidate.Accepted==f.Expected,
        diagnostic=(constructionFailure??candidate?.Diagnostic) is { } error?error[..Math.Min(512,error.Length)]:null};
}).ToArray();
var positions=new[]{"a1 [first\r\n+ second] model","a1 [first\r\n* comment\r\n+ second] model"}.Select(input=>
{
    var token=Trial.PortTokens(input).Single(t=>t.Text=="second");
    var index=input.IndexOf("second",StringComparison.Ordinal);
    var physicalLines=input[..index].Replace("\r\n","\n").Replace('\r','\n').Split('\n');
    return new{input,token,expected_index=index,expected_zero_based_line=physicalLines.Length-1,
        offset_correct=token.Start==index,line_correct=token.Line==physicalLines.Length-1};
}).ToArray();
var project=Path.Combine(repository,"experiments/SlyTrial");
var hashes=Directory.GetFiles(project,"*",SearchOption.AllDirectories)
    .Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(s=>s is "bin" or "obj"))
    .ToDictionary(p=>Path.GetRelativePath(repository,p).Replace('\\','/'),Hash);
var shared=rows.Where(r=>r.Scope=="shared").ToArray();
var report=new{status="bounded_experiment_completed",package="sly",package_version="3.8.1",
    baseline="ANTLR generator/runtime 4.13.1",runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,elapsed_seconds=watch.Elapsed.TotalSeconds,
    source_sha256=hashes,baseline_binary_sha256=Hash(typeof(DeckParser).Assembly.Location),
    shared_cases=shared.Length,shared_expectations_met=shared.Count(r=>r.candidate_matches_expectation==true),
    construction_failures=rows.Count(r=>r.candidate_accepted is null),rows,physical_positions=positions,
    limitations=new[]{"Independent bounded fragment grammars, not a transferred complete SPICE frontend or a 924-input corpus replay.",
        "Challenge cases distinguish omitted productions from implemented contract boundaries; they are not a framework failure score.",
        "The candidate regex grammars and generic-mode probe are separate experiments, not a single integrated deck lexer.",
        "No numerical evaluation, command execution, include loading, model resolution, performance ranking, Linux or NativeAOT qualification.",
        "No production parser switch or Sly dependency outside the experiment."}};
File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{report.status,report.shared_cases,report.shared_expectations_met,report.construction_failures,positions}));
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
