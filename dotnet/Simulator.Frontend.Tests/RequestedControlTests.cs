using System.Text.Json;
using System.Security.Cryptography;
using Simulator.Client;
using Simulator.Syntax;
using Simulator.RequestedQualification;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
namespace Simulator.Frontend.Tests;
public sealed class RequestedControlTests
{
 private static DeckSyntax Deck(string body)=>DeckParser.Parse("control profile\nV1 a 0 DC 0 AC 1 PWL(0 0 4u 4)\nR1 a out 1k\nR2 out 0 1k\n"+body+"\n.end\n",collectDeclarations:true);
 [Fact] public void Direct_generated_commands_retain_order_ranges_and_reporting_omissions(){
  var syntax=Deck(".control\nset noaskquit\nset filetype=ascii numdgt=17\nop\ndc V1 1 -1 -.5\nac dec 1 10 1k\ntran .1u 1u\nop\nplot v(out)\nquit\n.endc");Assert.Single(syntax.Declarations!.ControlBlocks);var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));Assert.Equal("direct_commands",plan.ControlProfile);Assert.Equal(new[]{"op","dc","ac","tran","op"},plan.Operations.Select(o=>o.Kind));Assert.All(plan.Operations,o=>Assert.Equal("direct",o.ControlOrigin!.Kind));Assert.Equal(4,plan.ProgramOmissions.Length);
 }
 [Fact] public void Finite_run_uses_native_unique_job_order_once_per_invocation(){
  var plan=RequestedAnalysis.Prepare(Deck(".tran .1u 1u\n.op\n.dc V1 0 1 1\n.ac lin 3 10 30\n.control\nrun\nplot v(out)\nrun\n.endc"));Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));Assert.Equal("finite_deck_run",plan.ControlProfile);Assert.Equal(new[]{"ac","dc","op","tran","ac","dc","op","tran"},plan.Operations.Select(o=>o.Kind));Assert.Equal(8,plan.Operations.Select(o=>o.Id).Distinct().Count());Assert.NotEqual(plan.Operations[0].ControlOrigin!.CommandRange,plan.Operations[4].ControlOrigin!.CommandRange);
 }
 [Fact] public void Outside_declarations_do_not_execute_implicitly_with_direct_control(){var plan=RequestedAnalysis.Prepare(Deck(".tran 1u 1m\n.control\nop\n.endc"));Assert.True(plan.Success);Assert.Equal("op",Assert.Single(plan.Operations).Kind);Assert.Contains(plan.ProgramOmissions,o=>o.Kind=="tran"&&o.Reason=="deck_analysis_not_dispatched_by_control");}
 [Theory]
 [InlineData("let x=1")][InlineData("alter R1=2")][InlineData("reset")][InlineData("load another.raw")][InlineData("shell echo bad")][InlineData("write external.raw all")][InlineData("noise v(out) V1 dec 10 1 1k")][InlineData("set reltol=1m")][InlineData("setplot tran1")][InlineData("op; alter R1=2")][InlineData("ac dec 1 10 $&upper")][InlineData("ac dec 1 10 {upper}")][InlineData("quit\nop")]
 public void Unmodeled_effects_variables_and_separators_refuse_the_whole_program(string command){var plan=RequestedAnalysis.Prepare(Deck(".control\nop\n"+command+"\n.endc"));Assert.False(plan.Success);Assert.Empty(plan.Operations);}
 [Theory]
 [InlineData(".ac lin 3 10 30\n.ac lin 5 10 50\n.control\nrun\n.endc","unsupported_control_run_duplicate_analysis_kind")]
 [InlineData(".op\n.control\nop\nrun\n.endc","unsupported_control_mixed_run_and_direct")]
 [InlineData(".control\nrun\n.endc","control_run_without_declared_analysis")]
 [InlineData(".control\nop\n.endc\n.control\nop\n.endc","unsupported_multiple_control_blocks")]
 public void Unqualified_run_registration_profiles_refuse_explicitly(string body,string code){var plan=RequestedAnalysis.Prepare(Deck(body));Assert.False(plan.Success);Assert.Contains(plan.Diagnostics,d=>d.Code==code);}
 [Theory]
 [InlineData("set filetype=binary")][InlineData("set numdgt=0")][InlineData("set numdgt=18")][InlineData("set noaskquit=1")]
 public void Reporting_setting_values_are_validated_instead_of_ignoring_arbitrary_set(string setting){var plan=RequestedAnalysis.Prepare(Deck(".control\n"+setting+"\nop\n.endc"));Assert.False(plan.Success);Assert.Contains(plan.Diagnostics,d=>d.Code.StartsWith("unsupported_control_setting:",StringComparison.Ordinal));}
 [Fact] public void Command_count_and_effective_analysis_budgets_are_explicit(){
  var plan=RequestedAnalysis.Prepare(Deck(".control\n"+string.Concat(Enumerable.Repeat("op\n",257))+".endc"));Assert.False(plan.Success);Assert.Contains(plan.Diagnostics,d=>d.Code=="control_command_budget");
  plan=RequestedAnalysis.Prepare(Deck(".control\n"+string.Concat(Enumerable.Repeat("op\n",129))+".endc"));Assert.False(plan.Success);Assert.Contains(plan.Diagnostics,d=>d.Code=="control_analysis_budget");
 }
 [Property(MaxTest=100,Replay="2391,20261103")]
 public bool State_changing_prefix_is_never_silently_omitted(NonNull<string> input){var tail=input.Get.Length>256?input.Get[..256]:input.Get;var plan=RequestedAnalysis.Prepare(Deck(".control\nop\nalter R1="+tail+"\n.endc"));return !plan.Success&&plan.Operations.IsEmpty;}
 [Fact] public void Original_control_projection_retains_literal_arguments_and_owns_only_output_instrumentation(){
  var text="control projection\nV1 a 0 AC 1\nR1 a 0 1k\n.tran 1u 1m\n.control\nac lin 3 10 30\nplot v(a)\nquit\n.endc\n.end\n";var syntax=DeckParser.Parse(text,collectDeclarations:true);var plan=RequestedAnalysis.Prepare(syntax);Assert.True(plan.Success);var projection=OriginalProjection.Create(text,syntax,plan.Operations[0],plan);Assert.True(projection.RequiresControlWrite);Assert.Contains("ac lin 3 10 30",projection.Text);Assert.DoesNotContain(".tran 1u",projection.Text);Assert.DoesNotContain("plot v(a)",projection.Text);Assert.Contains("write native.raw all",projection.Text);Assert.Equal(new[]{"write native.raw all","quit"},projection.Instrumentation);
 }
 [Fact] public void Control_RAW_blank_point_separators_do_not_relax_shape_or_index_checks(){var text="Title: raw\nPlotname: DC transfer characteristic\nFlags: real\nNo. Variables: 1\nNo. Points: 2\nVariables:\n0 v(v-sweep) voltage\nValues:\n0 -1\n\n1 1\n\n";Assert.Equal(2,AsciiRaw.Read(text).Points.Length);Assert.Throws<InvalidDataException>(()=>AsciiRaw.Read(text.Replace("1 1\n","2 1\n",StringComparison.Ordinal)));}
}
public sealed class RequestedControlNativeTests
{
 [RequestedNativeFact] public async Task Direct_and_run_control_requests_match_native_original_control_projections(){
  var parent=Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")!;var folder=Path.Combine(parent,"control-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);const string circuit="owned control\nV1 a 0 DC 0 AC 1 PWL(0 0 4u 4)\nR1 a out 1k\nR2 out 0 1k\n";
  var inputs=new Dictionary<string,string>{["direct.cir"]=circuit+".control\nset noaskquit\nset filetype=ascii\nop\ndc V1 1 -1 -.5\nac dec 1 10 1k\ntran .1u 1u\nop\nplot v(out)\nquit\n.endc\n.end\n",["run.cir"]=circuit+".tran .1u 1u\n.op\n.dc V1 1 -1 -.5\n.ac lin 3 10 30\n.control\nrun\nplot v(out)\nrun\nquit\n.endc\n.end\n",["unsafe.cir"]=circuit+".control\nop\nalter R1=2k\nop\n.endc\n.end\n"};
  foreach(var item in inputs)await File.WriteAllTextAsync(Path.Combine(folder,item.Key),item.Value);var inventory=Path.Combine(folder,"inventory.json");await File.WriteAllTextAsync(inventory,JsonSerializer.Serialize(new{rows=inputs.Keys.Select(name=>new{scope="ngspice",path=name,result=new{Sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name))))}})}));var root=Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.Parent!.FullName;var output=Path.Combine(folder,"output");var exit=await RequestedCorpus.RunAsync([root,folder,Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output,"300"]);Assert.Equal(0,exit);
  using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"summary.json")));Assert.True(report.RootElement.GetProperty("provenance_valid").GetBoolean());foreach(var row in report.RootElement.GetProperty("rows").EnumerateArray()){if(row.GetProperty("path").GetString()=="unsafe.cir"){Assert.False(row.GetProperty("requested_prepared").GetBoolean());Assert.Empty(row.GetProperty("operations").EnumerateArray());}else{Assert.True(row.GetProperty("native_agreed").ValueKind==JsonValueKind.True,row.GetRawText());Assert.All(row.GetProperty("operations").EnumerateArray(),op=>Assert.Equal(2,op.GetProperty("native_instrumentation").GetArrayLength()));}}
 }
 private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
public sealed class RequestedOriginalControlFactAttribute:FactAttribute
{
 public RequestedOriginalControlFactAttribute(){var corpus=Environment.GetEnvironmentVariable("SPICE_REQUESTED_CORPUS");if(!Directory.Exists(corpus)||!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE"))||!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE"))||!Directory.Exists(Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")))Skip="Set existing original corpus, native executables and artifact root.";}
}
public sealed class RequestedOriginalControlTests
{
 [RequestedOriginalControlFact] public async Task Original_simple_measure_run_deck_executes_once_and_preserves_any_accuracy_gap(){
  var source=Path.Combine(Environment.GetEnvironmentVariable("SPICE_REQUESTED_CORPUS")!,"examples/measure/simple-meas-tran.sp");Assert.True(File.Exists(source));var parent=Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")!;var folder=Path.Combine(parent,"original-control-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var input=Path.Combine(folder,"original.cir");File.Copy(source,input);var hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input)));Assert.Equal(hash,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));await File.WriteAllTextAsync(Path.Combine(folder,"origin.json"),JsonSerializer.Serialize(new{source,sha256=hash}));
  var inventory=Path.Combine(folder,"inventory.json");await File.WriteAllTextAsync(inventory,JsonSerializer.Serialize(new{rows=new[]{new{scope="ngspice",path="original.cir",result=new{Sha256=hash}}}}));var root=Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.Parent!.FullName;var output=Path.Combine(folder,"output");Assert.Equal(0,await RequestedCorpus.RunAsync([root,folder,Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output,"120"]));
  using var summary=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"summary.json")));Assert.True(summary.RootElement.GetProperty("provenance_valid").GetBoolean());var row=summary.RootElement.GetProperty("rows")[0];Assert.True(row.GetProperty("requested_prepared").GetBoolean(),row.GetRawText());Assert.True(row.GetProperty("requested_executed").GetBoolean(),row.GetRawText());Assert.Equal("finite_deck_run",row.GetProperty("control_profile").GetString());var operation=Assert.Single(row.GetProperty("operations").EnumerateArray());Assert.Equal("tran",operation.GetProperty("kind").GetString());Assert.True(row.GetProperty("program_omissions").GetArrayLength()>10);Assert.NotEqual(JsonValueKind.Null,row.GetProperty("native_agreed").ValueKind);
  // This is execution qualification, not a forced numerical pass: any measured
  // original-grid interpolation or integration discrepancy remains in the report.
  var status=operation.GetProperty("status").GetString();Assert.Contains(status,new[]{"agreed","numerical_disagreement"});
 }
 private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
