#:project ../dotnet/Simulator.Syntax/Simulator.Syntax.csproj
#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Simulator.Syntax;

// Executes only the owned named fixtures below; product parsing never executes
// controls or invokes ngspice. All generated files require an explicit output.
if(args.Length!=3)throw new ArgumentException("Expected repository, ngspice executable and fresh output directory");
var root=Path.GetFullPath(args[0]);var executable=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
if(!Directory.Exists(root)||!File.Exists(executable)||!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException("Missing input or output parent");
if(Directory.Exists(output))throw new IOException("Use a fresh oracle directory");Directory.CreateDirectory(output);
var observations=new List<object>();var lastExitCode=0;
var fixture=Path.Combine(root,"compat/ngspice/semantic-parameters.cir");
var catalog=DeckParser.Parse(File.ReadAllText(fixture),collectDeclarations:true).Declarations??throw new InvalidDataException("Fixture syntax");
var parameters=ParameterEvaluator.Evaluate(catalog);if(!parameters.Success)throw new InvalidDataException("Fixture parameters");
var call=InstanceBinder.Bind(catalog).Instances.Single();var local=ParameterEvaluator.ForInstance(catalog,call,parameters);
if(!local.Success)throw new InvalidDataException("Fixture overrides");
var expected=new Dictionary<string,double>{{"milli",parameters.Values["milli"]},{"mega",parameters.Values["mega"]},{"chain",parameters.Values["chain"]},{"unary",parameters.Values["unary"]},{"logic",parameters.Values["logic"]},{"choice",parameters.Values["choice"]},{"out",local.Values["resistance"]/(1000+local.Values["resistance"])}};
Compare("parameters",fixture,expected);
var nested=Path.Combine(root,"compat/ngspice/semantic-nested.cir");
var nc=DeckParser.Parse(File.ReadAllText(nested),collectDeclarations:true).Declarations!;var calls=InstanceBinder.Bind(nc).Instances;
var outer=ParameterEvaluator.ForInstance(nc,calls[0],ParameterEvaluator.Evaluate(nc));var inner=ParameterEvaluator.ForInstance(nc,calls[1],outer);
if(!inner.Success)throw new InvalidDataException("Nested parameters");Compare("nested",nested,new(){{"nested",inner.Values["derived"]}});
var ordered=Path.Combine(root,"compat/ngspice/semantic-port-order.cir");
var oc=DeckParser.Parse(File.ReadAllText(ordered),collectDeclarations:true).Declarations!;var ob=InstanceBinder.Bind(oc).Instances.Single();
var ov=ParameterEvaluator.ForInstance(oc,ob,ParameterEvaluator.Evaluate(oc));
var pNode=ob.Ports.Single(p=>p.Formal!.Text=="p").Actual.Text;var nNode=ob.Ports.Single(p=>p.Formal!.Text=="n").Actual.Text;
var sign=pNode=="out"&&nNode=="0"?1:pNode=="0"&&nNode=="out"?-1:throw new InvalidDataException("Unexpected port fixture map");
Compare("port-order",ordered,new(){{"portorder",sign*ov.Values["voltage"]}});
var zero=Path.Combine(root,"compat/ngspice/semantic-zero-port.cir");
var zc=DeckParser.Parse(File.ReadAllText(zero),collectDeclarations:true).Declarations!;var zb=InstanceBinder.Bind(zc);
if(!zb.Complete||zb.Instances.Single().Ports.Length!=0)throw new InvalidDataException("Zero-port binding");Compare("zero-port",zero,new(){{"zero",1}});
foreach(var (name,body,wantedStatus) in new[]{("arity","X1 a cell\n.subckt cell p n\nR1 p n 1k\n.ends",BindingStatus.Invalid),("missing","X1 a b absent",BindingStatus.Missing)})
{
    var text="Negative reference\nV1 a 0 1\n"+body+"\n.op\n.end\n";var path=Path.Combine(output,name+".cir");File.WriteAllText(path,text);
    var bc=DeckParser.Parse(text,collectDeclarations:true).Declarations!;
    if(InstanceBinder.Bind(bc).Instances.Single().Status!=wantedStatus)throw new InvalidDataException("Expected binding rejection");
    var log=Path.Combine(output,name+".log");Run(["-n","-b","-o",log,path],true);
    observations.Add(new{name,expected_binding=wantedStatus.ToString(),actual_ngspice_exit=lastExitCode,input_sha256=Hash(path),log_sha256=Hash(log)});
}
var resources=Path.Combine(output,"resources");Directory.CreateDirectory(resources);
var main=Path.Combine(resources,"main.cir");
File.WriteAllText(main,"Resource oracle\n.include 'settings.inc'\n.lib 'cells.lib' typical\nV1 input 0 1\nR1 input out 1k\nX1 out 0 load resistance={base*1k}\n.control\nset numdgt=17\nop\necho SEMANTIC_resource $&v(out)\nquit\n.endc\n.end\n");
File.WriteAllText(Path.Combine(resources,"settings.inc"),".param base=3\n");
File.WriteAllText(Path.Combine(resources,"cells.lib"),".lib unused\n.include 'unused.inc'\n.endl unused\n.lib typical\n.subckt load p n resistance=1k\nRload p n {resistance}\n.ends\n.endl typical\n");
File.WriteAllText(Path.Combine(resources,"unused.inc"),".param unused_only=777\n");
var resolved=ResourceResolver.Resolve(main,new([resources]));if(!resolved.Success)throw new InvalidDataException(JsonSerializer.Serialize(resolved.Diagnostics));
var rc=DeckParser.Parse(resolved.Text!,collectDeclarations:true).Declarations!;var rb=InstanceBinder.Bind(rc);var rp=ParameterEvaluator.ForInstance(rc,rb.Instances.Single(),ParameterEvaluator.Evaluate(rc));
if(!rp.Success)throw new InvalidDataException("Resolved parameters");var wanted=rp.Values["resistance"]/(1000+rp.Values["resistance"]);
Compare("resources-original",main,new(){{"resource",wanted}});
var expanded=Path.Combine(output,"expanded.cir");File.WriteAllText(expanded,resolved.Text,new UTF8Encoding(false));Compare("resources-expanded",expanded,new(){{"resource",wanted}});
var version=Run(["--version"]);
var hashes=Directory.EnumerateFiles(resources).ToDictionary(p=>Path.GetFileName(p),Hash);
var report=new{status="passed",oracle="ngspice process; analytical resistor divider and generated-tree parameter values",ngspice_sha256=Hash(executable),ngspice_version=version,
 observations,source_files=resolved.Sources.Select(s=>new{s.Path,s.Section,s.Sha256}),resource_inputs=hashes,
 source_sha256=new[]{"dotnet/Simulator.Syntax/InstanceBinding.cs","dotnet/Simulator.Syntax/InstanceExtraction.cs","dotnet/Simulator.Syntax/ParameterEvaluator.cs","dotnet/Simulator.Syntax/ResourceResolver.cs","dotnet/Simulator.Syntax/DeclarationCatalog.cs","dotnet/Simulator.Syntax/DeclarationExtraction.cs","dotnet/Simulator.Syntax/Grammar/SpiceParser.g4","scripts/QualifySemanticFrontend.cs"}.ToDictionary(p=>p,p=>Hash(Path.Combine(root,p))),
 limitations=new[]{"Named finite semantic profiles, not full SPICE/model or Rust numerical qualification","Windows execution; no Linux/reparse-point filesystem race qualification","Product path never executes the trusted fixture control commands","Observed ngspice opens includes in unselected library sections; this resolver intentionally leaves them unopened"}};
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.status,comparisons=observations.Count,output}));
void Compare(string name,string path,Dictionary<string,double> values)
{
    var log=Path.Combine(output,name+".log");Run(["-n","-b","-o",log,path]);var text=File.ReadAllText(log);
    foreach(var pair in values)
    {
        var matches=Regex.Matches(text,@"(?m)^SEMANTIC_"+Regex.Escape(pair.Key)+@"\s+([-+0-9.eE]+)\s*$");
        if(matches.Count!=1)throw new InvalidDataException("Missing/ambiguous independent reference: "+pair.Key);
        var actual=double.Parse(matches[0].Groups[1].Value,CultureInfo.InvariantCulture);
        var tolerance=1e-12+1e-12*Math.Abs(pair.Value);
        if(!double.IsFinite(actual)||Math.Abs(actual-pair.Value)>tolerance)throw new InvalidDataException($"{name}/{pair.Key}: expected {pair.Value:R}, actual {actual:R}");
        observations.Add(new{name,signal=pair.Key,expected=pair.Value,actual,tolerance,input_sha256=Hash(path),log_sha256=Hash(log)});
    }
}
string Run(string[] arguments,bool requireFailure=false)
{
    var info=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,WorkingDirectory=output};foreach(var a in arguments)info.ArgumentList.Add(a);
    using var process=Process.Start(info)??throw new IOException("Could not launch oracle");var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    if(!process.WaitForExit(30000)){process.Kill(true);process.WaitForExit();throw new TimeoutException("ngspice reference exceeded30s");}
    Task.WaitAll(stdout,stderr);lastExitCode=process.ExitCode;if(requireFailure?process.ExitCode==0:process.ExitCode!=0)throw new IOException($"ngspice exit {process.ExitCode}: {stderr.Result}");return stdout.Result+stderr.Result;
}
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
