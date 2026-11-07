using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>One evaluated root voltage IC; volts refer to the node relative to reference node zero.</summary>
public sealed record PreparedNodeInitialCondition(string OriginalNode,string WireNode,int NodeIndex,double Volts,SourceRange Range);
/// <summary>Actual execution request after optional public-client initial bias preparation. A failed bias publishes no transient request.</summary>
public sealed record RequestedExecutionPreparation(SimulationRequest? Request,SimulationResponse? InitializationResponse,string? Failure)
{
 /// <summary>True only when the original request or a complete explicitly initialized request is available.</summary>
 public bool Success=>Request!=null&&Failure==null;
}
public static partial class RequestedAnalysis
{
 private static bool Branch(Element element)=>element is Voltage or Inductor or Vcvs||element.GetType().Name=="Ccvs";
 private static ImmutableArray<PreparedNodeInitialCondition> BindInitialConditions(DeclarationCatalog catalog,ParameterEvaluation environment,BridgeResult prepared,bool uic=false){
  if(catalog.InitialConditions.IsEmpty)return [];
  var range=catalog.InitialConditions[0].Range;
  if(!uic&&prepared.Request!.Circuit.Elements.Any(e=>e is not (Resistor or Voltage or Current or Capacitor or Inductor or Vcvs or Vccs)))throw new PreparationFailure("unsupported_initial_condition_device",range);
  var result=ImmutableArray.CreateBuilder<PreparedNodeInitialCondition>();var targets=new HashSet<int>();var circuit=prepared.Request!.Circuit;
  foreach(var ic in catalog.InitialConditions)foreach(var node in ic.Nodes){
   if(!prepared.RootNodes.TryGetValue(node.Node.Text,out var wire))throw new PreparationFailure("initial_condition_node_missing",node.Node.Range);
   var index=Array.IndexOf(circuit.Nodes,wire);if(index<=0)throw new PreparationFailure("initial_condition_reference_node",node.Node.Range);
   if(!targets.Add(index))throw new PreparationFailure("duplicate_initial_condition",node.Range);
   if(!uic&&circuit.Elements.Any(e=>(e is Voltage or Vcvs)&&(e.P==index||e.N==index)))throw new PreparationFailure("initial_condition_source_constraint",node.Range);
   if(!uic&&!circuit.Elements.OfType<Capacitor>().Any(c=>c.P==index||c.N==index))throw new PreparationFailure("initial_condition_requires_dynamic_capacitor_node",node.Range);
   var evaluated=ParameterEvaluator.EvaluateValue(catalog,environment,node.Value);if(evaluated.Value is not {} value)throw new PreparationFailure(evaluated.Diagnostics[0].Code,node.Value.Range);
   result.Add(new(node.Node.Text,wire,index,value,node.Range));
  }
  if(!uic&&(circuit.Elements.Length+result.Count>1024||circuit.Nodes.Length-1+circuit.Elements.Count(Branch)+result.Count>128))throw new PreparationFailure("initial_condition_state_budget",range);
  return result.ToImmutable();
 }
 private static SimulationRequest ClampRequest(SimulationRequest request,string id,ImmutableArray<PreparedNodeInitialCondition> conditions){
  var elements=request.Circuit.Elements.ToList();var names=elements.Select(e=>e.Id).ToHashSet(StringComparer.Ordinal);
  foreach(var condition in conditions){var suffix=elements.Count;string name;do{name="frontend_ic_"+suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);suffix++;}while(!names.Add(name));elements.Add(new Voltage(name,condition.NodeIndex,0,condition.Volts));}
  return request with{CaseId=id+"_initial_bias",Circuit=request.Circuit with{Elements=elements.ToArray()},Analysis=new OperatingPoint(),Experiment=null};
 }
 /// <summary>Runs an optional temporary clamped OP through the public client, then reconstructs original MNA state by descriptor identity and unit. No missing component defaults to zero.</summary>
 /// <remarks>State order is original non-ground Circuit.Nodes followed by V/L/E/H branches in Circuit.Elements order. UIC inductor currents come from the admitted zero-current device profile. The temporary clamp branches are excluded.</remarks>
 public static async Task<RequestedExecutionPreparation> PrepareExecutionAsync(RequestedOperation operation,int point,SimulationClient client,CancellationToken cancellation=default){
  ArgumentNullException.ThrowIfNull(operation);ArgumentNullException.ThrowIfNull(client);if(point<0||point>=operation.Requests.Length)throw new ArgumentOutOfRangeException(nameof(point));cancellation.ThrowIfCancellationRequested();
  var request=operation.Requests[point];if(operation.InitializationRequest is not {} bias)return new(request,null,null);
  if(request.Analysis is not TransientConfigured||bias.Analysis is not OperatingPoint||(!operation.UsesUic&&operation.InitialConditions.IsEmpty)||operation.InitialConditions.Any(c=>c.NodeIndex<1||c.NodeIndex>=request.Circuit.Nodes.Length||request.Circuit.Nodes[c.NodeIndex]!=c.WireNode||!double.IsFinite(c.Volts))||operation.InitialConditions.Select(c=>c.NodeIndex).Distinct().Count()!=operation.InitialConditions.Length)return new(null,null,"invalid_initial_condition_plan");
  var response=await client.RunAsync(bias,cancellation);if(!response.Succeeded)return new(null,response,"initial_condition_bias_failed");
  try{
   var manifest=response.ReadManifest();if(manifest.CaseId!=bias.CaseId||manifest.Backend.Id!=(bias.Backend switch{"lu"=>"nalgebra_dense_lu_reference","faer"=>"faer_sparse_lu","qr"=>"nalgebra_dense_qr_reference",_=>"unsupported"})||manifest.Backend.Gpu||manifest.Backend.Placement!="cpu"||manifest.Samples.Length!=1||manifest.Samples[0].TimeS!=0||manifest.AcSamples.Length!=0||manifest.Signals.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=manifest.Signals.Length)throw new InvalidDataException("initial_condition_bias_identity_or_shape");
   double Read(string id,string unit,string quantity){var indices=Enumerable.Range(0,manifest.Signals.Length).Where(i=>manifest.Signals[i].Id==id&&manifest.Signals[i].Unit==unit&&manifest.Signals[i].Quantity==quantity).ToArray();if(indices.Length!=1)throw new InvalidDataException("initial_condition_state_descriptor_missing");var value=manifest.Samples[0].Values[indices[0]];return double.IsFinite(value)?value:throw new InvalidDataException("initial_condition_state_nonfinite");}
   var state=request.Circuit.Nodes.Skip(1).Select(n=>Read("v("+n+")","V","voltage")).Concat(request.Circuit.Elements.Where(Branch).Select(e=>operation.UsesUic&&e is Inductor?0:Read("i("+e.Id+")","A","current"))).ToArray();
   foreach(var condition in operation.InitialConditions){var actual=state[condition.NodeIndex-1];var tolerance=1e-10*(1+Math.Abs(condition.Volts));if(!double.IsFinite(tolerance)||Math.Abs(actual-condition.Volts)>tolerance)throw new InvalidDataException("initial_condition_clamp_not_satisfied");}
   if(request.Analysis is not TransientConfigured transient)throw new InvalidDataException("initial_condition_nontransient_request");
   return new(request with{Analysis=new TransientConfigured(transient.Options with{InitialState=state})},response,null);
  }catch(InvalidDataException e){return new(null,response,e.Message);}
 }
 /// <summary>Returns an explicit reporting-window view, while the response retains every raw engine sample from zero. No resampling or synthetic TSTART point is introduced.</summary>
 public static ResultManifest ReportedManifest(RequestedOperation operation,SimulationResponse response){
  ArgumentNullException.ThrowIfNull(operation);ArgumentNullException.ThrowIfNull(response);var manifest=response.ReadManifest();
  if(operation.Kind!="tran"||operation.OutputStartS==0)return manifest;
  var samples=manifest.Samples.Where(s=>s.TimeS>=operation.OutputStartS).ToArray();if(samples.Length==0)throw new InvalidDataException("empty_requested_reporting_window");return manifest with{Samples=samples};
 }
}
