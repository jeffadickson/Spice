using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.RequestedQualification;
using Simulator.Syntax;

// Qualification-only, closed-form affine gain/KCL oracle. It never edits decks,
// product code, frozen responses or the primary cohort classification.
if(args.Length!=3)throw new ArgumentException("Expected retained cohort, fresh output, frozen engine.");
var cohort=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);var engine=Path.GetFullPath(args[2]);
foreach(var dir in new[]{cohort,Path.GetDirectoryName(output)!})if(!Directory.Exists(dir))throw new DirectoryNotFoundException(dir);
if(Directory.Exists(output)||File.Exists(output))throw new IOException("Fresh output required.");
if(!File.Exists(engine))throw new FileNotFoundException(engine);
Directory.CreateDirectory(output);
var json=new JsonSerializerOptions {WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
var contracts=SelfTests();
var frozenHashes=new Dictionary<string,string>{{Path.Combine(cohort,"summary.json"),Hash(Path.Combine(cohort,"summary.json"))},{engine,Hash(engine)}};
foreach(var assembly in new[]{typeof(DeckParser).Assembly,typeof(RequestedAnalysis).Assembly,typeof(AsciiRaw).Assembly,typeof(SimulationRequest).Assembly}.Distinct())frozenHashes.Add(assembly.Location,Hash(assembly.Location));
using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(cohort,"summary.json")));
var root=summary.RootElement;var expectedEngine=root.GetProperty("source_hashes").EnumerateObject().Single(p=>p.Name.EndsWith("circuit-simulator.exe",StringComparison.OrdinalIgnoreCase)).Value.GetString();
if(Hash(engine)!=expectedEngine)throw new InvalidDataException("Frozen cohort engine hash mismatch.");
var cases=new List<object>();var excluded=new List<string>();var inventory=root.GetProperty("rows").EnumerateArray().ToArray();
for(var rowIndex=0;rowIndex<inventory.Length;rowIndex++) {
 var row=inventory[rowIndex];var operations=row.GetProperty("operations").EnumerateArray().Where(o=>o.GetProperty("status").GetString()=="numerical_disagreement").ToArray();
 if(operations.Length==0)continue;
 var caseFolder=Path.Combine(cohort,"cases",rowIndex.ToString("D4"));
 foreach(var path in new[]{Path.Combine(caseFolder,"expanded.cir"),Path.Combine(caseFolder,"requested_0","00000.request.json"),Path.Combine(caseFolder,"requested_0","00000.response.json"),Path.Combine(caseFolder,"requested_0","native.raw")})frozenHashes.Add(path,Hash(path));
 var requestPath=Path.Combine(caseFolder,"requested_0","00000.request.json");
 var request=JsonSerializer.Deserialize<SimulationRequest>(File.ReadAllText(requestPath),WireFormat.Options)??throw new InvalidDataException("Missing typed request.");
 if(request.Circuit.Elements.Any(e=>e is not (Voltage or Resistor or Vcvs))){excluded.Add(row.GetProperty("path").GetString()!);continue;}
 if(operations.Length!=1)throw new InvalidDataException("Expected one original transient operation.");
 var deck=File.ReadAllText(Path.Combine(caseFolder,"expanded.cir"));
 if(Hash(Path.Combine(caseFolder,"expanded.cir"))!=row.GetProperty("expanded_sha256").GetString())throw new InvalidDataException("Frozen expanded input changed.");
 var plan=RequestedAnalysis.Prepare(DeckParser.Parse(deck,collectDeclarations:true));
 if(!plan.Success||plan.Operations.Length!=1||plan.CircuitPreparation is not {} prepared)throw new InvalidDataException("Generated metadata unavailable.");
 var operation=plan.Operations[0];
 if(!JsonNode.DeepEquals(JsonNode.Parse(JsonSerializer.Serialize(operation.Requests.Single(),WireFormat.Options)),JsonNode.Parse(File.ReadAllText(requestPath))))throw new InvalidDataException("Generated request differs from retained wire payload.");
 var oracle=new AffineOracle(request);
 var responsePath=Path.Combine(caseFolder,"requested_0","00000.response.json");var rawPath=Path.Combine(caseFolder,"requested_0","native.raw");
 using var responseJson=JsonDocument.Parse(File.ReadAllText(responsePath));var payload=responseJson.RootElement.Clone();
 var response=new SimulationResponse(payload.GetProperty("schema_version").GetString()!,payload.GetProperty("status").GetString()!,payload.GetProperty("complete").GetBoolean(),payload);
 var manifest=response.ReadManifest();var raw=AsciiRaw.Read(File.ReadAllText(rawPath));
 if(Hash(rawPath)!=operations[0].GetProperty("native_raw_sha256").GetString())throw new InvalidDataException("Retained RAW changed.");
 var mappings=Mappings(prepared,raw);var nativeTimes=raw.Points.Select(p=>p[0].Real).ToArray();var engineTimes=manifest.Samples.Select(p=>p.TimeS).ToArray();
 CheckAxis(nativeTimes);CheckAxis(engineTimes);
 var ownEngine=OwnGrid(manifest,oracle);var ownNative=new List<object>();var interpolation=new List<object>();
 foreach(var map in mappings) {
  var n=raw.Variables.FindIndex(v=>v.Name.Equals(map.Native,StringComparison.OrdinalIgnoreCase));
  var e=Array.FindIndex(manifest.Signals,s=>s.Id==map.Engine);if(n<0||e<0)throw new InvalidDataException("Missing mapped signal.");
  var exact=oracle.Signal(map.Engine);var metric=new Metric();foreach(var point in raw.Points)metric.Add(point[n].Real,exact.At(oracle.Source(point[0].Real)),map.Unit);
  ownNative.Add(new {map.Engine,map.Native,map.Unit,metric});
  var primary=new Metric();var pureGrid=new Metric();double residual=0;
  foreach(var point in manifest.Samples) {
   var actual=point.Values[e];var nativeInterpolated=Interpolate(nativeTimes,raw.Points.Select(p=>p[n].Real).ToArray(),point.TimeS);
   var exactHere=exact.At(oracle.Source(point.TimeS));var analyticInterpolated=Interpolate(nativeTimes,nativeTimes.Select(t=>exact.At(oracle.Source(t))).ToArray(),point.TimeS);
   primary.Add(actual,nativeInterpolated,map.Unit);pureGrid.Add(exactHere,analyticInterpolated,map.Unit);
   residual=Math.Max(residual,Math.Abs((actual-nativeInterpolated)-(exactHere-analyticInterpolated)));
  }
  var original=operations[0].GetProperty("comparison").GetProperty("observables").EnumerateArray().Single(o=>o.GetProperty("engine_signal").GetString()==map.Engine);
  if(primary.Failed!=original.GetProperty("failed").GetInt32()||Math.Abs(primary.MaximumAbsoluteError-original.GetProperty("maximum_absolute_error").GetDouble())>1e-12)throw new InvalidDataException("Original interpolation outcome not reproduced.");
  interpolation.Add(new {map.Engine,map.Native,map.Unit,primary,pure_grid=pureGrid,decomposition_residual=residual});
 }
 var shared=Shared(manifest,raw,mappings);var pocFolder=Path.Combine(output,rowIndex.ToString("D4"));Directory.CreateDirectory(pocFolder);
 var engineLaws=oracle.LawResiduals(manifest,prepared);var nativeLaws=oracle.NativeLawResiduals(raw,prepared,mappings);
 // A zero-current source changes no circuit equation. Its explicit zero-valued
 // PWL knots exercise the EXISTING source scheduler only, preserving TMAX/budgets.
 var configuration=((TransientConfigured)request.Analysis).Options;const string schedulerId="__zero_grid_scheduler";
 if(request.Circuit.Elements.Any(e=>e.Id==schedulerId))throw new InvalidDataException("Scheduler ID collision.");
 var neutral=new Current(schedulerId,request.Circuit.Elements[0].P,request.Circuit.Elements[0].N,0);
 var scheduler=new SourceOverride(schedulerId,new Pwl(nativeTimes.Select(t=>new Point(t,0)).ToArray()));
 var poc=request with {Circuit=request.Circuit with {Elements=[..request.Circuit.Elements,neutral]},Analysis=new TransientConfigured(configuration with {Sources=[..configuration.Sources!,scheduler]})};
 File.WriteAllText(Path.Combine(pocFolder,"scheduler.request.json"),JsonSerializer.Serialize(poc,WireFormat.Options));
 using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));var pocResponse=await new SimulationClient(engine).RunAsync(poc,deadline.Token);
 File.WriteAllText(Path.Combine(pocFolder,"scheduler.response.json"),pocResponse.Payload.GetRawText());if(!pocResponse.Succeeded)throw new InvalidDataException(pocResponse.Payload.GetRawText());
 var pm=pocResponse.ReadManifest();var pocShared=Shared(pm,raw,mappings);var extra=pm.Samples.Where(s=>!nativeTimes.Contains(s.TimeS)).Select(s=>s.TimeS).ToArray();
 var missingNative=nativeTimes.Where(t=>!pm.Samples.Any(s=>s.TimeS==t)).Select(t=>{var nearest=pm.Samples.MinBy(s=>Math.Abs(s.TimeS-t))!;return new{native_time_s=t,nearest_engine_time_s=nearest.TimeS,delta_s=nearest.TimeS-t,ulps=Math.Abs(BitConverter.DoubleToInt64Bits(nearest.TimeS)-BitConverter.DoubleToInt64Bits(t))};}).ToArray();
 var fullPocComparison=RequestedComparison.Compare(operation with {Requests=[poc]},prepared,[pocResponse],raw);
 cases.Add(new {path=row.GetProperty("path").GetString(),row_index=rowIndex,primary_status_unchanged=operations[0].GetProperty("status").GetString(),source=oracle.Waveform,
  metadata_request_identical=true,native_points=raw.Points.Length,engine_points=manifest.Samples.Length,own_engine=ownEngine,own_native=ownNative,engine_law_residuals=engineLaws,native_law_residuals=nativeLaws,interpolation,exact_shared_original=shared,
  scheduler=new {physics_delta="One existing-node zero-amp Current plus all-zero PWL at native times; no unknowns, original elements/waveforms/method/TMAX/stop/max_steps unchanged",original_configuration=configuration,poc_configuration=((TransientConfigured)poc.Analysis).Options,points=pm.Samples.Length,native_knots_shared=pocShared,missing_bit_exact_native_knots=missingNative,extra_engine_knots=extra,own_grid=OwnGrid(pm,oracle),full_engine_grid_vs_original_raw=fullPocComparison,raw_directory=pocFolder},
  input_hashes=new Dictionary<string,string>{{requestPath,Hash(requestPath)},{responsePath,Hash(responsePath)},{rawPath,Hash(rawPath)},{Path.Combine(caseFolder,"expanded.cir"),Hash(Path.Combine(caseFolder,"expanded.cir"))}}});
}
if(cases.Count!=10||excluded.Count!=1||!excluded.Single().EndsWith("qualification-rc-ramp.cir",StringComparison.Ordinal))throw new InvalidDataException("Cohort selection drift.");
foreach(var entry in frozenHashes)if(Hash(entry.Key)!=entry.Value)throw new InvalidDataException("Frozen input changed during diagnosis: "+entry.Key);
var assemblies=new[]{typeof(DeckParser).Assembly,typeof(RequestedAnalysis).Assembly,typeof(AsciiRaw).Assembly,typeof(SimulationRequest).Assembly}.Distinct().ToDictionary(a=>a.Location,a=>Hash(a.Location));
File.WriteAllText(Path.Combine(output,"diagnosis.json"),JsonSerializer.Serialize(new {state="completed",source_commit=root.GetProperty("source_commit").GetString(),cohort,summary_sha256=Hash(Path.Combine(cohort,"summary.json")),engine,engine_sha256=Hash(engine),metadata_assemblies=assemblies,frozen_input_hashes_rechecked=frozenHashes,self_tests=contracts,cases,excluded,
 limitations=new[]{"Primary classifications and tolerances are unchanged.","Closed-form qualification is restricted to generated V/R/VCVS stateless topology with one undamped sine; no new input parser or MNA solver.","Exact shared-knot comparisons are explicitly subsets; extra engine knots are retained and checked independently, never silently dropped from primary comparison.","Neutral-source POC adds scheduler breakpoints; full engine-grid comparison is separately reported and may still disagree from interpolation.","Managed .NET10 helper, not NativeAOT; no library/product migration, no new production dependency."}},json));
Console.WriteLine($"Diagnosed {cases.Count} stateless cases; excluded {excluded.Single()}; output {output}");
return;

static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static void CheckAxis(double[] axis){if(axis.Length<2||axis.Any(v=>!double.IsFinite(v))||axis.Zip(axis.Skip(1)).Any(p=>p.First>=p.Second))throw new InvalidDataException("Invalid grid.");}
static double Interpolate(double[] axis,double[] values,double t){if(t<axis[0]||t>axis[^1])throw new InvalidDataException("No extrapolation.");var i=Array.BinarySearch(axis,t);if(i>=0)return values[i];var right=~i;var left=right-1;return values[left]+(t-axis[left])/(axis[right]-axis[left])*(values[right]-values[left]);}
static object[] OwnGrid(ResultManifest manifest,AffineOracle oracle)=>manifest.Signals.Select((signal,i)=>{var expression=oracle.Signal(signal.Id);var metric=new Metric();foreach(var sample in manifest.Samples)metric.Add(sample.Values[i],expression.At(oracle.Source(sample.TimeS)),signal.Unit);return (object)new{signal.Id,signal.Unit,metric};}).ToArray();
static (string Engine,string Native,string Unit)[] Mappings(BridgeResult prepared,NativeRaw raw){var result=new List<(string,string,string)>();foreach(var node in prepared.RootNodes)result.Add(("v("+node.Value+")","v("+node.Key+")","V"));foreach(var origin in prepared.Origins.Where(o=>o.NativeBranchName!=null&&o.InstancePath.IsEmpty))result.Add(("i("+origin.WireId+")","i("+origin.NativeBranchName+")","A"));if(result.Count!=raw.Variables.Length-1)throw new InvalidDataException("Unmapped native variables.");return result.ToArray();}
static object Shared(ResultManifest manifest,NativeRaw raw,(string Engine,string Native,string Unit)[] maps){var exact=raw.Points.Where(p=>manifest.Samples.Any(s=>s.TimeS==p[0].Real)).ToArray();var metrics=new List<object>();foreach(var map in maps){var c=raw.Variables.FindIndex(v=>v.Name.Equals(map.Native,StringComparison.OrdinalIgnoreCase));var e=Array.FindIndex(manifest.Signals,s=>s.Id==map.Engine);var m=new Metric();foreach(var point in exact)m.Add(manifest.Samples.Single(s=>s.TimeS==point[0].Real).Values[e],point[c].Real,map.Unit);metrics.Add(new{map.Engine,map.Native,metric=m});}return new{count=exact.Length,times=exact.Select(p=>p[0].Real).ToArray(),metrics};}
static string[] SelfTests(){
 if(Interpolate([0,2],[1,5],1)!=3)throw new InvalidDataException("Interpolation contract.");
 try{Interpolate([0,2],[1,5],3);throw new InvalidOperationException("Extrapolation accepted.");}catch(InvalidDataException){}
 var m=new Metric();m.Add(.001,0,"V");if(m.Failed!=1)throw new InvalidDataException("Fault sensitivity.");
 var a=new Affine(2,3);if(a.At(4)!=11||(a-a).At(4)!=0)throw new InvalidDataException("Affine algebra.");
 var request=new SimulationRequest("contract",new(["0","in","out"],[new Voltage("V",1,0,0),new Resistor("Ri",1,0,1000),new Resistor("Ro",2,0,1000),new Vcvs("G",2,0,1,0,10)]),new TransientConfigured(new(1,.01,Sources:[new("V",new DampedSine(0,1,1,0,0,0))])));
 var oracle=new AffineOracle(request);if(oracle.Signal("v(out)").At(1)!=10||Math.Abs(oracle.Signal("i(G)").At(1)+.01)>1e-16)throw new InvalidDataException("Gain/current polarity contract.");
 try{new AffineOracle(request with {Circuit=request.Circuit with {Elements=[..request.Circuit.Elements,new Capacitor("C",1,0,1e-9)]}});throw new InvalidOperationException("Dynamic topology accepted.");}catch(InvalidDataException){}
 try{new AffineOracle(request with {Circuit=request.Circuit with {Elements=[..request.Circuit.Elements,new Voltage("conflict",1,0,2)]}});throw new InvalidOperationException("Inconsistent constraint accepted.");}catch(InvalidDataException){}
 return ["affine source/gain algebra","linear interpolation midpoint","extrapolation refusal","deliberate 1mV signal fault exceeds original voltage tolerance","typed gain and signed output current","dynamic topology refusal","conflicting voltage constraint refusal"];
}

/// <summary>One independent affine closed form a*u(t)+b, where u is the typed original source.</summary>
readonly record struct Affine(double A,double B){public double At(double u)=>A*u+B;public static Affine operator +(Affine a,Affine b)=>new(a.A+b.A,a.B+b.B);public static Affine operator -(Affine a,Affine b)=>new(a.A-b.A,a.B-b.B);public static Affine operator *(Affine a,double k)=>new(a.A*k,a.B*k);}
/// <summary>Original cohort bounds: absolute plus relative*abs(expected), with no widening.</summary>
sealed class Metric{public int Points{get;private set;}public int Failed{get;private set;}public double MaximumAbsoluteError{get;private set;}public double MaximumNormalizedError{get;private set;}public void Add(double actual,double expected,string unit){if(!double.IsFinite(actual)||!double.IsFinite(expected))throw new InvalidDataException("Nonfinite oracle metric.");var error=Math.Abs(actual-expected);var bound=(unit=="V"?1e-6:unit=="A"?1e-9:throw new InvalidDataException("Unit"))+1e-4*Math.Abs(expected);Points++;if(error>bound)Failed++;MaximumAbsoluteError=Math.Max(MaximumAbsoluteError,error);MaximumNormalizedError=Math.Max(MaximumNormalizedError,error/bound);}}
/// <summary>Direct gain/source constraints followed by leaf KCL, without numerical MNA or reused solver equations.</summary>
sealed class AffineOracle {
 readonly string sourceId;readonly Circuit circuit;readonly Dictionary<int,Affine> nodes=new(){{0,new(0,0)}};readonly Dictionary<string,Affine> currents=new(StringComparer.Ordinal);public DampedSine Waveform{get;}
 public AffineOracle(SimulationRequest request){circuit=request.Circuit;if(request.Analysis is not TransientConfigured configuration||configuration.Options.Adaptive!=null||configuration.Options.Sources is not {Length:1} sources||sources[0].Waveform is not DampedSine wave||wave.DelayS!=0||wave.DampingPerS!=0||wave.PhaseRad!=0)throw new InvalidDataException("Unsupported stateless oracle excitation.");Waveform=wave;sourceId=sources[0].Id;
  if(circuit.Elements.Any(e=>e is not (Voltage or Vcvs or Resistor))||circuit.Elements.OfType<Resistor>().Any(r=>!double.IsFinite(r.Ohms)||r.Ohms<=0))throw new InvalidDataException("Not stateless V/R/VCVS.");
  // Propagate explicit voltage/gain equations only when their controls and one output are known.
  for(var pass=0;pass<circuit.Nodes.Length;pass++)foreach(var element in circuit.Elements){Affine? difference=element switch{Voltage v=>v.Id==sources[0].Id?new(1,0):new(0,v.Volts),Vcvs v when nodes.ContainsKey(v.Cp)&&nodes.ContainsKey(v.Cn)=>(nodes[v.Cp]-nodes[v.Cn])*v.Gain,_=>null};if(difference is not {} law)continue;if(nodes.TryGetValue(element.N,out var n)&&!nodes.ContainsKey(element.P))nodes.Add(element.P,n+law);else if(nodes.TryGetValue(element.P,out var p)&&!nodes.ContainsKey(element.N))nodes.Add(element.N,p-law);}
  if(nodes.Count!=circuit.Nodes.Length)throw new InvalidDataException("Closed form cannot resolve topology.");
  foreach(var element in circuit.Elements){Affine? constraint=element switch{Voltage v=>v.Id==sources[0].Id?new(1,0):new(0,v.Volts),Vcvs v=>(nodes[v.Cp]-nodes[v.Cn])*v.Gain,_=>null};if(constraint is {} law){var residual=nodes[element.P]-nodes[element.N]-law;if(!double.IsFinite(residual.A)||!double.IsFinite(residual.B)||Math.Abs(residual.A)>1e-12||Math.Abs(residual.B)>1e-12)throw new InvalidDataException("Inconsistent affine voltage constraint.");}}
  foreach(var r in circuit.Elements.OfType<Resistor>())currents.Add(r.Id,(nodes[r.P]-nodes[r.N])*(1/r.Ohms));
  for(var pass=0;pass<circuit.Elements.Length;pass++)for(var node=0;node<circuit.Nodes.Length;node++){var incident=circuit.Elements.Where(e=>e.P==node||e.N==node).ToArray();var unknown=incident.Where(e=>!currents.ContainsKey(e.Id)).ToArray();if(unknown.Length!=1)continue;var known=new Affine(0,0);foreach(var e in incident.Where(e=>currents.ContainsKey(e.Id)))known+=currents[e.Id]*(e.P==node?1:-1);currents.Add(unknown[0].Id,known*(unknown[0].P==node?-1:1));}
  if(currents.Count!=circuit.Elements.Length)throw new InvalidDataException("Closed-form branch KCL unresolved.");
  for(var node=0;node<circuit.Nodes.Length;node++){var residual=new Affine(0,0);foreach(var e in circuit.Elements.Where(e=>e.P==node||e.N==node))residual+=currents[e.Id]*(e.P==node?1:-1);if(Math.Abs(residual.A)>1e-12||Math.Abs(residual.B)>1e-12)throw new InvalidDataException("Independent affine KCL failure.");}
 }
 public double Source(double t)=>Waveform.Offset+Waveform.Amplitude*Math.Sin(2*Math.PI*Waveform.FrequencyHz*t);
 public Affine Signal(string id){foreach(var node in nodes)if(id=="v("+circuit.Nodes[node.Key]+")")return node.Value;foreach(var branch in currents)if(id=="i("+branch.Key+")")return branch.Value;throw new InvalidDataException("Unknown observable "+id);}
 // Native exposes root nodes only. Any omitted private voltage used here must
 // be a proven constant affine term; no missing unknown is replaced by zero.
 public object NativeLawResiduals(NativeRaw raw,BridgeResult prepared,(string Engine,string Native,string Unit)[] maps){var points=raw.Points.Select(point=>{var values=maps.ToDictionary(m=>m.Engine,m=>point[raw.Variables.FindIndex(v=>v.Name.Equals(m.Native,StringComparison.OrdinalIgnoreCase))].Real);foreach(var node in nodes.Where(n=>n.Value.A==0))values.TryAdd("v("+circuit.Nodes[node.Key]+")",node.Value.B);return (point[0].Real,values);}).ToArray();return Residuals(points,prepared);}
 public object LawResiduals(ResultManifest manifest,BridgeResult prepared)=>Residuals(manifest.Samples.Select(point=>(point.TimeS,manifest.Signals.Select((s,i)=>(s.Id,point.Values[i])).ToDictionary(p=>p.Id,p=>p.Item2))).ToArray(),prepared);
 object Residuals((double Time,Dictionary<string,double> Values)[] points,BridgeResult prepared){double sourceMax=0,gainMax=0,kclMax=0;var roots=prepared.RootNodes.Values.Select(name=>Array.IndexOf(circuit.Nodes,name)).ToArray();foreach(var point in points){double V(int node)=>node==0?0:point.Values["v("+circuit.Nodes[node]+")"];foreach(var element in circuit.Elements){if(element is Voltage source){var expected=source.Id==sourceId?Source(point.Time):source.Volts;sourceMax=Math.Max(sourceMax,Math.Abs(V(source.P)-V(source.N)-expected));}if(element is Vcvs gain)gainMax=Math.Max(gainMax,Math.Abs(V(gain.P)-V(gain.N)-gain.Gain*(V(gain.Cp)-V(gain.Cn))));}
   foreach(var node in roots){double sum=0;foreach(var element in circuit.Elements.Where(e=>e.P==node||e.N==node)){var current=element is Resistor resistor?(V(resistor.P)-V(resistor.N))/resistor.Ohms:point.Values["i("+element.Id+")"];sum+=(element.P==node?1:-1)*current;}kclMax=Math.Max(kclMax,Math.Abs(sum));}}
  return new{points=points.Length,source_maximum_v=sourceMax,gain_maximum_v=gainMax,root_kcl_maximum_a=kclMax,native_private_voltage_policy="Only constant affine private potentials reconstructed; all root currents are retained observations."};}
}
static class ImmutableLookup{public static int FindIndex<T>(this ImmutableArray<T> values,Func<T,bool> predicate){for(var i=0;i<values.Length;i++)if(predicate(values[i]))return i;return -1;}}
