using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class RequestedDcSourceBindingTests
{
 private static DeckSyntax Syntax(string cards="")=>DeckParser.Parse("DC original identity\nV1 in 0 1\nR1 in 0 1k\n"+cards+"\n.end\n",collectDeclarations:true);
 [Fact] public void Synthetic_behavioral_and_external_model_names_are_not_original_DC_targets(){
  var syntax=Syntax();var prepared=OperatingPointSelection.Select(syntax).Prepared;var physical=prepared.Origins[0];
  foreach(var name in new[]{"B1$offset","A1$offset"}){
   var synthetic=physical with{LocalName=name};var result=RequestedDcSourceBinding.Resolve(syntax.Declarations!,prepared with{Origins=[synthetic,synthetic]},name);Assert.Null(result.Source);Assert.Equal("unsupported_dc_source",result.Error);
  }
 }
 [Fact] public void Original_range_disambiguates_a_synthetic_name_and_duplicate_physical_origins_refuse(){
  var syntax=Syntax();var prepared=OperatingPointSelection.Select(syntax).Prepared;var original=prepared.Origins[0];var different=original with{Range=prepared.Origins[1].Range};
  var result=RequestedDcSourceBinding.Resolve(syntax.Declarations!,prepared with{Origins=prepared.Origins.Add(different)},"V1");Assert.IsType<Voltage>(result.Source);Assert.Null(result.Error);
  result=RequestedDcSourceBinding.Resolve(syntax.Declarations!,prepared with{Origins=prepared.Origins.Add(original)},"V1");Assert.Null(result.Source);Assert.Equal("ambiguous_dc_source_origin",result.Error);
 }
 [Fact] public void Missing_or_wrong_wire_type_is_a_structured_mapping_gap(){
  var syntax=Syntax();var prepared=OperatingPointSelection.Select(syntax).Prepared;var origin=prepared.Origins[0];var request=prepared.Request!;
  var missing=prepared with{Origins=prepared.Origins.SetItem(0,origin with{WireId="missing"})};Assert.Equal("dc_source_wire_mapping_gap",RequestedDcSourceBinding.Resolve(syntax.Declarations!,missing,"V1").Error);
  var wrong=request.Circuit.Elements.Select(e=>e is Voltage v?(Element)new Current(v.Id,v.P,v.N,v.Volts):e).ToArray();Assert.Equal("dc_source_wire_mapping_gap",RequestedDcSourceBinding.Resolve(syntax.Declarations!,prepared with{Request=request with{Circuit=request.Circuit with{Elements=wrong}}},"V1").Error);
 }
 [Theory]
 [InlineData("B1 in 0 V=v(in)+2\n.dc B1$offset 0 1 1")]
 [InlineData("B1 in 0 V=v(in)+2\nB1$offset other 0 V=2\nR2 other 0 1k\n.dc B1$offset 0 1 1")]
 public void Behavioral_real_name_collisions_never_publish_partial_DC_requests_or_throw(string cards){var plan=RequestedAnalysis.Prepare(Syntax(cards));Assert.False(plan.Success);Assert.Empty(plan.Operations);Assert.NotEmpty(plan.Diagnostics);}
}
