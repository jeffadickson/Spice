using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class TransientSelectionTests
{
 [Fact]
 public void Modulation_defaults_and_native_index_clipping_are_explicit()
 {
  var selected=TransientSelection.Prepare(Deck("SFFM(.1 2)"),2,.1);
  Assert.True(selected.Success);var fm=Assert.IsType<FrequencyModulated>(Assert.Single(Assert.IsType<TransientConfigured>(selected.Prepared.Request!.Analysis).Options.Sources!).Waveform);
  Assert.Equal(2.5,fm.CarrierHz);Assert.Equal(250,fm.ModulationHz);Assert.Equal(.01,fm.ModulationIndex,12);
  var am=TransientSelection.Prepare(Deck("AM(.1 2)"),2,.1);Assert.True(am.Success);
  var amplitude=Assert.IsType<AmplitudeModulated>(Assert.Single(Assert.IsType<TransientConfigured>(am.Prepared.Request!.Analysis).Options.Sources!).Waveform);
  Assert.Equal(1,amplitude.ModulationAmplitude);Assert.Equal(2.5,amplitude.ModulationHz);Assert.Equal(250,amplitude.CarrierHz);
  var negative=TransientSelection.Prepare(Deck("SFFM(0 1 1 -2 2)"),2,.1);Assert.True(negative.Success);
  Assert.Equal(0,Assert.IsType<FrequencyModulated>(Assert.Single(Assert.IsType<TransientConfigured>(negative.Prepared.Request!.Analysis).Options.Sources!).Waveform).ModulationIndex);
 }
 private static DeckSyntax Deck(string profile)=>DeckParser.Parse("Transient\nV1 out 0 "+profile+"\nR1 out 0 1k\n.tran 1u 1m\n.end\n",collectDeclarations:true);
 [Fact]
 public void Explicit_pulse_maps_to_existing_wire_contract_and_preserves_selection_notes()
 {
  var selected=TransientSelection.Prepare(Deck("PULSE(0 1 1u .1u .2u 1u 3u)"),10e-6,.1e-6);
  Assert.True(selected.Success);var analysis=Assert.IsType<TransientConfigured>(selected.Prepared.Request!.Analysis);
  var pulse=Assert.IsType<Pulse>(Assert.Single(analysis.Options.Sources!).Waveform);Assert.Equal(1e-6,pulse.DelayS,14);Assert.Equal(3e-6,pulse.PeriodS,14);
  Assert.DoesNotContain(selected.Omissions,o=>o.Kind=="source_transient");Assert.Contains(selected.Omissions,o=>o.Kind=="tran"&&o.Reason=="analysis_not_selected");
  using var json=JsonDocument.Parse(JsonSerializer.Serialize(selected.Prepared.Request,WireFormat.Options));
  Assert.Equal("pulse",json.RootElement.GetProperty("analysis").GetProperty("options").GetProperty("sources")[0].GetProperty("waveform").GetProperty("kind").GetString());
 }
 [Fact]
 public void Sine_and_exponential_defaults_are_bound_to_the_requested_analysis()
 {
  var sine=TransientSelection.Prepare(Deck("SIN(1 2)"),2,.1);
  var s=Assert.IsType<DampedSine>(Assert.Single(Assert.IsType<TransientConfigured>(sine.Prepared.Request!.Analysis).Options.Sources!).Waveform);Assert.Equal(.5,s.FrequencyHz);
  var exp=TransientSelection.Prepare(Deck("EXP(1 2 0 0 0 0)"),10e-6,1e-6);
  var e=Assert.IsType<Exponential>(Assert.Single(Assert.IsType<TransientConfigured>(exp.Prepared.Request!.Analysis).Options.Sources!).Waveform);
  Assert.Equal(1e-6,e.RiseDelayS);Assert.Equal(1e-6,e.RiseTauS);Assert.Equal(2e-6,e.FallDelayS);Assert.Equal(1e-6,e.FallTauS);
 }
 [Fact]
 public void Five_field_pulse_uses_ngspice_zero_width_rule()
 {
  var result=TransientSelection.Prepare(Deck("PULSE(0 1 1u .1u .1u)"),10e-6,.1e-6);
  Assert.True(result.Success);var p=Assert.IsType<Pulse>(Assert.Single(Assert.IsType<TransientConfigured>(result.Prepared.Request!.Analysis).Options.Sources!).Waveform);Assert.Equal(0,p.WidthS);Assert.Equal(10e-6,p.PeriodS);
 }
 [Theory]
 [InlineData("PULSE(0 1)",0,1e-6)]
 [InlineData("PULSE(0 1 2u .5u)",2e-6,.5e-6)]
 public void Short_pulse_preserves_first_rise_and_holds_through_stop(string profile,double delay,double rise)
 {
  var result=TransientSelection.Prepare(Deck(profile),10e-6,1e-6);
  Assert.True(result.Success,string.Join(',',result.Prepared.Diagnostics.Select(d=>d.Code)));
  var config=Assert.IsType<TransientConfigured>(result.Prepared.Request!.Analysis).Options;
  var ramp=Assert.IsType<Pwl>(Assert.Single(config.Sources!).Waveform);
  Assert.Equal(0,ramp.Points[0].TimeS);
  Assert.Equal(0,ramp.Points[0].Value);
  if(delay>0){Assert.Equal(delay,ramp.Points[1].TimeS,14);Assert.Equal(0,ramp.Points[1].Value);}
  Assert.Equal(delay+rise,ramp.Points[^1].TimeS,14);
  Assert.Equal(1,ramp.Points[^1].Value);
 }
 [Theory]
 [InlineData("DC 1 PWL(0 0 1u 1)","unsupported_transient_dc_bias")]
 [InlineData("DC 1 SFFM(0 1 1k 1 10k)","unsupported_transient_dc_bias")]
 [InlineData("PULSE(0 1 0 1u 1u 2u 3u)","unsupported_pulse_overlap")]
 [InlineData("SIN(0 1 -1)","unsupported_sine_profile")]
 [InlineData("EXP(1 2 1u -1u 2u 1u)","unsupported_exponential_profile")]
 [InlineData("AM(0 1 1 0 1)","unsupported_modulated_source_profile")]
 [InlineData("SFFM(0 1 -1 1 1)","unsupported_modulated_source_profile")]
 public void Unqualified_runtime_profiles_refuse_without_partial_requests(string profile,string code)
 {
  var result=TransientSelection.Prepare(Deck(profile),10e-6,1e-6);
  Assert.False(result.Success);Assert.Equal(code,Assert.Single(result.Prepared.Diagnostics).Code);Assert.Null(result.Prepared.Request);Assert.Empty(result.Omissions);Assert.Empty(result.Prepared.SourceWaveforms);
 }
 [Theory]
 [InlineData(0,1e-6,100)]
 [InlineData(1e-6,2e-6,100)]
 [InlineData(1e-3,1e-6,2)]
 public void Invalid_time_and_step_budgets_are_structured_refusals(double stop,double step,int count)
 {var result=TransientSelection.Prepare(Deck("PWL(0 0 1u 1)"),stop,step,count);Assert.False(result.Success);Assert.Null(result.Prepared.Request);}
}
