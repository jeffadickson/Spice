using System.Text.Json;
using System.Collections.Immutable;
using Simulator.Client;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Synthetic decomposition currents must not become invented native device names.</summary>
public sealed class RequestedOriginalObservableTests
{
 [Theory]
 [InlineData(false)]
 [InlineData(true)]
 public void Only_original_branch_currents_are_compared_even_when_a_real_name_contains_a_synthetic_suffix(bool collision)
 {
  var text="Original identity\nV1 in 0 1\nA1 in out amp\nR1 out 0 1k\n.model amp gain(gain=1 out_offset=1)\n"+
   (collision?"A1$offset in other amp2\nR2 other 0 1k\n.model amp2 gain(gain=2 out_offset=1)\n":"")+".op\n.end\n";
  var syntax=DeckParser.Parse(text,collectDeclarations:true);var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
  var prepared=plan.CircuitPreparation!;var operation=Assert.Single(plan.Operations);
  var nativeVariables=new List<RawVariable>();var nativeValues=new List<ComplexValue>();
  void Native(string name,string unit,double value){nativeVariables.Add(new(name,unit));nativeValues.Add(new(value,0));}
  Native("v(in)","voltage",1);Native("v(out)","voltage",2);
  if(collision)Native("v(other)","voltage",3);
  Native("i(v1)","current",0);Native("a1#branch","current",-.002);
  if(collision)Native("a1$offset#branch","current",-.003);
  var raw=new NativeRaw(false,nativeVariables.ToImmutableArray(),[nativeValues.ToArray()]){PlotName="Operating Point"};
  var signals=new List<SignalInfo>();var values=new List<double>();
  foreach(var node in prepared.Request!.Circuit.Nodes.Skip(1)){
   signals.Add(new("v("+node+")","voltage","V"));
   values.Add(node==prepared.RootNodes["out"]?2:collision&&node==prepared.RootNodes["other"]?3:1);
  }
  var first=syntax.Declarations!.XspiceInstances[0].Range;
  foreach(var element in prepared.Request.Circuit.Elements.Where(e=>e is Voltage or Vcvs)){
   signals.Add(new("i("+element.Id+")","current","A"));
   var origin=prepared.Origins.Single(o=>o.WireId==element.Id);
   values.Add(origin.LocalName=="V1"?0:origin.Range==first?-.002:-.003);
  }
  var manifest=new ResultManifest("1",operation.Requests[0].CaseId,"succeeded",true,signals.ToArray(),[new(0,values.ToArray())],[],[],[],null,new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");
  var response=new SimulationResponse("1","succeeded",true,JsonSerializer.SerializeToElement(manifest,WireFormat.Options));
  var compared=RequestedComparison.Compare(operation,prepared,[response],raw);
  Assert.True(compared.Agreed,string.Join(',',compared.Gaps));
  Assert.Equal(nativeVariables.Count,compared.Observables.Length);
 }
}
