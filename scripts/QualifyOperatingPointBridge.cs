#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Simulator.Syntax;
using Simulator.Frontend;
using Simulator.Client;

if(args.Length!=4)throw new ArgumentException("Expected repository, Rust executable, ngspice executable and fresh output directory");
var root=Path.GetFullPath(args[0]);var engine=Path.GetFullPath(args[1]);var ngspice=Path.GetFullPath(args[2]);var output=Path.GetFullPath(args[3]);
if(!Directory.Exists(root)||!File.Exists(engine)||!File.Exists(ngspice)||!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException("Missing inputs/output parent");
if(Directory.Exists(output))throw new IOException("Use a fresh output directory");Directory.CreateDirectory(output);
// Fingerprint domain source, grammar, dependency pins and this harness, plus the
// actual loaded managed assemblies. A commit ID alone cannot identify dirty work.
var sourceFiles=new[]{"Simulator.Syntax","Simulator.Client","Simulator.Frontend"}
    .SelectMany(project=>Directory.EnumerateFiles(Path.Combine(root,"dotnet",project)).Where(p=>p.EndsWith(".cs")||p.EndsWith(".csproj")||p.EndsWith(".json")))
    .Concat(Directory.EnumerateFiles(Path.Combine(root,"dotnet/Simulator.Syntax/Grammar"),"*.g4"))
    .Concat(new[]{Path.Combine(root,"scripts/QualifyOperatingPointBridge.cs"),Path.Combine(root,"global.json")}).ToArray();
var sourceHashes=sourceFiles.ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash);
var assemblyHashes=new[]{typeof(OperatingPointBridge).Assembly,typeof(DeckParser).Assembly,typeof(SimulationClient).Assembly}
    .ToDictionary(a=>a.GetName().Name!,a=>Hash(a.Location));
var cases=new[]{
 (Name:"flat",Body:".param r=1k\nV1 in 0 3\nRseries in out 1k\nRload out 0 {r*4}\n.op",Signal:"out",Expected:2.4),
 (Name:"hierarchy",Body:".include 'settings.inc'\n.lib 'cells.lib' typical\nV1 in 0 3\nRseries in out 1k\nX1 out 0 load gain=3\n.op",Signal:"out",Expected:2.4),
 (Name:"signed-voltage",Body:"V1 a 0 DC -2\nR1 a 0 1k\n.op",Signal:"a",Expected:-2.0),
 (Name:"signed-current",Body:"I1 a 0 1m\nR1 a 0 1k\n.op",Signal:"a",Expected:-1.0)};
var observations=new List<object>();var client=new SimulationClient(engine);
foreach(var item in cases)
{
    var folder=Path.Combine(output,item.Name);Directory.CreateDirectory(folder);
    var source="Bridge qualification\n"+item.Body+"\n.end\n";var input=Path.Combine(folder,"input.cir");File.WriteAllText(input,source);
    File.WriteAllText(Path.Combine(folder,"settings.inc"),".param r=1k\n");
    File.WriteAllText(Path.Combine(folder,"cells.lib"),".lib typical\n.subckt load p n gain=2\nR1 p private {r*gain}\nR2 private n {r}\n.ends\n.endl typical\n");
    var resolved=ResourceResolver.Resolve(input,new([folder]));if(!resolved.Success)throw new InvalidDataException(JsonSerializer.Serialize(resolved.Diagnostics));
    var syntax=DeckParser.Parse(resolved.Text!,collectDeclarations:true);
    var reference=Path.Combine(folder,"reference.cir");File.WriteAllText(reference,source.Replace(".end\n",".control\nset numdgt=17\nop\necho BRIDGE_VALUE $&v("+item.Signal+")\nquit\n.endc\n.end\n",StringComparison.Ordinal));
    var log=Path.Combine(folder,"ngspice.log");RunNg(reference,log);
    var matches=Regex.Matches(File.ReadAllText(log),@"(?m)^BRIDGE_VALUE\s+([-+0-9.eE]+)\s*$");if(matches.Count!=1)throw new InvalidDataException("Reference signal unavailable");
    var oracle=double.Parse(matches[0].Groups[1].Value,CultureInfo.InvariantCulture);Check(oracle,item.Expected,"analytical/ngspice");
    foreach(var provider in new[]{"lu","qr","faer"})
    {
        var prepared=OperatingPointBridge.Lower(syntax,backend:provider);if(!prepared.Success)throw new InvalidDataException(JsonSerializer.Serialize(prepared.Diagnostics));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response=await client.RunAsync(prepared.Request!,timeout.Token);if(!response.Succeeded)throw new InvalidDataException(JsonSerializer.Serialize(response.Error));
        var actual=response.FinalValue("v("+prepared.RootNodes[item.Signal]+")");Check(actual,oracle,"text/Rust/ngspice");
        var manual=item.Name is "flat" or "hierarchy"?new Circuit(["0","in","out"],[new Voltage("V1",1,0,3),new Resistor("R1",1,2,1000),new Resistor("R2",2,0,4000)]):
            item.Name=="signed-voltage"?new Circuit(["0","a"],[new Voltage("V1",1,0,-2),new Resistor("R1",1,0,1000)]):
            new Circuit(["0","a"],[new Current("I1",1,0,.001),new Resistor("R1",1,0,1000)]);
        var independent=await client.RunAsync(new("structured",manual,new OperatingPoint(),provider),timeout.Token);
        if(!independent.Succeeded)throw new InvalidDataException("Structured solve failed");
        var structured=independent.FinalValue("v("+item.Signal+")");Check(actual,structured,"text/structured");
        File.WriteAllText(Path.Combine(folder,provider+".result.json"),response.Payload.GetRawText());
        observations.Add(new{item.Name,provider,actual,structured,ngspice=oracle,analytical=item.Expected,nodes=prepared.Request!.Circuit.Nodes.Length,elements=prepared.Request.Circuit.Elements.Length,sources=resolved.Sources.Length,input_sha256=Hash(input),log_sha256=Hash(log)});
    }
}
foreach(var pair in sourceHashes)if(Hash(Path.Combine(root,pair.Key))!=pair.Value)throw new IOException("Source changed during qualification");
var report=new{status="passed",observations,engine_sha256=Hash(engine),ngspice_sha256=Hash(ngspice),source_commit=Git(root),
 source_sha256=sourceHashes,managed_assembly_sha256=assemblyHashes,
 limits=new[]{"R/V/I constant DC operating point only; no model/AC/transient bridge qualification","Typed public API; no legacy numerical importer or handwritten card parser","Named Windows CPU fixtures only; no whole-SPICE/GPU/Linux/production qualification"}};
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.status,comparisons=observations.Count,solves=observations.Count*2,output}));
void Check(double actual,double expected,string name){if(!double.IsFinite(actual)||Math.Abs(actual-expected)>1e-10+1e-10*Math.Abs(expected))throw new InvalidDataException($"{name}: expected {expected:R}, actual {actual:R}");}
void RunNg(string input,string log)
{
 var start=new ProcessStartInfo(ngspice){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(input)!};foreach(var a in new[]{"-n","-b","-o",log,input})start.ArgumentList.Add(a);
 using var p=Process.Start(start)!;var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(30000)){p.Kill(true);p.WaitForExit();throw new TimeoutException("ngspice");}Task.WaitAll(stdout,stderr);if(p.ExitCode!=0)throw new IOException("ngspice failed: "+stderr.Result);
}
static string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
static string Git(string root){var s=new ProcessStartInfo("git"){RedirectStandardOutput=true,UseShellExecute=false};foreach(var a in new[]{"-C",root,"rev-parse","HEAD"})s.ArgumentList.Add(a);using var p=Process.Start(s)!;var t=p.StandardOutput.ReadToEnd();p.WaitForExit();return t.Trim();}
