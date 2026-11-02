#:project ../dotnet/Simulator.Syntax/Simulator.Syntax.csproj
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Simulator.Syntax;

// Named independent probes distinguish node spellings from arithmetic values.
// ngspice is only a test oracle; the frontend remains I/O/simulation independent.
if(args.Length is not (3 or 4))throw new ArgumentException("Expected checkout, ngspice executable, fresh output directory, optional corpus root");
var repository=args[0];var executable=args[1];var destination=args[2];
var corpus=args.Length==4?Path.GetFullPath(args[3]):null;
if(corpus!=null&&!Directory.Exists(corpus))throw new DirectoryNotFoundException(corpus);
var root=Path.GetFullPath(repository);var engine=Path.GetFullPath(executable);var output=Path.GetFullPath(destination);
if(!Directory.Exists(root)||!File.Exists(engine)||!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException("Missing inputs or output parent");
if(Directory.Exists(output))throw new IOException("Output directory must be fresh");Directory.CreateDirectory(output);
var sourceFiles=Directory.EnumerateFiles(Path.Combine(root,"dotnet/Simulator.Syntax")).Where(p=>p.EndsWith(".cs")||p.EndsWith(".csproj")||p.EndsWith("packages.lock.json"))
    .Concat(Directory.EnumerateFiles(Path.Combine(root,"dotnet/Simulator.Syntax/Grammar"),"*.g4"))
    .Concat(new[]{Path.Combine(root,"scripts/QualifyVoltageReferences.cs"),Path.Combine(root,"global.json")}).ToArray();
var hashes=sourceFiles.ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash);
var fixtures=new[]{
 (Name:"differential",Expression:"V(VC+,VC-)",Expected:2.0),
 (Name:"single",Expression:"v(VC+)",Expected:3.0),
 (Name:"nested",Expression:"max(V(VC+,VC-)*2,1)",Expected:4.0),
 (Name:"spaced",Expression:"v ( VC+ , VC- )",Expected:2.0),
 (Name:"numeric-node",Expression:"V(2p)",Expected:2.25),
 (Name:"bus-node",Expression:"V(bus[3],ref:minus)",Expected:2.0),
 (Name:"path-node",Expression:"V(path/a,path-b)",Expected:2.5),
 (Name:"quoted-node",Expression:"V(\"VC+\",\"VC-\")",Expected:2.0),
 (Name:"limited",Expression:"LIMIT(.5*V(VC+,VC-),-1,1)",Expected:1.0),
 (Name:"continued",Expression:"V(VC+\n+ ,VC-)",Expected:2.0),
 (Name:"continued-open",Expression:"V\n+ (VC+,VC-)",Expected:2.0)};
var rows=new List<object>();var syntaxAccepted=0;var nativeMatched=0;var nativeAccepted=0;
foreach(var fixture in fixtures)
{
    var folder=Path.Combine(output,fixture.Name);Directory.CreateDirectory(folder);
    var source="Node reference qualification\nV1 VC+ 0 3\nV2 VC- 0 1\nV3 2p 0 2.25\nV4 bus[3] 0 2.5\nV5 ref:minus 0 .5\nV6 path/a 0 4\nV7 path-b 0 1.5\nB1 out 0 V={"+fixture.Expression+"}\nRload out 0 1k\n";
    var parserInput=Path.Combine(folder,"syntax.cir");File.WriteAllText(parserInput,source+".end\n");
    var parsed=DeckParser.Parse(source+".end\n",collectFeatures:true,collectDeclarations:true);
    if(parsed.Status=="accepted_structure")syntaxAccepted++;
    var input=Path.Combine(folder,"reference.cir");File.WriteAllText(input,source+".control\nset numdgt=17\nop\necho NODE_VALUE $&v(out)\nquit\n.endc\n.end\n");
    var log=Path.Combine(folder,"ngspice.log");var exitCode=Run(input,log);
    var logText=File.ReadAllText(log);
    var nativeErrors=Regex.IsMatch(logText,@"(?im)^\s*(?:error\b|fatal\b|syntax error\b)");
    var matches=Regex.Matches(logText,@"(?m)^NODE_VALUE\s+([-+0-9.eE]+)\s*$");
    double? actual=matches.Count==1?double.Parse(matches[0].Groups[1].Value,CultureInfo.InvariantCulture):null;
    var accepted=exitCode==0&&!nativeErrors&&actual is {} finite&&double.IsFinite(finite);
    if(accepted)nativeAccepted++;
    var native=accepted&&actual is {} number&&Math.Abs(number-fixture.Expected)<=1e-10+1e-10*Math.Abs(fixture.Expected);
    if(native)nativeMatched++;
    rows.Add(new{fixture.Name,fixture.Expression,expected=fixture.Expected,actual,native_accepted=accepted,native_matched=native,native_errors_detected=nativeErrors,native_exit=exitCode,syntax_status=parsed.Status,
        diagnostics=parsed.Diagnostics.Take(3),input_sha256=Hash(parserInput),native_input_sha256=Hash(input),log_sha256=Hash(log)});
}
// Reached-library recognition is a separate profile: no includes, controls or
// native simulation of vendor inputs. Paths are relative to an explicit corpus.
var vendorRows=new List<object>();var vendorAccepted=0;
var vendorPaths=new[]{"examples/optran/models/opa1611.lib","examples/optran/models/TLV6001.LIB","examples/optran/models/TLV9002.lib",
    "examples/p-to-n-examples/MCP6041.txt","examples/p-to-n-examples/OPA171.txt","examples/p-to-n-examples/TLC555.LIB",
    "examples/probe/TL072.301","examples/probe/TLC555.LIB","paranoia_parallel/examples/optran/models/opa1611.lib",
    "paranoia_parallel/examples/optran/models/TLV6001.LIB","paranoia_parallel/examples/optran/models/TLV9002.lib",
    "paranoia_parallel/examples/pton/MCP6041.txt","paranoia_parallel/examples/pton/OPA171.txt","paranoia_parallel/examples/pton/TLC555.LIB"};
if(corpus!=null)foreach(var relative in vendorPaths)
{
    var path=Path.GetFullPath(relative,corpus);
    if(!File.Exists(path)){vendorRows.Add(new{path=relative,status="input_unavailable"});continue;}
    var raw=File.ReadAllBytes(path);if(raw.Length>4_194_304){vendorRows.Add(new{path=relative,status="byte_budget"});continue;}
    var text=new System.Text.UTF8Encoding(false,true).GetString(raw).TrimStart('\uFEFF');
    var parsed=DeckParser.Parse("Vendor fragment\n"+text,new(4_194_304,2_000_000),collectDeclarations:true);
    if(parsed.Status=="accepted_structure")vendorAccepted++;
    vendorRows.Add(new{path=relative,status=parsed.Status,sha256=Convert.ToHexStringLower(SHA256.HashData(raw)),
        diagnostics=parsed.Diagnostics.Take(3).Select(d=>d with {Line=d.Line>0?d.Line-1:0,Message=d.Message[..Math.Min(512,d.Message.Length)]})});
}
foreach(var pair in hashes)if(Hash(Path.Combine(root,pair.Key))!=pair.Value)throw new IOException("Source changed during qualification");
// This is a syntax gate. A completed native OP independently establishes
// acceptance; numeric mismatches are retained separately and never made passes.
var status=syntaxAccepted==fixtures.Length&&nativeAccepted==fixtures.Length?"syntax_agreement":"syntax_gap";
var numericalStatus=nativeMatched==fixtures.Length?"matched":"mismatch_observed";
var summary=new{status,numerical_status=numericalStatus,fixtures=fixtures.Length,syntax_accepted=syntaxAccepted,native_accepted=nativeAccepted,native_matched=nativeMatched,rows,source_sha256=hashes,
    ngspice_sha256=Hash(engine),syntax_assembly_sha256=Hash(typeof(DeckParser).Assembly.Location),
    vendor_profile=corpus==null?"not_requested":"recognition_only",vendor_expected=corpus==null?0:vendorPaths.Length,vendor_accepted=vendorAccepted,vendor_rows=vendorRows,
    limitations=new[]{"Named voltage-reference probes only; not complete ngspice expression dialect conformance","Native B-source execution is an oracle, not Rust behavioral-source or signal-parameter support","Managed file-app; Windows execution only; no whole-corpus simulation, Linux/GPU/production qualification"}};
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{status,numericalStatus,syntaxAccepted,nativeAccepted,nativeMatched,fixtures=fixtures.Length,output}));
if(status!="syntax_agreement")Environment.ExitCode=2;
int Run(string input,string log)
{
    var start=new ProcessStartInfo(engine){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(input)!};
    foreach(var item in new[]{"-n","-b","-o",log,input})start.ArgumentList.Add(item);
    using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    if(!process.WaitForExit(30000)){process.Kill(true);process.WaitForExit();throw new TimeoutException("ngspice reference exceeded 30s");}Task.WaitAll(stdout,stderr);
    File.WriteAllText(log+".process.txt",stdout.Result+stderr.Result);return process.ExitCode;
}
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
