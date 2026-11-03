using Simulator.Syntax;
using Simulator.Client;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class SourceWaveformTests
{
 [Theory]
 [InlineData("PULSE(-1 2 1u 1n 1n 1u 2u)",-1)]
 [InlineData("pulse -1 2",-1)]
 [InlineData("SIN(1 2 1k 0 0 90)",3)]
 [InlineData("SIN(0 1 1k 0 0 -90)",-1)]
 [InlineData("SIN(1 2)",1)]
 [InlineData("SIN(1 2 1k 0 0)",1)]
 [InlineData("PULSE(0 1 0 1n 1n 1u 2u)",0)]
 [InlineData("EXP(1 2)",1)]
 [InlineData("EXP(1 2 1u 2u)",1)]
 [InlineData("SIN(0 1 1 1 0 90)",1)]
 [InlineData("PWL(0 2)",2)]
 [InlineData("DC -2 SIN(1 2 1k 0 0 90) AC .2 -90",-2)]
 [InlineData("PWL(0 -1 1u 2)",-1)]
 [InlineData("EXP(1 2 1u 2u 3u 4u)",1)]
 [InlineData("2 PULSE(0 1 1u 1n 1n 1u 2u) AC 3",2)]
 [InlineData("SFFM(.1 2 200 45 10k 1m 0 0)",0)]
 [InlineData("AM(.1 1 2 200 10k 1m 0 0)",0)]
 public void Static_initial_value_is_selected_without_erasing_the_waveform(string profile,double expected)
 {
  var syntax=DeckParser.Parse("Waveform\nV1 out 0 "+profile+"\nR1 out 0 1k\n.end\n",collectDeclarations:true);
  Assert.Equal("accepted_structure",syntax.Status);
  var selected=OperatingPointSelection.Select(syntax);
  Assert.True(selected.Success,string.Join(',',selected.Prepared.Diagnostics.Select(d=>d.Code)));
  Assert.Equal(expected,Assert.Single(selected.Prepared.Request!.Circuit.Elements.OfType<Voltage>()).Volts,10);
  Assert.False(OperatingPointBridge.Lower(syntax).Success);
  Assert.Single(selected.Prepared.SourceWaveforms);Assert.Contains(selected.Omissions,o=>o.Kind=="source_transient");
 }
 [Theory]
 [InlineData("SIN(1)")]
 [InlineData("PULSE(0)")]
 [InlineData("EXP(1)")]
 [InlineData("SIN(0 1 1k -1u)")]
 [InlineData("PWL(-1u 1 1u 2)")]
 [InlineData("PULSE(0 1 0 1n 1n 1u 2u 1)")]
 [InlineData("SIN(0 1 1k 0 0 0 2)")]
 [InlineData("PWL(0 1 0 2)")]
 [InlineData("EXP(1 2 -1u 1u 2u 1u)")]
 [InlineData("EXP(1 2 1u 1u -2u 1u)")]
 [InlineData("PULSE(0 1 -1u 1n 1n 1u 2u)")]
 [InlineData("PWL(0 1 2)")]
 [InlineData("PWL(1u 1 0 2)")]
 [InlineData("DC 1 DC 2 PULSE(0 1)")]
 public void Unsupported_or_duplicate_profiles_remain_atomic(string profile)
 {
  var result=OperatingPointSelection.Select(DeckParser.Parse("Waveform\nV1 out 0 "+profile+"\nR1 out 0 1k\n.end\n",collectDeclarations:true)).Prepared;
  Assert.False(result.Success);Assert.Null(result.Request);Assert.Empty(result.Origins);Assert.Empty(result.SourceWaveforms);
 }
 [Fact]
 public void Current_source_waveform_parameters_are_evaluated_in_the_instance_scope()
 {
  var deck="Scoped waveform\n.param a=1m\nX1 out load amp={a}\n.subckt load p amp=2m\nI1 p 0 PWL(0 {-amp} 1u {amp})\nR1 p 0 1k\n.ends\n.end\n";
  var selected=OperatingPointSelection.Select(DeckParser.Parse(deck,collectDeclarations:true)).Prepared;
  Assert.True(selected.Success,string.Join(',',selected.Diagnostics.Select(d=>d.Code)));
  Assert.Equal(-.001,Assert.Single(selected.Request!.Circuit.Elements.OfType<Current>()).Amps);
  var wave=Assert.Single(selected.SourceWaveforms);Assert.Equal(-.001,wave.Parameters[1]);Assert.Equal(.001,wave.Parameters[3]);
 }
 [FsCheck.Xunit.Property(MaxTest=64,Replay="929,20261103")]
 public bool Clause_order_does_not_change_independent_DC_AC_and_signed_waveform_fields(int a,int b,int c)
 {
  var culture=System.Globalization.CultureInfo.InvariantCulture;
  var dc=(a%101)/10.0;var low=b%101;var high=c%101;var ac=Math.Abs(c%17);
  var wave="PULSE("+low.ToString(culture)+" "+high.ToString(culture)+" 1u .1u .1u 1u 3u)";
  var first="DC "+dc.ToString("R",culture)+" AC "+ac.ToString(culture)+" "+wave;
  var second=wave+" AC "+ac.ToString(culture)+" DC "+dc.ToString("R",culture);
  BridgeResult Prepare(string profile)=>OperatingPointSelection.Select(DeckParser.Parse("Property\nV1 out 0 "+profile+"\nR1 out 0 1k\n.end\n",collectDeclarations:true)).Prepared;
  var x=Prepare(first);var y=Prepare(second);
  return x.Success&&y.Success&&x.Request!.Circuit.Elements.OfType<Voltage>().Single().Volts==dc&&y.Request!.Circuit.Elements.OfType<Voltage>().Single().Volts==dc&&x.AcSources.Single().Real==ac&&y.AcSources.Single().Real==ac&&x.SourceWaveforms.Single().Parameters.SequenceEqual(y.SourceWaveforms.Single().Parameters);
 }
}
