using Simulator.Syntax;
using Simulator.Client;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
namespace Simulator.Frontend.Tests;
public sealed class RequestedAnalysisTests
{
 private static RequestedAnalysisPlan Prepare(string cards)=>RequestedAnalysis.Prepare(DeckParser.Parse("requested\nV1 a 0 DC 1 AC 1\nR1 a 0 1k\n"+cards+"\n.end\n",collectDeclarations:true));
 [Fact] public void Repeated_analyses_retain_source_order_and_fresh_dc_points()
 {
  var plan=Prepare(".op\n.dc V1 1 -1 -.5\n.ac lin 3 10 30\n.op\n.tran 1u 4u");
  Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
  Assert.Equal(new[]{"op","dc","ac","op","tran"},plan.Operations.Select(o=>o.Kind));
  Assert.Equal(new[]{1d,.5,0,-.5,-1},plan.Operations[1].Coordinates);
  Assert.Equal(new[]{10d,20,30},Assert.IsType<Ac>(plan.Operations[2].Requests.Single().Analysis).FrequenciesHz);
 }
 [Theory]
 [InlineData(".op\n.noise v(a) V1 dec 1 1 10")]
 [InlineData(".dc V1 0 1 0")]
 [InlineData(".dc V1 1 0 1")]
 [InlineData(".tran 1u 4u 0 1u uic")]
 [InlineData(".tran 1u 4u 1u")]
 [InlineData(".ac dec 0 1 1k")]
 [InlineData(".dc V1 0 1 1 V2 0 1 1")]
 [InlineData(".control\nop\n.endc")]
 [InlineData(".ic v(a)=1\n.op")]
 public void Unsupported_or_invalid_programs_publish_no_partial_operations(string cards)
 {var plan=Prepare(cards);Assert.False(plan.Success);Assert.Empty(plan.Operations);Assert.NotEmpty(plan.Diagnostics);}
 [Property(MaxTest=100,Replay="2357,20261103")]
 public bool Dc_direction_preserves_count_and_endpoints(PositiveInt value)
 {
  var count=2+value.Get%80;var plan=Prepare($".dc V1 {count} 0 -1");
  return plan.Success&&plan.Operations[0].Coordinates.Length==count+1&&plan.Operations[0].Coordinates[0]==count&&plan.Operations[0].Coordinates[^1]==0;
 }
 [Fact] public void Requested_transient_separates_original_source_defaults_and_computation_ceiling()
 {
  var syntax=DeckParser.Parse("default\nV1 a 0 EXP(0 1)\nR1 a 0 1k\n.tran 1u 100u 0 .2u\n.end\n",collectDeclarations:true);
  var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
  var options=Assert.IsType<TransientConfigured>(plan.Operations[0].Requests[0].Analysis).Options;
  Assert.Equal(.2e-6,options.MaxStepS,14);Assert.Equal(1e-6,Assert.IsType<Exponential>(options.Sources![0].Waveform).RiseTauS,14);
 }
 [Fact] public void Documented_two_point_linear_grid_is_preserved_despite_native_oracle_bug()
 {var plan=Prepare(".ac lin 2 10 30");Assert.True(plan.Success);Assert.Equal(new[]{10d,30},plan.Operations[0].Coordinates);}
 [Fact] public void No_analysis_is_an_explicit_refusal()=>Assert.False(Prepare("").Success);
 [Fact] public void Scalar_expressions_and_logarithmic_grid_are_evaluated()
 {var plan=Prepare(".param upper=1k\n.ac dec 2 10 {upper}");Assert.True(plan.Success);Assert.Equal(5,plan.Operations[0].Coordinates.Length);Assert.Equal(1000,plan.Operations[0].Coordinates[^1],8);}
 [Fact] public async Task Transport_failures_preserve_all_operation_and_point_identities()
 {
  var plan=Prepare(".dc V1 0 1 .5\n.op");
  var result=await RequestedAnalysis.ExecuteAsync(plan,new(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe")));
  Assert.Equal(2,result.Length);Assert.All(result,r=>Assert.False(r.Success));Assert.Equal(3,result[0].Points.Length);
  Assert.All(result.SelectMany(r=>r.Points),p=>{Assert.Null(p.Response);Assert.NotNull(p.TransportError);});
 }
 [Fact] public async Task Cancellation_propagates_before_starting_a_request()
 {
  using var cancelled=new CancellationTokenSource();cancelled.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>RequestedAnalysis.ExecuteAsync(Prepare(".op"),new("unused"),cancelled.Token));
 }
 [Fact] public async Task An_invalid_plan_cannot_be_executed()
 {await Assert.ThrowsAsync<ArgumentException>(()=>RequestedAnalysis.ExecuteAsync(Prepare(""),new("unused")));}
 [Theory]
 [InlineData(".ac weird 3 1 10")]
 [InlineData(".ac lin 3 10 1")]
 [InlineData(".ac lin 3 0 10")]
 [InlineData(".ac dec 10000 1 1meg")]
 [InlineData(".dc V1 0 1 1p")]
 [InlineData(".dc R1 0 1 1")]
 [InlineData(".dc missing 0 1 1")]
 [InlineData(".ac dec 1 1 {missing}")]
 [InlineData(".tran 0 1")]
 [InlineData(".tran 1 0")]
 public void Boundary_and_negative_inputs_are_refused_atomically(string cards)=>Assert.False(Prepare(cards).Success);
 [Fact] public void Observations_are_reported_and_dc_requests_own_their_arrays()
 {
  var plan=Prepare(".dc V1 0 1 1\n.print dc v(a)");Assert.True(plan.Success);Assert.Single(plan.Observations);
  var requests=plan.Operations[0].Requests;Assert.NotSame(requests[0].Circuit.Elements,requests[1].Circuit.Elements);
  Assert.Equal(0,Assert.IsType<Voltage>(requests[0].Circuit.Elements[0]).Volts);Assert.Equal(1,Assert.IsType<Voltage>(requests[1].Circuit.Elements[0]).Volts);
 }
 [Theory]
 [InlineData(".dc V1 0 0 1",1)]
 [InlineData(".dc V1 0 1 .3",4)]
 [InlineData(".ac lin 1 10 30",1)]
 [InlineData(".ac oct 2 10 40",5)]
 public void Endpoint_and_single_point_grid_contracts_are_explicit(string cards,int count)
 {var plan=Prepare(cards);Assert.True(plan.Success);Assert.Equal(count,plan.Operations[0].Coordinates.Length);}
 [Fact] public void Resource_and_nested_context_limits_are_checked()
 {
  Assert.Throws<ArgumentOutOfRangeException>(()=>RequestedAnalysis.Prepare(DeckParser.Parse("empty\n.end\n"),0));
  Assert.False(RequestedAnalysis.Prepare(DeckParser.Parse("nested\nR1 a 0 1k\n.subckt cell p n\n.op\n.ends\n.op\n.end\n",collectDeclarations:true)).Success);
  Assert.False(RequestedAnalysis.Prepare(DeckParser.Parse("broken\n.tran\n.end\n",collectDeclarations:true)).Success);
 }
 [Property(MaxTest=100,Replay="2359,20261103")]
 public bool Arbitrary_analysis_tails_are_bounded_and_do_not_publish_invalid_grids(NonNull<string> input)
 {
  var tail=input.Get.Length>256?input.Get[..256]:input.Get;
  var plan=Prepare(".ac "+tail);
  return !plan.Success||plan.Operations.All(o=>o.Kind!="ac"||o.Coordinates.Length is >0 and <=10000&&o.Coordinates.All(v=>double.IsFinite(v)&&v>0));
 }
 [Fact] public void Source_grid_regressions_challenge_nonunit_steps_nonzero_origins_and_exact_budget()
 {
  foreach(var item in new[]{(".dc V1 2 -2 -.5",new[]{2d,1.5,1,.5,0,-.5,-1,-1.5,-2}), (".dc V1 2 4 .5",new[]{2d,2.5,3,3.5,4}), (".dc V1 2 2 .5",new[]{2d})}){
   var plan=Prepare(item.Item1);Assert.True(plan.Success);Assert.Equal(item.Item2,plan.Operations[0].Coordinates);Assert.Equal("V",plan.Operations[0].CoordinateUnit);
  }
  var syntax=DeckParser.Parse("budget\nV1 a 0 1\nR1 a 0 1k\n.dc V1 0 2 1\n.end\n",collectDeclarations:true);
  var refused=RequestedAnalysis.Prepare(syntax,2);Assert.False(refused.Success);Assert.Empty(refused.Operations);
  Assert.Contains(refused.Diagnostics,d=>d.Code=="requested_point_budget");
  var nearBoundary=DeckParser.Parse("budget\\nV1 a 0 1\\nR1 a 0 1k\\n.dc V1 0 1.99999999999 1\\n.end\\n".Replace("\\n","\n"),collectDeclarations:true);
  Assert.False(RequestedAnalysis.Prepare(nearBoundary,2).Success);
 }
 [Fact] public void Current_source_sweeps_retain_ampere_units_and_typed_replacements()
 {
  var syntax=DeckParser.Parse("current\nI1 a 0 1m\nR1 a 0 1k\n.dc I1 -.001 .001 .001\n.end\n",collectDeclarations:true);
  var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);var operation=plan.Operations[0];Assert.Equal("A",operation.CoordinateUnit);
  Assert.Equal(new[]{-.001,0,.001},operation.Requests.Select(r=>Assert.IsType<Current>(r.Circuit.Elements[0]).Amps));
 }
}