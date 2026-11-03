using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
namespace Simulator.Frontend.Tests;
public sealed class RequestedQualificationTests
{
 private const string OpRaw="Title: test\nPlotname: Operating Point\nFlags: real\nNo. Variables: 2\nNo. Points: 1\nVariables:\n0 v(a) voltage\n1 i(v1) current\nValues:\n0 1\n-.001\n";
 [Fact] public void Native_raw_retains_order_dimensions_and_signed_values(){var raw=AsciiRaw.Read(OpRaw);Assert.False(raw.Complex);Assert.Equal("i(v1)",raw.Variables[1].Name);Assert.Equal(-.001,raw.Points[0][1].Real);}
 [Theory]
 [InlineData("NaN")][InlineData("Infinity")][InlineData("junk")]
 public void Nonfinite_and_malformed_values_are_refused(string value)=>Assert.Throws<InvalidDataException>(()=>AsciiRaw.Read(OpRaw.Replace("-.001",value,StringComparison.Ordinal)));
 [Fact] public void Duplicate_columns_extra_plots_truncation_and_wrong_indices_are_refused(){foreach(var raw in new[]{OpRaw.Replace("i(v1)","v(a)",StringComparison.Ordinal),OpRaw+OpRaw,OpRaw[..^7],OpRaw.Replace("0 1\n","1 1\n",StringComparison.Ordinal),OpRaw.Replace("Flags: real","Flags: real padded",StringComparison.Ordinal)})Assert.Throws<InvalidDataException>(()=>AsciiRaw.Read(raw));}
 [Property(MaxTest=100,Replay="2371,20261103")]
 public bool Malformed_raw_tail_never_becomes_additional_successful_data(NonNull<string> value){var suffix=value.Get.Length>256?value.Get[..256]:value.Get;if(string.IsNullOrWhiteSpace(suffix))return true;try{AsciiRaw.Read(OpRaw+suffix);return false;}catch(InvalidDataException){return true;}}
 [Fact] public void Logarithmic_frequency_display_metadata_preserves_complex_columns()
 {
  var raw=AsciiRaw.Read("Title: ac\nPlotname: AC Analysis\nFlags: complex\nNo. Variables: 2\nNo. Points: 1\nVariables:\n0 frequency frequency grid=3\n1 v(a) voltage\nValues:\n0 10,0\n1,.5\n");Assert.True(raw.Complex);Assert.Equal(.5,raw.Points[0][1].Imag);
 }
 [Fact] public void Projection_retains_original_expressions_and_unique_repeated_analysis_identity(){
  const string text="original\n.param upper=30\nV1 a 0 AC 1\nR1 a 0 1k\n.op\n.ac lin 3 10 {upper}\n.op\n.print ac v(a)\n.end\n";
  var syntax=DeckParser.Parse(text,collectDeclarations:true);var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);
  var projection=OriginalProjection.Create(text,syntax,plan.Operations[1],plan);Assert.Contains(".param upper=30",projection.Text);Assert.Contains(".ac lin 3 10 {upper}",projection.Text);Assert.Equal(3,projection.Omissions.Length);Assert.DoesNotContain(".op",projection.Text);Assert.Equal(text.Length,projection.Text.Length);
  Assert.Throws<InvalidDataException>(()=>OriginalProjection.Create(text,syntax,plan.Operations[1] with{Range=new(0,1,1,1)},plan));
 }
 [Fact] public void Unknown_stages_remain_unknown_and_source_gate_invalidates_claims(){
  var stage=StageBurndown.Count("native",new bool?[]{true,false,null});Assert.Equal(new StageCount("native",1,1,1),stage);Assert.Equal(new StageCount("native",0,0,3),StageBurndown.Count("native",new bool?[]{true,false,null},false));
  var svg=StageBurndown.Svg([stage,stage,stage,stage]);Assert.Contains("1 unknown",svg);Assert.Contains("gray: unknown",svg);Assert.Throws<ArgumentException>(()=>StageBurndown.Svg([stage]));
 }
 [Fact] public void Identity_units_missing_columns_and_grid_anomalies_cannot_agree(){
  var syntax=DeckParser.Parse("op\nV1 a 0 1\nR1 a 0 1k\n.op\n.end\n",collectDeclarations:true);var plan=RequestedAnalysis.Prepare(syntax);var circuit=OperatingPointSelection.Select(syntax).Prepared;var op=plan.Operations[0];
  var signals=new[]{new SignalInfo("v(n1)","voltage","V"),new SignalInfo("i(e0)","current","A")};
  SimulationResponse Response(string id,SignalInfo[] columns,double voltage=1){var manifest=new ResultManifest("1",id,"succeeded",true,columns,[new(0,[voltage,-.001])],[],[],[],null,new("nalgebra_dense_lu_reference","cpu",128,false,false,true,false,"test"),"hash");return new("1","succeeded",true,JsonSerializer.SerializeToElement(manifest,WireFormat.Options));}
  var raw=AsciiRaw.Read(OpRaw);Assert.True(RequestedComparison.Compare(op,circuit,[Response(op.Requests[0].CaseId,signals)],raw).Agreed);
  Assert.False(RequestedComparison.Compare(op,circuit,[Response("wrong",signals)],raw).Agreed);
  Assert.False(RequestedComparison.Compare(op,circuit,[Response(op.Requests[0].CaseId,signals)],raw with{PlotName="Transient Analysis"}).Agreed);
  Assert.False(RequestedComparison.Compare(op,circuit,[Response(op.Requests[0].CaseId,[signals[0] with{Unit="A"},signals[1]])],raw).Agreed);
  Assert.False(RequestedComparison.Compare(op,circuit,[Response(op.Requests[0].CaseId,signals,2)],raw).Agreed);
  Assert.False(RequestedComparison.Compare(op,circuit,[Response(op.Requests[0].CaseId,signals)],raw with{Variables=[raw.Variables[0],new("other","current")]}).Agreed);
 }
 [RequestedNativeFact] public async Task Inventory_path_escape_and_duplicate_identity_fail_before_output(){
  var parent=Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS");Assert.True(Directory.Exists(parent));
  var folder=Path.Combine(parent!,"invalid-inventory-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var root=RepositoryRoot();var inventory=Path.Combine(folder,"inventory.json");var fake=Path.Combine(folder,"fake.exe");await File.WriteAllTextAsync(fake,"");
  var item=new{scope="ngspice",path="../escape.cir",result=new{Sha256=new string('a',64)}};await File.WriteAllTextAsync(inventory,JsonSerializer.Serialize(new{rows=new[]{item}}));
  var output=Path.Combine(folder,"output");await Assert.ThrowsAsync<InvalidDataException>(()=>RequestedCorpus.RunAsync([root,folder,fake,fake,inventory,output]));Assert.False(Directory.Exists(output));
  item=new{scope="ngspice",path="ok.cir",result=new{Sha256=new string('a',64)}};await File.WriteAllTextAsync(inventory,JsonSerializer.Serialize(new{rows=new[]{item,item}}));await Assert.ThrowsAsync<InvalidDataException>(()=>RequestedCorpus.RunAsync([root,folder,fake,fake,inventory,output]));
 }
 private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath]string path="")=>Directory.GetParent(Path.GetDirectoryName(path)!)!.Parent!.FullName;
}
public sealed class RequestedCorpusNativeTests
{
 [RequestedNativeFact]
 public async Task Representative_original_programs_report_four_distinct_stages_and_refusals(){
  var parent=Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")!;var folder=Path.Combine(parent,"coordinator-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  var fixtures=new Dictionary<string,string>{
   ["good.cir"]="ordinary\n.param upper=1k\nV1 a 0 DC 0 AC 1 PWL(0 0 4u 4)\nR1 a out 1k\nR2 out 0 1k\n.op\n.dc V1 1 -1 -.5\n.ac dec 1 10 {upper}\n.tran 1u 4u\n.op\n.end\n",
   ["control.cir"]="control\nV1 a 0 1\nR1 a 0 1k\n.control\nop\n.endc\n.op\n.end\n",
   ["uic.cir"]="uic\nV1 a 0 1\nR1 a 0 1k\n.tran 1u 4u uic\n.end\n",
   ["resource.cir"]="resource\n.include missing-file.cir\n.op\n.end\n",
   ["lin2.cir"]="native grid anomaly\nV1 a 0 AC 1\nR1 a 0 1k\n.ac lin 2 10 30\n.end\n",
   ["current.cir"]="current\nI1 a 0 1m\nR1 a 0 1k\n.dc I1 -.001 .001 .001\n.end\n",
   ["norequest.cir"]="none\nV1 a 0 1\nR1 a 0 1k\n.end\n"};
  foreach(var item in fixtures)await File.WriteAllTextAsync(Path.Combine(folder,item.Key),item.Value);
  var inventory=Path.Combine(folder,"inventory.json");var rows=fixtures.Keys.Select(name=>new{scope="ngspice",path=name,result=new{Sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name))))}});await File.WriteAllTextAsync(inventory,JsonSerializer.Serialize(new{rows}));
  var root=Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.Parent!.FullName;var output=Path.Combine(folder,"output");var exit=await RequestedCorpus.RunAsync([root,folder,Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output,"300"]);Assert.Equal(0,exit);
  using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"summary.json")));Assert.True(report.RootElement.GetProperty("provenance_valid").GetBoolean());var results=report.RootElement.GetProperty("rows").EnumerateArray().ToDictionary(r=>r.GetProperty("path").GetString()!);
  Assert.True(results["good.cir"].GetProperty("native_agreed").GetBoolean(),results["good.cir"].GetRawText());Assert.Equal(5,results["good.cir"].GetProperty("operations").GetArrayLength());
  Assert.True(results["current.cir"].GetProperty("native_agreed").GetBoolean(),results["current.cir"].GetRawText());
  foreach(var name in new[]{"control.cir","uic.cir","norequest.cir"}){Assert.False(results[name].GetProperty("requested_prepared").GetBoolean());Assert.Empty(results[name].GetProperty("operations").EnumerateArray());}
  Assert.Equal("resource_refused",results["resource.cir"].GetProperty("status").GetString());
  Assert.False(results["lin2.cir"].GetProperty("native_agreed").GetBoolean());Assert.Equal("original_grid_discrepancy",results["lin2.cir"].GetProperty("operations")[0].GetProperty("status").GetString());
  Assert.Contains("unknown",await File.ReadAllTextAsync(Path.Combine(output,"burndown.svg")));
 }
 private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
