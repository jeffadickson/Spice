#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;

// Bounded public-wire/API experiment only. Original cohort artifacts are immutable.
if(args.Length!=3)throw new ArgumentException("Expected frozen census, copied engine, fresh diagnosis output.");
var census=Path.GetFullPath(args[0]);var engine=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
foreach(var path in new[]{census,Path.GetDirectoryName(output)!})if(!Directory.Exists(path))throw new DirectoryNotFoundException(path);
if(!File.Exists(engine)||Directory.Exists(output)||File.Exists(output))throw new IOException("Existing engine and fresh output required.");Directory.CreateDirectory(output);
using var job=new CancellationTokenSource(TimeSpan.FromSeconds(120));var client=new SimulationClient(engine);var json=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
var identities=new Dictionary<string,string>{{engine,Hash(engine)}};var rows=new List<object>();
foreach(var index in new[]{"0458","0521"}){
 var folder=Path.Combine(census,"cases",index,"requested_0");var path=Path.Combine(folder,"00000.request.json");identities.Add(path,Hash(path));identities.Add(Path.Combine(folder,"00000.response.json"),Hash(Path.Combine(folder,"00000.response.json")));
 var original=JsonSerializer.Deserialize<SimulationRequest>(File.ReadAllText(path),WireFormat.Options)??throw new InvalidDataException("Missing original request.");
 var transient=(TransientConfigured)original.Analysis;
 // For these exact decks the nominal circuit and time-zero waveform are identical;
 // no source equation is changed to manufacture a more convenient bias problem.
 foreach(var source in transient.Options.Sources!){if(source.Waveform is not DampedSine w||w.Offset!=0||w.DelayS!=0||w.PhaseRad!=0||original.Circuit.Elements.Single(e=>e.Id==source.Id) is not Voltage {Volts:0})throw new InvalidDataException("Time-zero source identity not established.");}
 var destination=Path.Combine(output,index);Directory.CreateDirectory(destination);var trials=new List<object>();var successful=new Dictionary<string,ResultManifest>();
 await Probe("original-transient",original);
 foreach(var policy in new[]{(Name:"direct50",Options:new NewtonOptions()),(Name:"source50",Options:new NewtonOptions(Continuation:new())),(Name:"gmin50",Options:new NewtonOptions(GminContinuation:new())),(Name:"direct200",Options:new NewtonOptions(MaxIterations:200))}){
  var response=await Probe("op-"+policy.Name,original with{CaseId="case"+index+"_op_"+policy.Name,Analysis=new OperatingPointNewton(policy.Options)});
  if(response.Succeeded)successful.Add(policy.Name,response.ReadManifest());
 }
 var warm=successful.ContainsKey("source50")?"source50":successful.ContainsKey("gmin50")?"gmin50":successful.Keys.FirstOrDefault();
 if(warm!=null){
  var bias=successful[warm];var state=State(original.Circuit,bias);var materialized=original with{CaseId="case"+index+"_warm_"+warm,Analysis=new TransientConfigured(transient.Options with{InitialState=state})};
  await Probe("warm-one-step",materialized with{Analysis=new TransientConfigured(((TransientConfigured)materialized.Analysis).Options with{StopS=transient.Options.MaxStepS})});
  await Probe("warm-original-transient",materialized);
  if(index=="0458"){
   foreach(var count in new[]{2,4,8,16,32,64,128})await Probe("warm-prefix-"+count,materialized with{Analysis=new TransientConfigured(((TransientConfigured)materialized.Analysis).Options with{StopS=count*transient.Options.MaxStepS})});
   var faerBias=await Probe("faer-op-source50",original with{Backend="faer",CaseId="case"+index+"_faer_op_source50",Analysis=new OperatingPointNewton(new(Continuation:new()))});
   if(faerBias.Succeeded)await Probe("faer-warm-original-transient",original with{Backend="faer",CaseId="case"+index+"_faer_warm",Analysis=new TransientConfigured(transient.Options with{InitialState=State(original.Circuit,faerBias.ReadManifest())})});
   await Probe("op-gmin200",original with{CaseId="case"+index+"_op_gmin200",Analysis=new OperatingPointNewton(new(MaxIterations:200,GminContinuation:new()))});
   if(successful.TryGetValue("direct200",out var direct))await Probe("direct200-warm-original-transient",original with{CaseId="case"+index+"_direct200_warm",Analysis=new TransientConfigured(transient.Options with{InitialState=State(original.Circuit,direct)})});
  }
 }
 rows.Add(new{index,request_path=path,nominal_matches_time_zero=true,warm_policy=warm,trials});
 async Task<SimulationResponse> Probe(string name,SimulationRequest request){
  File.WriteAllText(Path.Combine(destination,name+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));using var timeout=CancellationTokenSource.CreateLinkedTokenSource(job.Token);timeout.CancelAfter(TimeSpan.FromSeconds(30));var response=await client.RunAsync(request,timeout.Token);File.WriteAllText(Path.Combine(destination,name+".response.json"),response.Payload.GetRawText());var m=response.Succeeded?response.ReadManifest():null;
  trials.Add(new{name,request_backend=request.Backend,succeeded=response.Succeeded,error=response.Error,request_sha256=Hash(Path.Combine(destination,name+".request.json")),response_sha256=Hash(Path.Combine(destination,name+".response.json")),newton=m?.Newton,integration=m?.Integration,samples=m?.Samples.Length,last_time_s=m?.Samples.LastOrDefault()?.TimeS,consistency=m?.Consistency});return response;
 }
}
foreach(var item in identities)if(Hash(item.Key)!=item.Value)throw new InvalidDataException("Frozen identity changed.");
File.WriteAllText(Path.Combine(output,"diagnosis.json"),JsonSerializer.Serialize(new{state="completed",identities,rows,limitations=new[]{"Two original retained requests only; no original status reclassified, no product changes.","Public OperatingPointNewton and explicit-state TransientConfigured only; source/Gmin warm starts remove Gmin in final OP per returned metadata.","Transient original waveforms, step, stop and method retained; extra initial state is an explicit experiment, not an original-request pass.","Convergence experiment, not native-waveform/model numerical qualification."}},json));Console.WriteLine("Public BJT initialization diagnosis completed: "+output);

static double[] State(Circuit circuit,ResultManifest bias){var signals=bias.Signals.Select((s,i)=>(s,i)).ToDictionary(p=>p.s.Id,p=>(p.s.Unit,Value:bias.Samples.Single().Values[p.i]),StringComparer.Ordinal);double Read(string id,string unit){var value=signals[id];if(value.Unit!=unit||!double.IsFinite(value.Value))throw new InvalidDataException("Missing/nonfinite/wrong-unit initial state");return value.Value;}return circuit.Nodes.Skip(1).Select(n=>Read("v("+n+")","V")).Concat(circuit.Elements.Where(e=>e is Voltage or Inductor or Vcvs or Ccvs).Select(e=>Read("i("+e.Id+")","A"))).ToArray();}
static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
