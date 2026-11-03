using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>One original directive, with source identity, ordered requests and SI coordinate values (DC source units, Hz, or seconds).</summary>
public sealed record RequestedOperation(string Id,string Kind,SourceRange Range,ImmutableArray<SimulationRequest> Requests,ImmutableArray<double> Coordinates)
{
 /// <summary>SI coordinate unit: V/A for DC source sweeps, Hz for AC, s for transient, absent for OP.</summary>
 public string? CoordinateUnit { get; init; }
 /// <summary>Evaluated original directive scalar arguments, in grammar order; retained for audit including transient TSTEP/TSTOP/TSTART/TMAX.</summary>
 public ImmutableArray<double> Arguments { get; init; } = [];
 /// <summary>Requested transient reporting start in seconds; the engine still integrates from zero.</summary>
 public double OutputStartS { get; init; }
 /// <summary>Retained root voltage initial conditions handled through a separate temporary public-client bias solve.</summary>
 public ImmutableArray<PreparedNodeInitialCondition> InitialConditions { get; init; } = [];
 /// <summary>Optional temporary clamped OP; its extra branches are never part of the original transient state.</summary>
 public SimulationRequest? InitializationRequest { get; init; }
}
/// <summary>Atomic preparation: unsupported original execution semantics publish diagnostics and no partial operation plan.</summary>
public sealed record RequestedAnalysisPlan(ImmutableArray<RequestedOperation> Operations,ImmutableArray<BridgeDiagnostic> Diagnostics)
{
 /// <summary>All requested supported directives were prepared; numerical success remains separate.</summary>
 public bool Success=>!Operations.IsEmpty&&Diagnostics.IsEmpty;
 /// <summary>Explicit unexecuted output/reporting instructions; analyses and controls are never silently omitted.</summary>
 public ImmutableArray<OperatingPointOmission> Observations { get; init; } = [];
 /// <summary>Original physical circuit and observable mapping, before temporary initialization clamps.</summary>
 public BridgeResult? CircuitPreparation { get; init; }
}
/// <summary>Result of one independently initialized request. Transport errors remain distinct from engine response errors.</summary>
public sealed record RequestedPointResult(int Index,SimulationResponse? Response,string? TransportError)
{
 /// <summary>Separate optional initial-bias response; it is not a requested transient result.</summary>
 public SimulationResponse? InitializationResponse { get; init; }
 /// <summary>Explicit reported-window view. Response retains the full raw engine history.</summary>
 public ResultManifest? ReportedManifest { get; init; }
}
/// <summary>All points of one original directive; a failed operation publishes no partial successful responses.</summary>
public sealed record RequestedOperationResult(string Id,bool Success,ImmutableArray<RequestedPointResult> Points);
/// <summary>Prepares ordinary root .op/.dc/.ac/.tran cards using generated syntax and the public client contract.</summary>
public static partial class RequestedAnalysis
{
 /// <summary>Prepares bounded grids. DC uses fresh OP per point, without continuation/hysteresis guarantees; controls, nested analyses and UIC remain explicit refusals; bounded linear node IC and reporting starts are retained.</summary>
 public static RequestedAnalysisPlan Prepare(DeckSyntax syntax,int maxPoints=10000,BridgeLimits? limits=null,string backend="lu")
 {
  ArgumentNullException.ThrowIfNull(syntax);
  if(maxPoints is <1 or >100000)throw new ArgumentOutOfRangeException(nameof(maxPoints));
  RequestedAnalysisPlan Fail(string code,SourceRange? range)=>new([],[new(code,range)]);
  if(syntax.Status!="accepted_structure"||syntax.Declarations is not {} catalog)return Fail("syntax_not_accepted",null);
  var forbidden=catalog.Cards.FirstOrDefault(c=>c.Kind=="control"||c.Kind=="extension"&&OperatingPointSelection.OmissionReason(c)=="analysis_not_selected"||c.ScopeId!=0&&c.Kind is "op" or "dc" or "ac" or "tran");
  if(forbidden!=null)return Fail("unsupported_requested_program",forbidden.Range);
  if(catalog.Analyses.IsEmpty)return Fail("no_requested_analysis",null);
  if(catalog.InitialConditions.Any(ic=>ic.ScopeId!=0||!ic.Supported))return Fail("unsupported_initial_condition_profile",catalog.InitialConditions.First(ic=>ic.ScopeId!=0||!ic.Supported).Range);
  if(!catalog.InitialConditions.IsEmpty&&!catalog.Analyses.Any(a=>a.Kind=="tran"))return Fail("initial_conditions_without_transient",catalog.InitialConditions[0].Range);
  var selected=OperatingPointSelection.SelectRequested(syntax,limits,backend,catalog.InitialConditions.Select(ic=>ic.Range.Start).ToImmutableHashSet());
  if(!selected.Success)return new([],selected.Prepared.Diagnostics);
  var prepared=selected.Prepared;var baseRequest=prepared.Request!;
  var environment=ParameterEvaluator.Evaluate(catalog);
  var operations=ImmutableArray.CreateBuilder<RequestedOperation>();
  try{
   var initialConditions=BindInitialConditions(catalog,environment,prepared);
   foreach(var card in catalog.Analyses.OrderBy(c=>c.Range.Start)){
    var id="requested_"+operations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if(card.Kind=="tran"&&!card.Names.IsEmpty)throw new PreparationFailure("unsupported_transient_uic",card.Range);
    var values=card.Values.Select(v=>{
     var result=ParameterEvaluator.EvaluateValue(catalog,environment,v);
     if(result.Value is not {} value)throw new PreparationFailure(result.Diagnostics[0].Code,v.Range);
     return value;
    }).ToArray();
    var requests=ImmutableArray.CreateBuilder<SimulationRequest>();ImmutableArray<double> coordinates=[];string? unit=null;var outputStart=0d;SimulationRequest? initialization=null;
    switch(card.Kind){
     case "op":requests.Add(baseRequest with{CaseId=id});break;
     case "dc":
      if(card.Names.Length!=1)throw new PreparationFailure("unsupported_nested_dc_sweep",card.Range);
      var resolvedSource=RequestedDcSourceBinding.Resolve(catalog,prepared,card.Names[0]);
      if(resolvedSource.Source is not {} source)throw new PreparationFailure(resolvedSource.Error!,card.Range);
      unit=source is Voltage?"V":"A";
      coordinates=Linear(values[0],values[1],values[2],maxPoints,card.Range);
      foreach(var value in coordinates){
       // Each request owns its element array, preventing mutation/state carryover between points.
       var elements=baseRequest.Circuit.Elements.Select(e=>e.Id!=source.Id?e:source is Voltage v?v with{Volts=value}:(Element)((Current)source with{Amps=value})).ToArray();
       requests.Add(baseRequest with{CaseId=id+"_"+requests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),Circuit=baseRequest.Circuit with{Elements=elements}});
      }
      break;
     case "ac":
      unit="Hz";
      var count=values[0];if(count<1||count!=Math.Truncate(count)||count>maxPoints||values[1]<=0||values[2]<values[1])throw new PreparationFailure("invalid_requested_ac",card.Range);
      var mode=card.Names[0].ToLowerInvariant();
      if(mode=="lin")coordinates=count==1?[values[1]]:Enumerable.Range(0,(int)count).Select(i=>values[1]+(values[2]-values[1])*i/(count-1)).ToImmutableArray();
      else if(mode is "dec" or "oct"){
       var ratio=mode=="dec"?10d:2d;var span=Math.Log(values[2]/values[1],ratio)*count;
       if(!double.IsFinite(span)||span>=maxPoints)throw new PreparationFailure("requested_point_budget",card.Range);
       var points=(int)Math.Floor(span+1e-10)+1;
       if(points>maxPoints)throw new PreparationFailure("requested_point_budget",card.Range);
       coordinates=Enumerable.Range(0,points).Select(i=>values[1]*Math.Pow(ratio,i/count)).ToImmutableArray();
      }else throw new PreparationFailure("unsupported_ac_grid",card.Range);
      requests.Add(baseRequest with{CaseId=id,Analysis=new Ac(coordinates.ToArray(),prepared.AcSources.ToArray())});break;
     case "tran":
      unit="s";
      if(!card.Names.IsEmpty)throw new PreparationFailure("unsupported_transient_uic",card.Range);
      outputStart=values.Length>2?values[2]:0;
      if(outputStart<0||outputStart>values[1])throw new PreparationFailure("invalid_transient_start",card.Range);
      var maximumStep=values.Length>3?values[3]:Math.Min(values[0],(values[1]-outputStart)/50);
      var transient=TransientSelection.PrepareRequested(syntax,values[1],values[0],maximumStep,maxPoints,limits,backend,selected);
      if(!transient.Success)throw new PreparationFailure(transient.Prepared.Diagnostics[0].Code,card.Range);
      requests.Add(transient.Prepared.Request! with{CaseId=id});
      if(!initialConditions.IsEmpty)initialization=ClampRequest(baseRequest,id,initialConditions);break;
    }
    operations.Add(new(id,card.Kind,card.Range,requests.ToImmutable(),coordinates) { CoordinateUnit=unit,Arguments=values.ToImmutableArray(),OutputStartS=outputStart,InitializationRequest=initialization,InitialConditions=card.Kind=="tran"?initialConditions:[] });
   }
  }catch(PreparationFailure e){return Fail(e.Code,e.Range);}
  return new(operations.ToImmutable(),[]) { Observations=selected.Omissions.Where(o=>o.Kind=="extension").ToImmutableArray(),CircuitPreparation=prepared };
 }
 private static ImmutableArray<double> Linear(double start,double stop,double step,int maximum,SourceRange range)
 {
  if(!double.IsFinite(start)||!double.IsFinite(stop)||!double.IsFinite(step)||step==0||(stop-start)/step<0)throw new PreparationFailure("invalid_requested_dc",range);
  var span=(stop-start)/step;
  if(!double.IsFinite(span)||span>=maximum)throw new PreparationFailure("requested_point_budget",range);
  // Endpoint is included only when on the requested grid; tolerance absorbs decimal representation error.
  var points=(int)Math.Floor(span+1e-10)+1;
  if(points>maximum)throw new PreparationFailure("requested_point_budget",range);
  return Enumerable.Range(0,points).Select(i=>start+i*step).ToImmutableArray();
 }
 /// <summary>Runs every operation in order, starting a fresh public-client process for every request. Cancellation propagates; operation failures are atomic and later operations still run.</summary>
 public static async Task<ImmutableArray<RequestedOperationResult>> ExecuteAsync(RequestedAnalysisPlan plan,SimulationClient client,CancellationToken cancellation=default)
 {
  ArgumentNullException.ThrowIfNull(plan);ArgumentNullException.ThrowIfNull(client);
  if(!plan.Success)throw new ArgumentException("A complete prepared plan is required.",nameof(plan));
  var results=ImmutableArray.CreateBuilder<RequestedOperationResult>();
  foreach(var operation in plan.Operations){
   var points=ImmutableArray.CreateBuilder<RequestedPointResult>();var success=true;
   foreach(var request in operation.Requests){
    cancellation.ThrowIfCancellationRequested();
    try{

     var execution=await PrepareExecutionAsync(operation,points.Count,client,cancellation);
     if(!execution.Success){points.Add(new(points.Count,null,execution.Failure){InitializationResponse=execution.InitializationResponse});success=false;continue;}
     var response=await client.RunAsync(execution.Request!,cancellation);
     points.Add(new(points.Count,response,null){InitializationResponse=execution.InitializationResponse,ReportedManifest=response.Succeeded?ReportedManifest(operation,response):null});success&=response.Succeeded;
    }
    catch(Exception e) when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException){points.Add(new(points.Count,null,e.GetType().Name));success=false;}
   }
   // Preserve one outcome per requested point, but withhold successful partial payloads on failed operations.
   results.Add(new(operation.Id,success,success?points.ToImmutable():points.Select(p=>p.Response?.Succeeded==true?p with{Response=null,ReportedManifest=null,TransportError="operation_failed_atomically"}:p).ToImmutableArray()));
  }
  return results.ToImmutable();
 }
 private sealed class PreparationFailure(string code,SourceRange? range):Exception
 {internal string Code{get;}=code;internal SourceRange? Range{get;}=range;}
}
