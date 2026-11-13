using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>One original directive, with source identity, ordered requests and SI coordinate values (DC source units, Hz, or seconds).</summary>
public sealed record RequestedOperation(string Id,string Kind,SourceRange Range,ImmutableArray<SimulationRequest> Requests,ImmutableArray<double> Coordinates)
{
 /// <summary>Validated bounded PZ dispatch; its result is computed at execution and is not a Rust transport request.</summary>
 public PreparedPoleZeroAnalysis? PoleZero { get; init; }
 /// <summary>SI coordinate unit: V/A for DC source sweeps, Hz for AC, s for transient, absent for OP.</summary>
 public string? CoordinateUnit { get; init; }
 /// <summary>Outer DC coordinates in request order, independently identified by the source terminal voltage difference.</summary>
 public NestedDcSweep? NestedDc { get; init; }
 /// <summary>Evaluated original directive scalar arguments, in grammar order; retained for audit including transient TSTEP/TSTOP/TSTART/TMAX.</summary>
 public ImmutableArray<double> Arguments { get; init; } = [];
 /// <summary>Requested transient reporting start in seconds; the engine still integrates from zero.</summary>
 public double OutputStartS { get; init; }
 /// <summary>Retained root voltage initial conditions handled through a separate temporary public-client bias solve.</summary>
 public ImmutableArray<PreparedNodeInitialCondition> InitialConditions { get; init; } = [];
 /// <summary>Optional startup consistency solve. Temporary constraints are never part of the original transient circuit.</summary>
 public SimulationRequest? InitializationRequest { get; init; }
 /// <summary>True for a requested UIC startup with storage constraints and no physical DC bias solve.</summary>
 public bool UsesUic { get; init; }
 /// <summary>Optional original root control command or finite run invocation.</summary>
 public RequestedControlOrigin? ControlOrigin { get; init; }
}
/// <summary>Secondary voltage axis for a two-source DC sweep. A null terminal signal denotes ground.</summary>
public sealed record NestedDcSweep(string? NativeVoltageSignal,ImmutableArray<double> Coordinates)
{
 /// <summary>Native voltage signal at the negative terminal, subtracted from the positive terminal voltage.</summary>
 public string? NativeNegativeVoltageSignal { get; init; }
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
 /// <summary>Qualified dispatch profile: deck cards, batch-selected root transient, direct control commands or finite retained-deck run.</summary>
 public string ControlProfile { get; init; } = "deck";
 /// <summary>Base operating temperature in Celsius; an explicit DC temperature sweep publishes its own per-point coordinates.</summary>
 public double OperatingTemperatureCelsius { get; init; } = 27;
 /// <summary>Bounded root scalar exports, available before any control analysis.</summary>
 public ImmutableDictionary<string,double> ControlConstants { get; init; } = ImmutableDictionary<string,double>.Empty;
 /// <summary>Explicit unexecuted control reporting, deferred suffix declarations, exit and uninvoked deck instructions.</summary>
 public ImmutableArray<OperatingPointOmission> ProgramOmissions { get; init; } = [];
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
public sealed record RequestedOperationResult(string Id,bool Success,ImmutableArray<RequestedPointResult> Points)
{
 /// <summary>Finite analytical root result for a requested PZ operation, absent for ordinary transport analyses.</summary>
 public PoleZeroResult? PoleZero { get; init; }
}

/// <summary>Immutable, source-identified bounded PZ request sharing the root analysis implementation.</summary>
public sealed record PreparedPoleZeroAnalysis(BridgeResult Circuit,AnalysisSyntax Directive)
{
 /// <summary>Evaluates every requested root or returns one explicit unsupported outcome.</summary>
 public PoleZeroResult Execute()=>PoleZeroSelection.AnalyzeRequested(Circuit,Directive);
}
/// <summary>Prepares ordinary root .op/.dc/.ac/.tran cards using generated syntax and the public client contract.</summary>
public static partial class RequestedAnalysis
{
 /// <summary>Prepares bounded grids. DC uses fresh OP per point, without continuation/hysteresis guarantees; unmodeled controls and nested analyses refuse explicitly. Bounded bias IC, UIC and reporting starts are retained.</summary>
 public static RequestedAnalysisPlan Prepare(DeckSyntax syntax,int maxPoints=10000,BridgeLimits? limits=null,string backend="lu")
 {
  ArgumentNullException.ThrowIfNull(syntax);
  if(maxPoints is <1 or >100000)throw new ArgumentOutOfRangeException(nameof(maxPoints));
  RequestedAnalysisPlan Fail(string code,SourceRange? range)=>new([],[new(code,range)]);
  var branches=NetlistConditionalSelection.Select(syntax);
  if(branches.Diagnostic is {} branchFailure)return Fail(branchFailure.Code,branchFailure.Range);
  syntax=branches.Syntax;
  if(syntax.Status!="accepted_structure"||syntax.Declarations is not {} catalog)return Fail("syntax_not_accepted",null);
  var forbidden=catalog.Cards.FirstOrDefault(c=>c.Kind=="extension"&&OperatingPointSelection.OmissionReason(c)=="analysis_not_selected"||c.ScopeId!=0&&c.Kind is "op" or "dc" or "ac" or "tran");
  if(forbidden!=null)return Fail("unsupported_requested_program",forbidden.Range);
  var temperature=RequestedTemperaturePreparation.Prepare(catalog,limits);
  if(temperature.Diagnostic is {} temperatureError)return Fail(temperatureError.Code,temperatureError.Range);
  limits=temperature.Limits;
  var constants=BindControlConstants(catalog,out var constantError);
  if(constantError!=null)return Fail(constantError.Code,constantError.Range);
  var control=RequestedControlPreparation.Compile(catalog,constants);if(control.Diagnostic is {} controlFailure)return Fail(controlFailure.Code,controlFailure.Range);
  if(control.Directives.IsEmpty)return Fail("no_requested_analysis",null);
  if(catalog.InitialConditions.Any(ic=>ic.ScopeId!=0||!ic.Supported))return Fail("unsupported_initial_condition_profile",catalog.InitialConditions.First(ic=>ic.ScopeId!=0||!ic.Supported).Range);
  // Node .IC constrains only the non-UIC transient bias, never a DC or AC operation.
  var hasTransient=control.Directives.Any(a=>a.Analysis.Kind=="tran");
  var methodCards=catalog.Cards.Where(c=>hasTransient&&c.ScopeId==0&&c.TransientMethodOption!=null).ToArray();
  if(methodCards.Length>1)return Fail("ambiguous_transient_method",methodCards[1].Range);
  var requestedMethod=methodCards.SingleOrDefault()?.TransientMethodOption;
  var abstolCards=catalog.Cards.Where(c=>hasTransient&&c.ScopeId==0&&c.TransientAbstolOption!=null).ToArray();
  if(abstolCards.Length>1)return Fail("ambiguous_transient_abstol",abstolCards[1].Range);
  if(abstolCards.Length!=0&&control.Directives.Any(d=>d.Analysis.Kind!="tran"))
   return Fail("unsupported_mixed_abstol_analyses",abstolCards[0].Range);
  var handledPhysicalStarts=
      temperature.Handled.Union(catalog.InitialConditions.Select(ic=>ic.Range.Start)).Concat(methodCards.Concat(abstolCards).Concat(catalog.Cards.Where(c=>!c.ControlConstants.IsEmpty)).Select(c=>c.Range.Start)).ToImmutableHashSet();
  var selected=OperatingPointSelection.SelectRequested(syntax,limits,backend,handledPhysicalStarts);
  if(!selected.Success)return new([],selected.Prepared.Diagnostics);
  var prepared=selected.Prepared;var baseRequest=prepared.Request!;
  var sourceAlterations=catalog.ControlBlocks.SelectMany(b=>b.Commands).Where(c=>c.SourceParameterAlteration!=null).Select(c=>c.SourceParameterAlteration!).ToArray();
  if(sourceAlterations.Length!=0){
   var parameters=RequestedSourceParameterBinding.Bind(prepared,sourceAlterations);
   if(sourceAlterations.Any(a=>!parameters.ContainsKey(a.Target)))return Fail("unsupported_source_parameter_alter_target",sourceAlterations.First(a=>!parameters.ContainsKey(a.Target)).Range);
  }
  if(limits.TemperatureCelsius!=27&&!RequestedTemperaturePreparation.Supports(prepared))
    return Fail("unsupported_operating_temperature_profile",catalog.Cards.FirstOrDefault(c=>c.OperatingTemperature!=null)?.Range);
  if(baseRequest.Circuit.Elements.Any(e=>e is BsimSoiFdReference) &&
      control.Directives.FirstOrDefault(d=>d.Analysis.Kind is not ("op" or "dc")) is {} dynamicFd)
    return Fail("unsupported_bsim_soi_fd_dynamic",dynamicFd.Analysis.Range);
  // Native let may overwrite an existing root result vector by its bare node
  // name. Do not hide observable replacement as a local reporting declaration.
  var rootNames=prepared.RootNodes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
  var clobber=catalog.ControlBlocks.SelectMany(b=>b.Commands).FirstOrDefault(c=>
      c.Postprocessing is {Kind:ControlPostprocessingKind.Let} local&&rootNames.Contains(local.Names[0]));
  if(clobber!=null)return Fail("unsupported_control_observable_assignment",clobber.Range);
  var environment=ParameterEvaluator.EvaluateAtTemperature(catalog,limits.TemperatureCelsius);
  double? abstol=null;
  if(abstolCards.SingleOrDefault()?.TransientAbstolOption is {} tolerance)
  {
   var value=ParameterEvaluator.EvaluateValue(catalog,environment,tolerance);
   if(value.Value is not >0)return Fail("invalid_transient_abstol",tolerance.Range);
   abstol=value.Value;
  }
  var operations=ImmutableArray.CreateBuilder<RequestedOperation>();
  try{
   var biasedConditions=control.Directives.Any(a=>a.Analysis.Kind=="tran"&&a.Analysis.Names.IsEmpty)
       ? BindInitialConditions(catalog,environment,prepared) : [];
   foreach(var directive in control.Directives){
    var card=directive.Analysis;
    if(card.Kind is not ("op" or "dc")&&baseRequest.Circuit.Elements.Any(e=>e is BehavioralVoltage))
     throw new PreparationFailure("unsupported_behavioral_analysis",card.Range);
    if(card.Kind is not ("op" or "dc" or "ac")&&baseRequest.Circuit.Elements.Any(e=>e is BehavioralResistor))
     throw new PreparationFailure("unsupported_behavioral_resistance_analysis",card.Range);
    var id="requested_"+operations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    var usesUic=card.Kind=="tran"&&card.Names.Length==1
        &&card.Names[0].Equals("uic",StringComparison.OrdinalIgnoreCase);
    var initialConditions=card.Kind=="tran"?biasedConditions:[];
    var values=card.Values.Select(v=>{
     var result=ParameterEvaluator.EvaluateValue(catalog,environment,v);
     if(result.Value is not {} value)throw new PreparationFailure(result.Diagnostics[0].Code,v.Range);
     return value;
    }).ToArray();
    var requests=ImmutableArray.CreateBuilder<SimulationRequest>();ImmutableArray<double> coordinates=[];string? unit=null;var outputStart=0d;SimulationRequest? initialization=null;NestedDcSweep? nestedDc=null;PreparedPoleZeroAnalysis? poleZero=null;
    switch(card.Kind){
     case "op":requests.Add(baseRequest with{CaseId=id});break;
     case "pz":
      poleZero=new(prepared,card);
      var checkedRoots=poleZero.Execute();
      if(!checkedRoots.Success)throw new PreparationFailure(checkedRoots.Diagnostic!.Code,card.Range);
      unit="1/s";break;
     case "dc":
      if(card.Names.Length is <1 or >2)throw new PreparationFailure("unsupported_nested_dc_sweep",card.Range);
      if(card.Names.Any(name=>name.Equals("temp",StringComparison.OrdinalIgnoreCase)))
      {
       if(card.Names.Length!=1||baseRequest.Circuit.Elements.Any(e=>e is not (Resistor or Voltage or Current or Capacitor or Inductor or Vcvs or Vccs or Cccs or Ccvs or SpiceDiode {Model.JunctionFarads:0,Model.TransitSeconds:0})))
        throw new PreparationFailure("unsupported_temperature_sweep_profile",card.Range);
       unit="degC";
       coordinates=Linear(values[0],values[1],values[2],Math.Min(maxPoints,128),card.Range);
       foreach(var celsius in coordinates)
       {
        if(celsius<=-273.15)throw new PreparationFailure("invalid_sweep_temperature",card.Range);
        var atTemperature=(limits??new BridgeLimits()) with{TemperatureCelsius=celsius};
        var point=OperatingPointSelection.SelectRequested(syntax,atTemperature,backend,handledPhysicalStarts).Prepared;
        if(!point.Success)throw new PreparationFailure(point.Diagnostics[0].Code,point.Diagnostics[0].Range);
        requests.Add(point.Request! with{CaseId=id+"_"+requests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),Analysis=DcSweepAnalysis(point.Request!)});
       }
       break;
      }
      var resolvedSource=RequestedDcSourceBinding.Resolve(catalog,prepared,card.Names[0]);
      if(resolvedSource.Source is not {} source)throw new PreparationFailure(resolvedSource.Error!,card.Range);
      unit=source is Voltage?"V":"A";
      var primary=Linear(values[0],values[1],values[2],maxPoints,card.Range);
      Voltage? outerSource=null;
      ImmutableArray<double> outer=[0];
      if(card.Names.Length==2){
       var secondary=RequestedDcSourceBinding.Resolve(catalog,prepared,card.Names[1]);
       if(secondary.Source is not Voltage v||v.P==v.N||v.Id==source.Id)
        throw new PreparationFailure("unsupported_nested_dc_secondary_source",card.Range);
       outerSource=v;
       outer=Linear(values[3],values[4],values[5],maxPoints/primary.Length,card.Range);
       // Native RAW repeats the primary axis. Reconstruct the independent
       // outer voltage from the original terminal columns and known ground.
       var terminals=catalog.Primitives.Single(p=>p.ScopeId==0&&p.Name.Equals(card.Names[1],StringComparison.OrdinalIgnoreCase)&&p.Family=="V");
       nestedDc=new(v.P==0?null:"v("+terminals.P.Text+")",outer.SelectMany(value=>Enumerable.Repeat(value,primary.Length)).ToImmutableArray())
        {NativeNegativeVoltageSignal=v.N==0?null:"v("+terminals.N.Text+")"};
      }
      coordinates=outer.SelectMany(_=>primary).ToImmutableArray();
      var dcAnalysis=DcSweepAnalysis(baseRequest);
      foreach(var outerValue in outer)foreach(var value in primary){
       // Each request owns its element array, preventing mutation/state carryover between points.
       var elements=baseRequest.Circuit.Elements.Select(e=>e.Id==source.Id
          ?e is Voltage v?v with{Volts=value}:(Element)((Current)e with{Amps=value})
          :outerSource!=null&&e.Id==outerSource.Id?outerSource with{Volts=outerValue}:e).ToArray();
       requests.Add(baseRequest with{CaseId=id+"_"+requests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),Circuit=baseRequest.Circuit with{Elements=elements},Analysis=dcAnalysis});
      }
      break;
     case "ac":
      unit="Hz";
      var count=values[0];
      var mode=card.Names[0].ToLowerInvariant();
      if(count<1||count!=Math.Truncate(count)||count>maxPoints||values[1]<0||mode!="lin"&&values[1]==0||values[2]<values[1])throw new PreparationFailure("invalid_requested_ac",card.Range);
      if(mode=="lin")coordinates=count==1?[values[1]]:Enumerable.Range(0,(int)count).Select(i=>values[1]+(values[2]-values[1])*i/(count-1)).ToImmutableArray();
      else if(mode is "dec" or "oct"){
       var ratio=mode=="dec"?10d:2d;var span=Math.Log(values[2]/values[1],ratio)*count;
       if(!double.IsFinite(span)||span>=maxPoints)throw new PreparationFailure("requested_point_budget",card.Range);
       var points=(int)Math.Floor(span+1e-10)+1;
       if(points>maxPoints)throw new PreparationFailure("requested_point_budget",card.Range);
       coordinates=Enumerable.Range(0,points).Select(i=>values[1]*Math.Pow(ratio,i/count)).ToImmutableArray();
      }else throw new PreparationFailure("unsupported_ac_grid",card.Range);
      var acCircuit=baseRequest.Circuit;
      if(!prepared.AcResistances.IsEmpty){
       // The engine biases and linearizes nonlinear elements from the same
       // circuit. Substitution is exact only when AC stamps are bias independent.
       if(acCircuit.Elements.Any(e=>e is not (Resistor or Capacitor or Inductor or Voltage or Current or Vcvs or Vccs or Cccs or Ccvs)))
        throw new PreparationFailure("unsupported_nonlinear_ac_resistance",card.Range);
       acCircuit=acCircuit with{Elements=acCircuit.Elements.Select(e=>
           prepared.AcResistances.TryGetValue(e.Id,out var ohms)?new Resistor(e.Id,e.P,e.N,ohms):e).ToArray()};
      }
      requests.Add(baseRequest with{CaseId=id,Circuit=acCircuit,Analysis=new Ac(coordinates.ToArray(),prepared.AcSources.ToArray())});break;
     case "tran":
      unit="s";
      if(!card.Names.IsEmpty&&!usesUic)throw new PreparationFailure("unsupported_transient_uic",card.Range);
      outputStart=values.Length>2?values[2]:0;
      if(outputStart<0||outputStart>values[1])throw new PreparationFailure("invalid_transient_start",card.Range);
      var maximumStep=values.Length>3?values[3]:Math.Min(values[0],(values[1]-outputStart)/50);
      var transient=TransientSelection.PrepareRequested(syntax,values[1],values[0],maximumStep,maxPoints,limits,backend,selected);
      if(!transient.Success)throw new PreparationFailure(transient.Prepared.Diagnostics[0].Code,card.Range);
      var transientRequest=transient.Prepared.Request! with{CaseId=id};
      var options=((TransientConfigured)transientRequest.Analysis).Options;
      if(requestedMethod!=null){
       if(requestedMethod=="trapezoidal"&&options.InitialNewton!=null&&
           transientRequest.Circuit.Elements.Any(e=>e is Bsim330 or Mos1Channel{Model.OxideFarads:>0}))
        throw new PreparationFailure("unsupported_trapezoidal_device",methodCards[0].Range);
       options=options with{Method=requestedMethod};
      }
      if(usesUic){
       var hints=BindInitialConditions(catalog,environment,prepared,uic:true);
       (initialization,initialConditions)=UicStartup(baseRequest,id,hints,options,card.Range);
       // M=TT=0 reduces the junction to Q=C*V and the same Shockley
       // conduction law. Use the existing explicit-state wire profile.
       var elements=transientRequest.Circuit.Elements.Select(e=>e is SpiceDiode diode
           ? new DiodeWithCapacitance(diode.Id,diode.P,diode.N,diode.Model.SaturationAmps,
               diode.Model.ThermalVolts,diode.Model.JunctionFarads)
           : e).ToArray();
       transientRequest=transientRequest with{Circuit=transientRequest.Circuit with{Elements=elements}};
       // Match native's default method for the bounded linear UIC profile.
       // For constant-charge diodes use local step doubling with a fixed
       // accuracy policy and preserve the requested step and work ceilings.
       var hasDiode=elements.Any(e=>e is DiodeWithCapacitance);
       options=options with{
        InitialNewton=null,
        Method=requestedMethod??(hasDiode?"backward_euler":"trapezoidal"),
        Adaptive=hasDiode?new AdaptiveOptions(1e-7,1e-9,1e-12,
            Math.Max(double.Epsilon,options.MaxStepS/1024),4*options.MaxSteps):null
       };
      }else if(!initialConditions.IsEmpty)initialization=ClampRequest(baseRequest,id,initialConditions);
      else if(options.InitialNewton==null&&(requestedMethod is null or "trapezoidal")&&
              options.Sources is {Length:>0}&&transientRequest.Circuit.Elements.Any(e=>e is Capacitor or Inductor)){
       // A coarse step across a driven storage ramp can give a large charge error.
       // Retain the deck's step ceiling and bound rejected work independently.
       options=options with{
        Method="trapezoidal",
        Adaptive=new AdaptiveOptions(1e-7,1e-9,1e-9,
            Math.Max(double.Epsilon,options.MaxStepS/1e6),4*options.MaxSteps)
       };
      }
      options=options with{CurrentAbsoluteTolerance=abstol};
      // Native MOS decks default to trapezoidal. The runtime records source-
      // corner BE restarts and reconstructs junction current with that method.
      if(!usesUic && requestedMethod is null && transientRequest.Circuit.Elements.Any(e=>e is Mos1Channel) &&
          !transientRequest.Circuit.Elements.Any(e=>e is Bsim330 or Mos1Channel{Model.OxideFarads:>0}))
       options=options with{Method="trapezoidal"};
      if(!usesUic && options.Method=="trapezoidal" && transientRequest.Circuit.Elements.Any(e=>e is Mos1Channel) &&
          !transientRequest.Circuit.Elements.Any(e=>e is Bsim330 or Mos1Channel{Model.OxideFarads:>0}))
       options=options with{Adaptive=new AdaptiveOptions(1e-7,1e-9,1e-9,
           Math.Max(double.Epsilon,options.MaxStepS/1e6),4*options.MaxSteps,CurrentRelative:1e-4)};
      if(abstol is {} amps&&!usesUic&&initialization!=null&&options.InitialNewton!=null)
       initialization=initialization with{Analysis=new OperatingPointNewton(new NewtonOptions(CurrentAbsoluteTolerance:amps))};
      requests.Add(transientRequest with{Analysis=new TransientConfigured(options)});break;
     default:throw new PreparationFailure("unsupported_requested_analysis:"+card.Kind,card.Range);
    }
    operations.Add(new(id,card.Kind,card.Range,requests.ToImmutable(),coordinates) { PoleZero=poleZero,CoordinateUnit=unit,NestedDc=nestedDc,Arguments=values.ToImmutableArray(),OutputStartS=outputStart,InitializationRequest=initialization,InitialConditions=initialConditions,UsesUic=usesUic,ControlOrigin=directive.Origin });
   }
  }catch(PreparationFailure e){return Fail(e.Code,e.Range);}
  var observations=selected.Omissions.Where(o=>o.Kind=="extension").ToImmutableArray();
  if(!hasTransient)observations=observations.AddRange(catalog.InitialConditions.Select(ic=>
      new OperatingPointOmission(ic.ScopeId,"extension",".ic","initial_conditions_not_used_by_requested_analyses",ic.Range)));
  return new(operations.ToImmutable(),[]) { Observations=observations,CircuitPreparation=prepared,OperatingTemperatureCelsius=temperature.Limits.TemperatureCelsius,ControlProfile=control.Profile,ControlConstants=constants,ProgramOmissions=control.Omissions };
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
   if(operation.PoleZero is {} requestedRoots){
    cancellation.ThrowIfCancellationRequested();
    var roots=requestedRoots.Execute();
    results.Add(new(operation.Id,roots.Success,[]){PoleZero=roots});continue;
   }
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
 // A fresh nonlinear point can take more work around device transitions.
 // InitialStep=1 tries the physical sources first and reduces only on failure.
 // Switches can be hysteretic, so keep their existing initial-state policy.
 private static Analysis DcSweepAnalysis(SimulationRequest request)
 {
     var elements=request.Circuit.Elements;
     return elements.Any(e=>e is Diode or DiodeWithCapacitance or SpiceDiode or Mos1Channel or Bsim330)
         &&!elements.Any(e=>e is PspiceSwitch)
         ? new OperatingPointNewton(new NewtonOptions(MaxIterations:200,Continuation:new(InitialStep:1)))
         : request.Analysis;
 }
 private sealed class PreparationFailure(string code,SourceRange? range):Exception
 {internal string Code{get;}=code;internal SourceRange? Range{get;}=range;}
}
