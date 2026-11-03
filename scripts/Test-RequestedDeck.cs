#:property PublishAot=false
#:property TreatWarningsAsErrors=true
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

// End-to-end command smoke checks: existing compiled adapter and real native
// engine, no replacement numerical implementation or third-party test framework.
if(args.Length!=4)throw new ArgumentException("Expected runner DLL, frozen engine, fresh smoke output and example directory.");
var runner=Path.GetFullPath(args[0]);var engine=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);var examples=Path.GetFullPath(args[3]);
foreach(var f in new[]{runner,engine})if(!File.Exists(f))throw new FileNotFoundException(f);
foreach(var p in new[]{Path.GetDirectoryName(output)!,examples})if(!Directory.Exists(p))throw new DirectoryNotFoundException(p);
if(Directory.Exists(output)||File.Exists(output))throw new IOException("Fresh smoke output required.");Directory.CreateDirectory(output);
var inputs=Path.Combine(output,"inputs with spaces");Directory.CreateDirectory(inputs);var checks=new List<object>();
var help=await InvokeRaw(["--help"],Path.Combine(output,"help"));Require(help.Exit==0,"Help failed");checks.Add(new{name="help",exit=help.Exit,status="usage"});
var usage=await InvokeRaw([],Path.Combine(output,"usage"));Require(usage.Exit==2,"Missing arguments accepted");checks.Add(new{name="usage",exit=usage.Exit,status="refused_before_output_ownership"});
const string rlc="RLC execution\nV1 in 0 DC 0 AC 1 PULSE(0 1 10u 1u 1u 50u 100u)\nR1 in out 100\nL1 out cap 1m\nC1 cap 0 1u\nRLOAD cap 0 1k\n.op\n.ac lin 3 100 300\n.tran 10u 100u\n.end\n";
var rlcFile=Write("rlc.cir",rlc);var success=await Run("rlc",rlcFile,0,"executed");
foreach(var invalid in new[]{(Name:"missing-input",Input:Path.Combine(inputs,"absent.cir"),Engine:engine,Roots:Array.Empty<string>()),(Name:"missing-engine",Input:rlcFile,Engine:Path.Combine(output,"absent-engine.exe"),Roots:Array.Empty<string>()),(Name:"missing-allowed-root",Input:rlcFile,Engine:engine,Roots:new[]{Path.Combine(output,"absent-root")})}){
 var target=Path.Combine(output,invalid.Name);var rejected=await Invoke(invalid.Input,invalid.Engine,target,invalid.Roots);Require(rejected.Exit==2&&!Directory.Exists(target),"Invalid preflight claimed output ownership");checks.Add(new{name=invalid.Name,exit=rejected.Exit,status="refused_before_output_ownership"});
}
Require(success.GetProperty("operations").GetArrayLength()==3,"RLC operation count");
foreach(var operation in success.GetProperty("operations").EnumerateArray())Require(operation.GetProperty("points")[0].GetProperty("raw_response").ValueKind==JsonValueKind.String,"Raw RLC response missing");
var refusal=await Run("unknown-control",Write("unknown-control.cir","Unknown control\nV1 out 0 1\nR1 out 0 1k\n.control\nop\nshell echo must-not-run\n.endc\n.end\n"),1,"refused");
Require(refusal.GetProperty("operations").GetArrayLength()==0,"Unknown control executed");
Require(refusal.GetProperty("preparation_diagnostics").EnumerateArray().Any(d=>d.GetProperty("code").GetString()=="unsupported_control_command:shell"),"Unknown control-effect refusal not established");
var missing=await Run("missing-resource",Write("missing.cir","Missing resource\n.include missing.inc\nV1 out 0 1\nR1 out 0 1k\n.op\n.end\n"),1,"refused");
Require(missing.GetProperty("resource_diagnostics").GetArrayLength()>0,"Missing resource diagnostic");
var existing=Path.Combine(output,"existing-output");Directory.CreateDirectory(existing);var sentinel=Path.Combine(existing,"sentinel.txt");File.WriteAllText(sentinel,"preserve-me");var before=Hash(sentinel);
var existsResult=await Invoke(rlcFile,engine,existing,[]);Require(existsResult.Exit==2&&Hash(sentinel)==before&&Directory.GetFiles(existing).Length==1,"Existing output modified or accepted");checks.Add(new{name="existing-output",exit=existsResult.Exit,status="refused_before_output_ownership"});
await Run("singular",Write("singular.cir","Singular\nR1 a b 1k\n.op\n.end\n"),1,"failed");
var dc=await Run("dc",Write("dc.cir","Independent DC points\nV1 out 0 0\nR1 out 0 1k\n.dc V1 0 2 1\n.end\n"),0,"executed");
var dcPoints=dc.GetProperty("operations")[0].GetProperty("points").EnumerateArray().ToArray();Require(dcPoints.Length==3,"DC point count");
using(var plan=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"dc","plan.json"))))for(var i=0;i<3;i++){
 Require(dcPoints[i].GetProperty("original_point_ordinal").GetInt32()==i,"DC ordinal");Require(dcPoints[i].GetProperty("case_id").GetString()==plan.RootElement.GetProperty("operations")[0].GetProperty("requests")[i].GetProperty("case_id").GetString(),"Original DC case identity");
 using var raw=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"dc",dcPoints[i].GetProperty("raw_response").GetString()!)));var node=raw.RootElement.GetProperty("signals").EnumerateArray().Select((s,n)=>(s,n)).First(p=>p.s.GetProperty("quantity").GetString()=="voltage").n;Require(Math.Abs(raw.RootElement.GetProperty("samples")[0].GetProperty("values")[node].GetDouble()-i)<1e-12,"Independent DC voltage");
}
var ic=await Run("initial-window",Write("ic.cir","IC and reporting window\nV1 in 0 1\nR1 in out 1k\nC1 out 0 1u\n.ic V(out)=.2\n.tran .1m .5m .2m\n.end\n"),0,"executed");
var point=ic.GetProperty("operations")[0].GetProperty("points")[0];Require(point.GetProperty("initialization_response").ValueKind==JsonValueKind.String,"Bias response missing");
using(var raw=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"initial-window",point.GetProperty("raw_response").GetString()!))))using(var reported=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"initial-window",point.GetProperty("reported_manifest").GetString()!))))using(var request=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"initial-window",point.GetProperty("materialized_request").GetString()!)))){
 Require(raw.RootElement.GetProperty("samples")[0].GetProperty("time_s").GetDouble()==0,"Raw time0 missing");Require(reported.RootElement.GetProperty("samples")[0].GetProperty("time_s").GetDouble()>=.0002,"Reporting window not retained");Require(raw.RootElement.GetProperty("samples").GetArrayLength()>reported.RootElement.GetProperty("samples").GetArrayLength(),"Raw window was trimmed");Require(request.RootElement.GetProperty("analysis").GetProperty("options").GetProperty("initial_state").GetArrayLength()==3,"Materialized IC state missing/or includes clamp branch");
}
var partial=await Run("partial-dc",Write("partial.cir","Archive successful partial points without success label\nI1 0 out 0\nD1 out 0 dm\n.model dm D(IS=1p)\n.dc I1 -1m 1m 1m\n.end\n"),1,"failed");
var partialPoints=partial.GetProperty("operations")[0].GetProperty("points").EnumerateArray().ToArray();Require(partialPoints.Any(p=>p.GetProperty("status").GetString()=="executed")&&partialPoints.Any(p=>p.GetProperty("status").GetString()=="failed"),"Mixed original operation not exercised");Require(partialPoints.All(p=>p.GetProperty("raw_response").ValueKind==JsonValueKind.String),"Partial raw response discarded");
Write("local.inc","RLOCAL out 0 1k\n");await Run("local-include",Write("local.cir","Local include\n.include local.inc\nV1 out 0 1\n.op\n.end\n"),0,"executed");
var external=Path.Combine(output,"shared.inc");File.WriteAllText(external,"REXTERNAL out 0 1k\n");var externalDeck=Write("external.cir","Explicit extra allowed directory\n.include ../shared.inc\nV1 out 0 1\n.op\n.end\n");await Run("denied-include",externalDeck,1,"refused");await Run("explicit-include",externalDeck,0,"executed",[output]);
foreach(var name in new[]{"bjt-ebers-moll.cir","mos1-body-overlap.cir"})await Run(Path.GetFileNameWithoutExtension(name),Path.Combine(examples,name),0,"executed");
File.WriteAllText(Path.Combine(output,"verification.json"),JsonSerializer.Serialize(new{state="passed",checks,runner_sha256=Hash(runner),engine_sha256=Hash(engine),limitations=new[]{"Finite CLI/public-API smoke scope, Windows managed .NET10, no native oracle or numerical model qualification.","Timeout/user-cancellation and forced-disk failure not fault-injected; existing public client cancellation is reused.","No new mutation or broad fuzz campaign; negatives include resource/control/topology/output ownership and mixed-point failure."}},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"Requested deck runner: {checks.Count} smoke cases passed.");

string Write(string name,string text){if(!Directory.Exists(inputs))throw new DirectoryNotFoundException();var path=Path.Combine(inputs,name);File.WriteAllText(path,text);return path;}
async Task<JsonElement> Run(string name,string input,int expected,string status,string[]? roots=null){var target=Path.Combine(output,name);var result=await Invoke(input,engine,target,roots??[]);Require(result.Exit==expected,$"{name}: exit {result.Exit} vs {expected}: {result.Error}");using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(target,"summary.json")));var summary=document.RootElement.Clone();Require(summary.GetProperty("status").GetString()==status,$"{name}: wrong status");Require(summary.GetProperty("qualification").GetString()=="not_performed","Execution mislabeled qualification");Require(!Directory.GetFiles(target,"summary.*.tmp").Any(),"Atomic summary temporary remains");checks.Add(new{name,exit=result.Exit,status});return summary;}
async Task<(int Exit,string Error)> Invoke(string input,string executable,string target,string[] roots)=>await InvokeRaw([input,executable,target,..roots],target);
async Task<(int Exit,string Error)> InvokeRaw(string[] arguments,string target){var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{runner}.Concat(arguments))start.ArgumentList.Add(arg);using var process=Process.Start(start)??throw new IOException("Runner did not start");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(150));try{await process.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}var text=await stdout;var error=await stderr;var log=target+".log";File.WriteAllText(log,text);File.WriteAllText(log+".stderr",error);return(process.ExitCode,error);}
static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
