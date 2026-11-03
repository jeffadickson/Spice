using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using SpiceSharp.Simulations;
using SpiceSharpParser;
using SpiceSharpParser.Common;

// One bounded job per supervised process: parser/model/solver state is never
// shared across unrelated circuits. Direct exports bypass the known IR adapter
// defect without changing SpiceSharp equations or native source projections.
if(args is not [var jobPath,var output])throw new ArgumentException("Expected job JSON and fresh output JSON");
var parent=Path.GetDirectoryName(Path.GetFullPath(output))!;
if(!Directory.Exists(parent)||File.Exists(output))throw new IOException("Existing parent and fresh output required");
var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,PropertyNameCaseInsensitive=true,WriteIndented=true};
var result=new ReferenceResult();
try
{
 if(!File.Exists(jobPath)||new FileInfo(jobPath).Length>1_048_576)throw new InvalidDataException("Missing/oversized job");
 var job=JsonSerializer.Deserialize<ReferenceJob>(File.ReadAllText(jobPath),json)??throw new InvalidDataException("Empty job");
 result.CaseId=job.CaseId;result.Mode=job.Mode;
 if(string.IsNullOrWhiteSpace(job.CaseId)||job.Mode is not ("op" or "ac" or "transient")||job.Probes is not {Length:>0 and <=256}||job.Probes.Any(p=>string.IsNullOrEmpty(p.Id)||string.IsNullOrEmpty(p.Target)||p.Kind is not ("voltage" or "current"))||job.Probes.Select(p=>p.Id).Distinct(StringComparer.Ordinal).Count()!=job.Probes.Length)throw new InvalidDataException("Invalid job/probe contract");
 if(!File.Exists(job.DeckPath)||new FileInfo(job.DeckPath).Length>4_194_304)throw new InvalidDataException("Missing/oversized projected deck");
 result.Probes=job.Probes;
 var behavioral=typeof(SpiceSharpBehavioral.Builders.Functions.RealFunctionBuilder).Assembly.Location;
 result.AssemblyHashes=new(){["SpiceSharp"]=Hash(typeof(SpiceSharp.Circuit).Assembly.Location),["SpiceSharpParser"]=Hash(typeof(SpiceNetlistParser).Assembly.Location),["SpiceSharpBehavioral"]=Hash(behavioral)};
 if(job.PatchManifest!=null){
  if(!File.Exists(job.PatchManifest)||new FileInfo(job.PatchManifest).Length>1_048_576)throw new InvalidDataException("Missing/oversized patch manifest");
  using var patch=JsonDocument.Parse(File.ReadAllText(job.PatchManifest));
  if(patch.RootElement.GetProperty("schema_version").GetInt32()!=1||patch.RootElement.GetProperty("commit").GetString()!="37cefa9afe9b3cbc19615ba29026b0ad1b2d2003")throw new InvalidDataException("Unknown reference patch identity");
  if(patch.RootElement.GetProperty("dll_sha256").GetString()!=Hash(behavioral))throw new InvalidDataException("Reference patch binary identity differs");
  result.Flavor="SpiceSharp3.2.3 + Parser3.4.1 + Behavioral3.2.0 long-branch IL patch";result.PatchManifestSha256=Hash(job.PatchManifest);
 }else{
  if(Hash(behavioral)!="4cce9831ce35b6f8c5841d41d1ddc12b0e5ed53c50ad4c1ce36ee8ff6664e89a")throw new InvalidDataException("Unidentified behavioral assembly; explicit patch manifest required");
  result.Flavor="stock SpiceSharp3.2.3 + Parser3.4.1 + Behavioral3.2.0";
 }
 result.DeckSha256=Hash(job.DeckPath);
 var parsed=new SpiceNetlistParser().ParseNetlist(new UTF8Encoding(false,true).GetString(File.ReadAllBytes(job.DeckPath)),job.DeckPath);
 if(parsed.ValidationResult.HasError||parsed.FinalModel==null){result.Status="input_rejected";result.Diagnostics=parsed.ValidationResult.Errors.Select(e=>e.Message[..Math.Min(e.Message.Length,1024)]).Take(32).ToArray();}
 else{
  var model=new SpiceSharpReader().Read(parsed.FinalModel);
  if(model.ValidationResult.HasError){result.Status="input_rejected";result.Diagnostics=model.ValidationResult.Errors.Select(e=>e.Message[..Math.Min(e.Message.Length,1024)]).Take(32).ToArray();}
  else{
   var simulations=model.Simulations.ToArray();if(simulations.Length!=1)throw new InvalidDataException("Exactly one selected analysis required");
   var simulation=simulations[0];
   if(job.Mode=="op"&&simulation is not OP||job.Mode=="ac"&&simulation is not AC||job.Mode=="transient"&&simulation is not Transient)throw new InvalidDataException("Reference analysis differs from request");
   var readers=new List<Func<Complex>>();
   foreach(var probe in job.Probes){
    if(job.Mode=="ac"){
     var ac=(IFrequencySimulation)simulation;
     if(probe.Kind=="voltage"){var e=new ComplexVoltageExport(ac,probe.Target);readers.Add(()=>e.Value);}
     else{var e=new ComplexCurrentExport(ac,probe.Target);readers.Add(()=>e.Value);}
    }else{
     var bias=(IBiasingSimulation)simulation;
     if(probe.Kind=="voltage"){var e=new RealVoltageExport(bias,probe.Target);readers.Add(()=>new(e.Value,0));}
     else{var e=new RealCurrentExport(bias,probe.Target);readers.Add(()=>new(e.Value,0));}
    }
   }
   simulation.EventExportData+=(_,_)=>{
    if(result.Samples.Count>=20000||(long)(result.Samples.Count+1)*job.Probes.Length>100000)throw new InvalidDataException("Reference sample budget exceeded");
    var values=new Dictionary<string,ComplexValue>(StringComparer.Ordinal);
    for(var i=0;i<readers.Count;i++){var value=readers[i]();if(!double.IsFinite(value.Real)||!double.IsFinite(value.Imaginary))throw new InvalidDataException("Nonfinite reference result");values.Add(job.Probes[i].Id,new(value.Real,value.Imaginary));}
    var coordinate=simulation is AC a?a.Frequency:simulation is IStateful<IIntegrationMethod> time?time.State.Time:0;
    if(!double.IsFinite(coordinate))throw new InvalidDataException("Nonfinite coordinate");
    result.Samples.Add(new(coordinate,values));
   };
   simulation.Execute(model.Circuit);
   if(result.Samples.Count==0)throw new InvalidDataException("No reference samples");
   result.Status="executed";result.Diagnostics=model.ValidationResult.Warnings.Select(e=>e.Message[..Math.Min(e.Message.Length,1024)]).Take(32).ToArray();
  }
 }
}
catch(Exception e){result.Status="reference_unavailable";result.Diagnostics=[e.GetType().Name+": "+e.Message[..Math.Min(e.Message.Length,1024)]];result.Samples.Clear();}
var temporary=output+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllText(temporary,JsonSerializer.Serialize(result,json));File.Move(temporary,output);
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
internal sealed record ReferenceJob(string CaseId,string Mode,string DeckPath,ReferenceProbe[] Probes,string? PatchManifest=null);
internal sealed record ReferenceProbe(string Id,string Kind,string Target){public string Unit=>Kind=="current"?"A":"V";}
internal sealed record ComplexValue(double Real,double Imag);
internal sealed record ReferenceSample(double Coordinate,Dictionary<string,ComplexValue> Values);
internal sealed class ReferenceResult
{
 public int SchemaVersion{get;}=1;public string? CaseId{get;set;}public string? Mode{get;set;}public string Status{get;set;}="not_executed";public string? Flavor{get;set;}public string? DeckSha256{get;set;}public string? PatchManifestSha256{get;set;}
 public string CoordinateUnit=>Mode switch{"op"=>"op_index","ac"=>"Hz","transient"=>"s",_=>""};public ReferenceProbe[] Probes{get;set;}=[];
 public Dictionary<string,string> AssemblyHashes{get;set;}=[];public string[] Diagnostics{get;set;}=[];public List<ReferenceSample> Samples{get;}=[];
}
