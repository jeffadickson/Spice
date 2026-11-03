#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;

// Named original-source circuits, not a full corpus transient claim. Native and
// managed references retain their own integration methods; interpolation and the
// backward-Euler refinement error are recorded explicitly in SI units.
if(args is ["--self-test"]){Trace.CheckContracts();Console.WriteLine("Trace contracts passed");return;}
if(args.Length!=5)throw new ArgumentException("Expected Rust, ngspice, reference worker, patch manifest or '-', fresh output");
var engine=Path.GetFullPath(args[0]);var native=Path.GetFullPath(args[1]);var worker=Path.GetFullPath(args[2]);var patch=args[3]=="-"?null:Path.GetFullPath(args[3]);var output=Path.GetFullPath(args[4]);
foreach(var f in new[]{engine,native,worker})if(!File.Exists(f))throw new FileNotFoundException(f);
if(!Directory.Exists(Path.GetDirectoryName(output))||Directory.Exists(output)||patch!=null&&!File.Exists(patch))throw new IOException("Existing parents/inputs and fresh output required");
Directory.CreateDirectory(output);
var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,WriteIndented=true};
var identities=new[]{engine,native,worker}.Concat(patch==null?[]:new[]{patch}).ToDictionary(p=>p,Hash);
var cases=new[]{
 new Fixture("switch-rc-pulse","VDR in 0 PULSE(0 1 1m 100u 100u 1m 3m)\nVC control 0 0.5\nRCONTROL control 0 20k\nS1 in out control 0 sw\n.model sw VSWITCH(RON=100 ROFF=10000 VON=1 VOFF=0)\nRLOAD out 0 1k\nC1 out 0 1u",.004,5e-6,new[]{"out"},new[]{"VDR","VC"}),
 new Fixture("switch-control-ramp","VDR in 0 1\nVC control 0 PWL(0 0 1m 0 2m 1 3m 1 4m 0)\nRCONTROL control 0 20k\nS1 in out control 0 sw\n.model sw VSWITCH(RON=100 ROFF=10000 VON=1 VOFF=0)\nRLOAD out 0 1k\nC1 out 0 1u",.004,5e-6,new[]{"out"},new[]{"VDR","VC"}),
 new Fixture("delayed-phase-sine","VDR out 0 SIN(0 1 100 1m 0 90)\nRLOAD out 0 1k",.003,1e-5,new[]{"out"},new[]{"VDR"}),
 new Fixture("exponential-rc","VDR in 0 EXP(0 1 1m 200u 3m 300u)\nR1 in out 1k\nC1 out 0 1u\nRLOAD out 0 1k",.004,1e-5,new[]{"out"},new[]{"VDR"})
};
var results=new List<CaseResult>();
Dictionary<string,string>? referenceAssemblies=null;string? referenceFlavor=null;
foreach(var fixture in cases){
 var result=new CaseResult{CaseId=fixture.Name};results.Add(result);
 try{
  var folder=Path.Combine(output,fixture.Name);Directory.CreateDirectory(folder);
  var text=fixture.Name+"\n"+fixture.Body+"\n.end\n";var syntax=DeckParser.Parse(text,collectDeclarations:true);
  var probes=fixture.Nodes.Select(n=>new Probe("v_"+n,"voltage",n,"V")).Concat(fixture.Currents.Select(n=>new Probe("i_"+n,"current",n,"A"))).ToArray();
  result.SourceSha256=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
  var traces=new Dictionary<string,Trace>();
  foreach(var refine in new[]{1,2}){
   var step=fixture.Step/refine;var tag="h"+refine;
   var prepared=TransientSelection.Prepare(syntax,fixture.Stop,step,20000);
   if(!prepared.Success)throw new InvalidDataException(string.Join(',',prepared.Prepared.Diagnostics.Select(d=>d.Code)));
   var nativeDeck=Path.Combine(folder,tag+"-native.cir");var table=Path.Combine(folder,tag+"-native.txt");
   var expressions=probes.Select(p=>p.Kind=="voltage"?"v("+p.Target+")":"i("+p.Target+")");
   File.WriteAllText(nativeDeck,text[..text.LastIndexOf(".end",StringComparison.Ordinal)]+".control\nset wr_singlescale\nset wr_vecnames\nset numdgt=17\ntran "+F(step)+" "+F(fixture.Stop)+" 0 "+F(step)+"\nlinearize "+string.Join(' ',expressions)+"\nwrdata '"+table.Replace('\\','/')+"' "+string.Join(' ',expressions)+"\nquit\n.endc\n.end\n");
   var nativeExit=await Execute(native,["-n","-D","ngbehavior=psa","-b",nativeDeck],Path.Combine(folder,tag+"-native.log"));
   if(nativeExit!=0||!File.Exists(table))throw new InvalidDataException("Native trace unavailable");
   traces["ngspice_"+tag]=Trace.Native(table,probes.Length);
   var sharpDeck=Path.Combine(folder,tag+"-sharp.cir");File.WriteAllText(sharpDeck,text.Insert(text.LastIndexOf(".end",StringComparison.Ordinal),".tran "+F(step)+" "+F(fixture.Stop)+" 0 "+F(step)+"\n"));
   var job=Path.Combine(folder,tag+"-sharp.job.json");var sharpResult=Path.Combine(folder,tag+"-sharp.result.json");
   File.WriteAllText(job,JsonSerializer.Serialize(new{case_id=fixture.Name+tag,mode="transient",deck_path=sharpDeck,probes,patch_manifest=patch},json));
   var sharpExit=await Execute("dotnet",[worker,job,sharpResult],Path.Combine(folder,tag+"-sharp.log"));
   if(sharpExit!=0||!File.Exists(sharpResult))result.Unavailable.Add(tag+": SpiceSharp process unavailable");
   else{
    using var doc=JsonDocument.Parse(File.ReadAllText(sharpResult));var data=doc.RootElement;
    if(data.GetProperty("status").GetString()!="executed")result.Unavailable.Add(tag+": SpiceSharp "+string.Join(';',data.GetProperty("diagnostics").EnumerateArray().Select(v=>v.GetString())));
    else{
     if(data.GetProperty("schema_version").GetInt32()!=1||data.GetProperty("case_id").GetString()!=fixture.Name+tag||data.GetProperty("deck_sha256").GetString()!=Hash(sharpDeck)||data.GetProperty("mode").GetString()!="transient"||data.GetProperty("coordinate_unit").GetString()!="s"||data.GetProperty("patch_manifest_sha256").GetString()!=(patch==null?null:Hash(patch)))throw new InvalidDataException("Third-reference identity differs");
     var assemblies=data.GetProperty("assembly_hashes").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!);
     if(assemblies.Count!=3||new[]{"SpiceSharp","SpiceSharpParser","SpiceSharpBehavioral"}.Any(n=>!assemblies.TryGetValue(n,out var h)||h.Length!=64||h.Any(c=>!Uri.IsHexDigit(c))))throw new InvalidDataException("Reference assembly identities unavailable");
     var flavor=data.GetProperty("flavor").GetString();if(string.IsNullOrEmpty(flavor))throw new InvalidDataException("Reference flavor unavailable");
     if(referenceAssemblies==null){referenceAssemblies=assemblies;referenceFlavor=flavor;}
     else if(referenceAssemblies.Any(p=>assemblies[p.Key]!=p.Value)||flavor!=referenceFlavor)throw new InvalidDataException("Reference dependencies changed between jobs");
     result.ReferenceFlavor=data.GetProperty("flavor").GetString();
     traces["spicesharp_"+tag]=Trace.Sharp(data,probes);
    }
   }
   foreach(var backend in new[]{"lu","faer"}){
    var request=prepared.Prepared.Request! with{Backend=backend,CaseId=fixture.Name+tag};
    File.WriteAllText(Path.Combine(folder,tag+"-"+backend+".request.json"),JsonSerializer.Serialize(request,json));
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var response=await new SimulationClient(engine).RunAsync(request,deadline.Token);
    File.WriteAllText(Path.Combine(folder,tag+"-"+backend+".result.json"),response.Payload.GetRawText());
    if(!response.Succeeded)throw new InvalidDataException("Rust "+backend+" "+response.Error?.Code);
    var manifest=response.ReadManifest();var expected=backend=="lu"?"nalgebra_dense_lu_reference":"faer_sparse_lu";
    if(manifest.Backend.Id!=expected||manifest.CaseId!=request.CaseId||manifest.Backend.Gpu)throw new InvalidDataException("Wrong native provider/identity");
    var ids=probes.Select(p=>p.Kind=="voltage"?"v("+prepared.Prepared.RootNodes[p.Target]+")":"i("+prepared.Prepared.Origins.Single(o=>o.LocalName.Equals(p.Target,StringComparison.OrdinalIgnoreCase)&&o.InstancePath.Length==0).WireId+")").ToArray();
    traces[backend+"_"+tag]=Trace.Rust(response.Payload,ids,probes);
   }
  }
  // Compare on a named common grid. Independent references are piecewise-linearly
  // interpolated; no extrapolation or zero substitution is permitted.
  var grid=Enumerable.Range(0,101).Select(i=>fixture.Stop*i/100).ToArray();
  if(fixture.Currents.Contains("VC",StringComparer.Ordinal)){
   var p=Array.FindIndex(probes,p=>p.Target=="VC");
   // Independent control-load KCL, with ideal source current positive P-to-N.
   // The resistor is explicitly20k on every engine. Native's additional1TOhm
   // intrinsic loading is smaller than this separately stated0.1nA bound.
   double Control(double t)=>fixture.Name=="switch-rc-pulse"?.5:t<=.001?0:t<=.002?(t-.001)/.001:t<=.003?1:1-(t-.003)/.001;
   foreach(var provider in new[]{"lu","faer","ngspice","spicesharp"}){
    if(!traces.ContainsKey(provider+"_h2"))continue;
    double Error(string tag)=>grid.Max(t=>Math.Abs(traces[provider+tag].At(t,p)+Control(t)/20000));
    var coarse=Error("_h1");var fine=Error("_h2");
    result.Checks.Add(new(provider,"analytic_control_load",probes[p].Id,"A",coarse,fine,0,1e-10,fine<=1e-10));
   }
  }
  foreach(var provider in new[]{"lu","faer"})foreach(var reference in new[]{"ngspice","spicesharp"}){
   if(!traces.ContainsKey(reference+"_h2"))continue;
   for(var p=0;p<probes.Length;p++){
    double Error(string a,string b)=>grid.Max(t=>Math.Abs(traces[a].At(t,p)-traces[b].At(t,p)));
    var coarse=Error(provider+"_h1",reference+"_h2");var fine=Error(provider+"_h2",reference+"_h2");var delta=Error(provider+"_h1",provider+"_h2");
    var tolerance=probes[p].Unit=="V"?.012:1.2e-5;
    // These are finite fixture-specific bounds, not an automatic error estimator.
    result.Checks.Add(new(provider,reference,probes[p].Id,probes[p].Unit,coarse,fine,delta,tolerance,fine<=tolerance&&fine<=coarse+1e-10));
   }
  }
  // Shared-provider agreement is independently much tighter than discretization
  // tolerance. Check both voltages and P-to-N branch currents at every grid point.
  for(var p=0;p<probes.Length;p++){
   var error=grid.Max(t=>Math.Abs(traces["lu_h2"].At(t,p)-traces["faer_h2"].At(t,p)));
   var bound=probes[p].Unit=="V"?1e-8:1e-10;result.Checks.Add(new("lu","faer",probes[p].Id,probes[p].Unit,error,error,0,bound,error<=bound));
  }
  result.State=result.Checks.Any(c=>!c.Passed)?"numerical_mismatch":result.Unavailable.Count>0?"native_compared_third_unavailable":"three_way_compared";
 }catch(Exception e){result.State="failed";result.Unavailable.Add(e.GetType().Name+": "+e.Message);}
 File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{state="in_progress",cases=results},json));
}
if(identities.Any(p=>Hash(p.Key)!=p.Value))throw new InvalidDataException("Reference executable changed during run");
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{state="completed_with_results",identities,reference_assembly_sha256=referenceAssemblies,reference_flavor=referenceFlavor,cases=results,limits=new[]{"Four named fixtures; not original-corpus transient qualification.","Backward Euler at h and h/2; 101 shared samples with linear reference interpolation, no extrapolation.","Absolute V/A bounds are fixture-specific; refinement observed, no global accuracy proof.","Explicit20kOhm control resistor on all engines; native intrinsic1TOhm loading remains different from high-impedance Rust input.","Current sign is element positive-to-negative; source initialization is OP, no UIC/IC/control-program support.","Windows only; no performance, GPU or production qualification."}},json));
Console.WriteLine(JsonSerializer.Serialize(results.Select(r=>new{r.CaseId,r.State,checks=r.Checks.Count,r.Unavailable}),json));
Environment.ExitCode=results.Any(r=>r.State is "failed" or "numerical_mismatch")?1:results.Any(r=>r.Unavailable.Count>0)?3:0;
static string F(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
static string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
static async Task<int> Execute(string file,string[] arguments,string log){
 var start=new ProcessStartInfo(file){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in arguments)start.ArgumentList.Add(a);
 using var p=Process.Start(start)??throw new IOException("Process failed");var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
 try{await p.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){p.Kill(true);await p.WaitForExitAsync();}
 var text=await stdout+"\n"+await stderr;File.WriteAllText(log,text[..Math.Min(text.Length,1_048_576)]);return deadline.IsCancellationRequested?-1:p.ExitCode;
}
sealed record Fixture(string Name,string Body,double Stop,double Step,string[] Nodes,string[] Currents);
sealed record Probe(string Id,string Kind,string Target,string Unit);
sealed record Check(string Provider,string Reference,string Probe,string Unit,double CoarseError,double FineError,double RefinementDelta,double Bound,bool Passed);
sealed class CaseResult{public string CaseId{get;set;}="";public string State{get;set;}="not_executed";public string? SourceSha256{get;set;}public string? ReferenceFlavor{get;set;}public List<string> Unavailable{get;}=[];public List<Check> Checks{get;}=[];}
sealed class Trace(double[] time,double[][] values){
 public double At(double t,int p){if(t<time[0]-1e-12||t>time[^1]+1e-12)throw new InvalidDataException("Interpolation would extrapolate");var i=Array.BinarySearch(time,t);if(i>=0)return values[i][p];i=~i;if(i==0)return values[0][p];if(i==time.Length)return values[^1][p];var f=(t-time[i-1])/(time[i]-time[i-1]);return values[i-1][p]*(1-f)+values[i][p]*f;}
 static Trace Validate(double[] t,double[][] v,int n){if(t.Length<2||t.Length!=v.Length||t.Any(x=>!double.IsFinite(x))||t.Zip(t.Skip(1)).Any(x=>x.First>=x.Second)||v.Any(x=>x.Length!=n||x.Any(y=>!double.IsFinite(y))))throw new InvalidDataException("Invalid trace shape/ordering/finiteness");return new(t,v);}
 public static Trace Native(string path,int n){var rows=File.ReadLines(path).Skip(1).Where(l=>!string.IsNullOrWhiteSpace(l)).Select(l=>l.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray()).ToArray();if(rows.Any(r=>r.Length!=n+1))throw new InvalidDataException("Native trace column mismatch");return Validate(rows.Select(r=>r[0]).ToArray(),rows.Select(r=>r[1..]).ToArray(),n);}
 public static Trace Sharp(JsonElement d,Probe[] probes){var descriptors=d.GetProperty("probes").EnumerateArray().ToArray();if(descriptors.Length!=probes.Length)throw new InvalidDataException("Reference descriptor count");foreach(var p in probes){var x=descriptors.Single(x=>x.GetProperty("id").GetString()==p.Id);if(x.GetProperty("unit").GetString()!=p.Unit||x.GetProperty("target").GetString()!=p.Target||x.GetProperty("kind").GetString()!=p.Kind)throw new InvalidDataException("Reference descriptor differs");}var samples=d.GetProperty("samples").EnumerateArray().ToArray();return Validate(samples.Select(s=>s.GetProperty("coordinate").GetDouble()).ToArray(),samples.Select(s=>{var v=s.GetProperty("values");if(v.EnumerateObject().Count()!=probes.Length)throw new InvalidDataException("Reference vector length");return probes.Select(p=>{var x=v.GetProperty(p.Id);if(x.GetProperty("imag").GetDouble()!=0)throw new InvalidDataException("Complex real trace");return x.GetProperty("real").GetDouble();}).ToArray();}).ToArray(),probes.Length);}
 public static Trace Rust(JsonElement d,string[] ids,Probe[] probes){var signals=d.GetProperty("signals").EnumerateArray().ToArray();var indexes=ids.Select((id,i)=>{var ix=Array.FindIndex(signals,s=>s.GetProperty("id").GetString()==id);if(ix<0||signals[ix].GetProperty("unit").GetString()!=probes[i].Unit)throw new InvalidDataException("Rust observable absent/unit differs");return ix;}).ToArray();var samples=d.GetProperty("samples").EnumerateArray().ToArray();return Validate(samples.Select(s=>s.GetProperty("time_s").GetDouble()).ToArray(),samples.Select(s=>indexes.Select(i=>s.GetProperty("values")[i].GetDouble()).ToArray()).ToArray(),ids.Length);}
 public static void CheckContracts(){
  var t=Validate([0,1],[[0],[2]],1);if(t.At(.5,0)!=1)throw new Exception("Interpolation failed");
  void Reject(Action a){try{a();}catch(InvalidDataException){return;}throw new Exception("Malformed trace accepted");}
  Reject(()=>t.At(-.1,0));Reject(()=>t.At(1.1,0));Reject(()=>Validate([0,0],[[0],[1]],1));Reject(()=>Validate([1,0],[[0],[1]],1));Reject(()=>Validate([0,1],[[0],[double.NaN]],1));Reject(()=>Validate([0,1],[[0],[]],1));Reject(()=>Validate([0],[[0]],1));
 }
}
