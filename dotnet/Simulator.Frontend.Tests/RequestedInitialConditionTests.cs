using Simulator.Client;
using Simulator.Syntax;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
namespace Simulator.Frontend.Tests;
public sealed class RequestedInitialConditionTests
{
 private static DeckSyntax Deck(string cards)=>DeckParser.Parse("initial\n.param level=1\nV1 in 0 0\nR1 in out 1k\nC1 out 0 1u\n"+cards+"\n.end\n",collectDeclarations:true);
 [Fact] public void Root_voltage_initial_conditions_retain_generated_fields_and_prepare_temporary_clamps(){
  var syntax=Deck(".ic v(out)={level}\n.tran .1u 10u");Assert.Single(syntax.Declarations!.InitialConditions);
  var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));Assert.NotNull(plan.CircuitPreparation);
  var operation=Assert.Single(plan.Operations);Assert.Single(operation.InitialConditions);Assert.Equal(1,operation.InitialConditions[0].Volts);Assert.NotNull(operation.InitializationRequest);Assert.Equal(4,operation.InitializationRequest!.Circuit.Elements.Length);
 }
 [Fact] public void Nonzero_start_retains_full_integration_and_reports_a_separate_window(){
  var plan=RequestedAnalysis.Prepare(Deck(".tran 10u 100u 50u"));Assert.True(plan.Success);var op=Assert.Single(plan.Operations);Assert.Equal(50e-6,op.OutputStartS,12);var options=Assert.IsType<TransientConfigured>(op.Requests[0].Analysis).Options;Assert.Equal(100e-6,options.StopS,12);Assert.Equal(1e-6,options.MaxStepS,12);
 }
 [Theory]
 [InlineData(".ic v(out)=1 v(OUT)=1\n.tran 1u 10u","duplicate_initial_condition")]
 [InlineData(".ic v(in)=1\n.tran 1u 10u","initial_condition_source_constraint")]
 [InlineData(".ic v(missing)=1\n.tran 1u 10u","initial_condition_node_missing")]
 [InlineData(".ic i(L1)=1\n.tran 1u 10u","unsupported_initial_condition_profile")]
 [InlineData(".ic v(out)={missing}\n.tran 1u 10u","missing_parameter")]
 [InlineData(".tran 1u 10u 11u","invalid_transient_start")]
 public void Unsupported_or_inconsistent_IC_and_windows_refuse_atomically(string cards,string code){var plan=RequestedAnalysis.Prepare(Deck(cards));Assert.False(plan.Success);Assert.Empty(plan.Operations);Assert.Contains(plan.Diagnostics,d=>d.Code==code);}
 [Fact] public void Root_IC_without_transient_is_recorded_but_does_not_clamp_an_OP(){
  var plan=RequestedAnalysis.Prepare(Deck(".ic v(out)=1\n.op"));
  Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
  var operation=Assert.Single(plan.Operations);
  Assert.Null(operation.InitializationRequest);
  Assert.Empty(operation.InitialConditions);
  Assert.Contains(plan.Observations,o=>o.Reason=="initial_conditions_not_used_by_requested_analyses");
 }
 [Fact] public void Ordinary_short_UIC_spelling_uses_zero_default_capacitor_voltage(){var plan=RequestedAnalysis.Prepare(Deck(".tran 1u 10u uic"));Assert.True(plan.Success);Assert.True(Assert.Single(plan.Operations).UsesUic);}
 [Property(MaxTest=100,Replay="2381,20261103")]
 public bool Initial_condition_bias_preserves_the_original_circuit_and_unique_branch_identity(NonNegativeInt input){
  var value=input.Get%2001-1000;var plan=RequestedAnalysis.Prepare(Deck(".ic v(out)="+value.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\n.tran 1u 10u"));if(!plan.Success)return false;
  var physical=plan.CircuitPreparation!.Request!.Circuit;var initial=plan.Operations[0].InitializationRequest!.Circuit;
  return physical.Elements.Length==3&&initial.Elements.Length==4&&initial.Elements.Select(e=>e.Id).Distinct(StringComparer.Ordinal).Count()==4&&initial.Elements[^1] is Voltage clamp&&clamp.Volts==value&&clamp.P==2&&clamp.N==0;
 }
 [Fact] public async Task Initial_preparation_observes_cancellation_even_without_a_bias_job(){
  var op=RequestedAnalysis.Prepare(Deck(".op")).Operations[0];using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>RequestedAnalysis.PrepareExecutionAsync(op,0,new("unused"),cancel.Token));
 }
 [Fact] public async Task Tampered_initial_metadata_is_rejected_before_launching_a_bias_child(){
  var op=RequestedAnalysis.Prepare(Deck(".ic v(out)=1\n.tran 1u 10u")).Operations[0];
  foreach(var condition in new[]{op.InitialConditions[0] with{NodeIndex=99},op.InitialConditions[0] with{WireNode="wrong"},op.InitialConditions[0] with{Volts=double.NaN}}){var result=await RequestedAnalysis.PrepareExecutionAsync(op with{InitialConditions=[condition]},0,new("unused"));Assert.False(result.Success);Assert.Null(result.Request);Assert.Null(result.InitializationResponse);Assert.Equal("invalid_initial_condition_plan",result.Failure);}
 }
 [RequestedNativeFact] public async Task Linear_RC_initial_bias_uses_public_clamp_solve_and_never_substitutes_a_default_state(){
  var plan=RequestedAnalysis.Prepare(Deck(".ic v(out)=1\n.tran .1u 10u"));Assert.True(plan.Success);var op=Assert.Single(plan.Operations);var client=new SimulationClient(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!);
  var execution=await RequestedAnalysis.PrepareExecutionAsync(op,0,client);Assert.True(execution.Success,execution.Failure);var options=Assert.IsType<TransientConfigured>(execution.Request!.Analysis).Options;Assert.NotNull(options.InitialState);Assert.Equal(3,options.InitialState!.Length);Assert.Equal(0,options.InitialState[0],12);Assert.Equal(1,options.InitialState[1],12);Assert.Equal(.001,options.InitialState[2],12);
  var results=await RequestedAnalysis.ExecuteAsync(plan,client);var point=Assert.Single(Assert.Single(results).Points);Assert.True(point.Response!.Succeeded);Assert.NotNull(point.InitializationResponse);Assert.NotNull(point.ReportedManifest);var raw=point.Response.ReadManifest();var voltage=Array.FindIndex(raw.Signals,s=>s.Id=="v(n2)");Assert.True(voltage>=0);Assert.Equal(1,raw.Samples[0].Values[voltage],12);
  var current=Array.FindIndex(raw.Signals,s=>s.Id=="i(e0)");Assert.True(current>=0);
  // Independent RC continuum solution and a first-order BE bound, not a copied solver recurrence.
  const double tau=.001;var step=Assert.IsType<TransientConfigured>(op.Requests[0].Analysis).Options.MaxStepS;
  foreach(var sample in raw.Samples){var exact=Math.Exp(-sample.TimeS/tau);var bound=exact*(Math.Exp(step*sample.TimeS/(2*tau*tau))-1)+1e-11;Assert.InRange(Math.Abs(sample.Values[voltage]-exact),0,bound);Assert.InRange(Math.Abs(sample.Values[current]-sample.Values[voltage]/1000),0,1e-11);}
 }
 [RequestedNativeFact] public async Task Public_state_descriptors_preserve_inductor_and_E_source_branch_order(){
  var client=new SimulationClient(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!);
  foreach(var fixture in new[]{("V1 in 0 0\nR1 in middle 1k\nL1 middle out 1m\nC1 out 0 1u",new[]{0d,1,1,.001,-.001}),("V1 in 0 1\nE1 drive 0 in 0 2\nR1 drive out 1k\nC1 out 0 1u",new[]{1d,2,1,0,-.001})}){
   var syntax=DeckParser.Parse("layout\n"+fixture.Item1+"\n.ic v(out)=1\n.tran .01u 1u\n.end\n",collectDeclarations:true);var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);var request=await RequestedAnalysis.PrepareExecutionAsync(plan.Operations[0],0,client);Assert.True(request.Success,request.Failure);var values=Assert.IsType<TransientConfigured>(request.Request!.Analysis).Options.InitialState!;Assert.Equal(fixture.Item2.Length,values.Length);for(var i=0;i<values.Length;i++)Assert.Equal(fixture.Item2[i],values[i],10);
  }
 }
 [RequestedNativeFact] public async Task Failed_clamped_bias_publishes_no_state_and_preserves_the_later_original_OP(){
  var syntax=DeckParser.Parse("conflict\nV1 in 0 1\nL1 in out 1m\nC1 out 0 1u\n.ic v(out)=0\n.tran .01u 1u\n.op\n.end\n",collectDeclarations:true);var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
  var result=await RequestedAnalysis.ExecuteAsync(plan,new(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!));Assert.False(result[0].Success);var point=Assert.Single(result[0].Points);Assert.Null(point.Response);Assert.Null(point.ReportedManifest);Assert.NotNull(point.InitializationResponse);Assert.False(point.InitializationResponse!.Succeeded);Assert.Equal("initial_condition_bias_failed",point.TransportError);Assert.True(result[1].Success);
 }
 [RequestedNativeFact] public async Task TSTART_reporting_retains_raw_integration_history_without_synthetic_samples(){
  var syntax=Deck(".tran .1u 4u 1.01u .1u");var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);var result=await RequestedAnalysis.ExecuteAsync(plan,new(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!));var point=Assert.Single(Assert.Single(result).Points);Assert.True(point.Response!.Succeeded);var raw=point.Response.ReadManifest();Assert.Equal(0,raw.Samples[0].TimeS);Assert.NotEmpty(point.ReportedManifest!.Samples);Assert.True(point.ReportedManifest.Samples.Length<raw.Samples.Length);Assert.All(point.ReportedManifest.Samples,s=>Assert.True(s.TimeS>=1.01e-6));Assert.Equal(raw.Samples[^1].TimeS,point.ReportedManifest.Samples[^1].TimeS);Assert.Equal(raw.Samples[^1].Values,point.ReportedManifest.Samples[^1].Values);
 }
}
public sealed class RequestedInitialNativeOracleTests
{
 [RequestedNativeFact] public async Task Original_node_IC_and_nonzero_reporting_windows_match_native_without_copying_analysis_fields(){
  var parent=Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")!;var folder=Path.Combine(parent,"initial-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  var fixtures=new Dictionary<string,string>{
   ["rc.cir"]="rc initial\n.param level=.5 dt=.01u duration=10u\nR1 out 0 1k\nC1 out 0 1u\n.ic v(out)={level*2}\n.tran {dt} {duration}\n.end\n",
   ["rlc.cir"]="rlc initial\nV1 in 0 0\nR1 in middle 1k\nL1 middle out 1m\nC1 out 0 1u\n.ic v(out)=1\n.tran .001u 1u\n.end\n",
   ["e-branch.cir"]="E initial\nV1 in 0 1\nE1 drive 0 in 0 2\nR1 drive out 1k\nC1 out 0 1u\n.ic v(out)=1\n.tran .001u 1u\n.end\n",
   ["op-ic.cir"]="op ignores ic\nV1 in 0 2\nR1 in out 1k\nR2 out 0 1k\n.ic v(out)=99\n.op\n.end\n",
   ["window.cir"]="window\nV1 in 0 DC 0 PWL(0 0 4u 4)\nR1 in out 1k\nR2 out 0 1k\n.tran .1u 4u 1.101u .1u\n.end\n",
   ["window-gap.cir"]="window gap\nV1 in 0 DC 0 PWL(0 0 4u 4)\nR1 in out 1k\nR2 out 0 1k\n.tran .1u 4u 1.01u .1u\n.end\n"};
  foreach(var item in fixtures)await File.WriteAllTextAsync(Path.Combine(folder,item.Key),item.Value);var inventory=Path.Combine(folder,"inventory.json");var rows=fixtures.Keys.Select(name=>new{scope="ngspice",path=name,result=new{Sha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name))))}});await File.WriteAllTextAsync(inventory,System.Text.Json.JsonSerializer.Serialize(new{rows}));
  var root=Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.Parent!.FullName;var output=Path.Combine(folder,"output");var exit=await Simulator.RequestedQualification.RequestedCorpus.RunAsync([root,folder,Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output,"300"]);Assert.Equal(0,exit);
  using var report=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"summary.json")));Assert.True(report.RootElement.GetProperty("provenance_valid").GetBoolean());foreach(var row in report.RootElement.GetProperty("rows").EnumerateArray()){if(row.GetProperty("path").GetString()=="window-gap.cir"){Assert.True(row.GetProperty("requested_executed").GetBoolean());Assert.False(row.GetProperty("native_agreed").GetBoolean());Assert.Equal("transient_extrapolation",row.GetProperty("operations")[0].GetProperty("status").GetString());}else Assert.True(row.GetProperty("native_agreed").ValueKind==System.Text.Json.JsonValueKind.True,row.GetRawText());}
  foreach(var file in Directory.GetFiles(output,"original-analysis.cir",SearchOption.AllDirectories)){var projected=await File.ReadAllTextAsync(file);if(projected.StartsWith("op ignores ic",StringComparison.Ordinal)){Assert.Contains(".op",projected);Assert.Contains(".ic v(out)=99",projected);}else{Assert.Contains(".tran",projected);if(!projected.StartsWith("window",StringComparison.Ordinal))Assert.Contains(".ic v(out)=",projected);}}
 }
 private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}