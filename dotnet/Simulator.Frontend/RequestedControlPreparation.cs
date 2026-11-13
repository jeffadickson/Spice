using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>Original control invocation that dispatches an individual requested analysis.</summary>
public sealed record RequestedControlOrigin(string Kind,SourceRange BlockRange,SourceRange CommandRange,SourceRange? DeclaredAnalysisRange);
internal sealed record ControlDirective(AnalysisSyntax Analysis,RequestedControlOrigin? Origin);
internal sealed record CompiledControlProfile(ImmutableArray<ControlDirective> Directives,ImmutableArray<OperatingPointOmission> Omissions,string Profile,BridgeDiagnostic? Diagnostic);
// Finite analysis dispatch only. Unmodeled effects refuse the complete input;
// no command interpreter or opaque command text is forwarded to the engine.
internal static partial class RequestedControlPreparation
{
 // Qualification uses ngspice -b. In this shape the true branch only
 // reports, so the native batch driver dispatches the sole root analysis.
 // Refuse unmodeled active commands and arbitrary dormant program shapes.
 private static CompiledControlProfile? BatchModeRoot(DeclarationCatalog catalog,ControlBlockSyntax block)
 {
  var commands=block.Commands.Where(c=>c.Kind!="comment").ToArray();
  if(catalog.Analyses is not [{ScopeId:0,Kind:"tran"} analysis]||commands.Length<3||
     commands[0] is not {Kind:"if",Argument:"$?batchmode",Diagnostic:null}||commands[^1].Kind!="end")return null;
  var split=Array.FindIndex(commands,c=>c.Kind=="else");
  if(split<1||!commands[1..split].All(Echo)||!commands[(split+1)..^1].All(c=>
       Echo(c)||c.Diagnostic==null&&(c.Kind=="analysis"||c is {Kind:"report",Name:"plot"})||
       c is {Kind:"unsupported",Name:"unset",GrammarRule:"controlUnknown",Argument:"ngdebug"}))
   return null;
  return new([new(analysis,null)],
      [new(0,"control","if","batchmode_reporting_and_interactive_branch_not_executed",block.Range)],
      "batchmode_root",null);

  static bool Echo(ControlCommandSyntax command)=>command is {Kind:"report",Name:"echo",Diagnostic:null};
 }

 internal static CompiledControlProfile Compile(DeclarationCatalog catalog,IReadOnlyDictionary<string,double> constants){
  CompiledControlProfile Fail(string code,SourceRange? range)=>new([],[],"refused",new(code,range));
  if(catalog.ControlBlocks.IsEmpty)return new(catalog.Analyses.OrderBy(a=>a.Range.Start).Select(a=>new ControlDirective(a,null)).ToImmutableArray(),[],"deck",null);
  if(catalog.ControlBlocks.Length!=1)return Fail("unsupported_multiple_control_blocks",catalog.ControlBlocks[0].Range);
  var block=catalog.ControlBlocks[0];if(block.ScopeId!=0)return Fail("unsupported_control_scope",block.Range);if(block.Diagnostic!=null)return Fail(block.Diagnostic,block.Range);
  if(BatchModeRoot(catalog,block) is {} batch)return batch;
  var result=ImmutableArray.CreateBuilder<ControlDirective>();var omissions=ImmutableArray.CreateBuilder<OperatingPointOmission>();
  var hasRun=block.Commands.Any(c=>c.Kind=="run");var hasDirect=block.Commands.Any(c=>c.Kind=="analysis");
  var phaseReporting=block.Commands.Any(c=>c.Kind is "postprocessing" or "if" or "report_substitution" or "report_redirect"||c is {Kind:"report",Name:"print"});
  if(!hasRun&&(block.Commands.Count(c=>c.Kind=="analysis")>1&&phaseReporting||block.Commands.Any(c=>c.Analysis?.Kind=="pz")))
   return CompileOrdered(catalog,block,constants);
  if(hasRun&&hasDirect)return Fail("unsupported_control_mixed_run_and_direct",block.Range);
  if(hasRun&&catalog.Analyses.GroupBy(a=>a.Kind).Any(g=>g.Count()!=1))return Fail("unsupported_control_run_duplicate_analysis_kind",block.Range);
  if(hasRun&&catalog.Analyses.IsEmpty)return Fail("control_run_without_declared_analysis",block.Range);
  if(hasRun&&catalog.Analyses.Any(a=>a.ScopeId!=0))return Fail("unsupported_control_run_analysis_scope",block.Range);
  var lastAnalysisStart=block.Commands.Where(c=>c.Kind is "analysis" or "run").Select(c=>c.Range.Start).DefaultIfEmpty(-1).Max();
  var prefix=block.Commands.Where(c=>c.Range.Start<lastAnalysisStart).ToArray();
  var hasLocalSettings=prefix.Any(c=>c.Kind=="local_set");
  var scalarPrefix=prefix.Any(c=>c.Postprocessing?.Kind==ControlPostprocessingKind.Let)&&
      prefix.All(c=>c.Kind=="comment"||c is {Diagnostic:null,Postprocessing.Kind:ControlPostprocessingKind.Let});
  var prefixReporting=hasDirect&&!hasRun&&block.Commands.Count(c=>c.Kind=="analysis")==1&&
      (scalarPrefix||hasLocalSettings||prefix.Any(c=>c.Postprocessing?.Kind==ControlPostprocessingKind.Define));
  if(prefixReporting){
   foreach(var command in prefix){
    if(command.Postprocessing?.Kind==ControlPostprocessingKind.Define){
     if(ControlSuffixAdmission.Admit(command,lastAnalysisStart,new(StringComparer.OrdinalIgnoreCase),allowPrefixDefinition:true) is {} error)
      return Fail(error,command.Range);
     continue;
    }
    if(command.Kind=="comment")continue;
    if(scalarPrefix){
     if(ControlSuffixAdmission.Admit(command,lastAnalysisStart,new(StringComparer.OrdinalIgnoreCase),allowPrefixScalar:true) is {} invalid)
      return Fail(invalid,command.Range);
     continue;
    }
    if(!hasLocalSettings||command.Kind is not ("if" or "else" or "end" or "local_set" or "report_substitution")&&
       !(command.Kind=="report"&&command.Name=="echo")&&command.Postprocessing?.Kind!=ControlPostprocessingKind.Let)
     return Fail("unsupported_control_prefix_effect",command.Range);
   }
   try{
    var validation=ControlVectorExecution.Execute(prefix,new Dictionary<string,double[]>(),constants:constants);
    if(scalarPrefix&&(!validation.Diagnostics.IsEmpty||validation.Locals.Values.Any(v=>v.Length!=1)))return Fail("unsupported_control_prefix_effect",block.Range);
   }
   catch(InvalidDataException error){return Fail(error.Message,block.Range);}
  }
  var locals=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var quit=false;var inLoop=false;var branches=new Stack<bool>();
  foreach(var command in block.Commands){
   if(command.Kind=="comment")continue;
   if(prefixReporting&&command.Range.Start<lastAnalysisStart){
    omissions.Add(new(0,"control_prefix_reporting",command.Name,"prefix_reporting_executed_separately",command.Range));continue;
   }
   if(quit)return Fail("unsupported_control_command_after_quit",command.Range);
   if(command.Diagnostic!=null)return Fail(command.Diagnostic,command.Range);
   if(!inLoop&&command.Postprocessing is {Kind:ControlPostprocessingKind.Let,Expression: {} expression}&&expression.Text.Contains('$'))
    return Fail("control_loop_reference_outside_loop",command.Range);
   // Only a terminal reporting region can be projected away. No branch may
   // affect a later dispatch, physical setting, external command or artifact.
   if(command.Kind is "if" or "else" or "end" or "foreach" || branches.Count!=0 || inLoop){
    if(lastAnalysisStart<0||command.Range.Start<=lastAnalysisStart)return Fail("conditional_before_last_analysis",command.Range);
    switch(command.Kind){
     case "foreach":
      if(inLoop||branches.Count!=0||command.Foreach==null)return Fail("unsupported_reporting_loop_shape",command.Range);
      inLoop=true;break;
     case "loop_set" when inLoop:break;
     case "if":
      if(branches.Count>=8)return Fail("conditional_depth_budget",command.Range);
      branches.Push(false);break;
     case "else":
      if(branches.Count==0||branches.Pop())return Fail("invalid_conditional_else",command.Range);
      branches.Push(true);break;
     case "end":
      if(branches.Count==0&&inLoop){inLoop=false;break;}
      if(branches.Count==0)return Fail("invalid_conditional_end",command.Range);
      branches.Pop();break;
     case "report" when command.Name is "echo" or "print":
     case "report_substitution":
     case "quit" when !inLoop:
     case "quit_status" when !inLoop:break;
     case "postprocessing" when command.Postprocessing?.Kind==ControlPostprocessingKind.Let:
      // Conditional assignments are not guaranteed to exist outside a branch.
      if(ControlSuffixAdmission.Admit(command,lastAnalysisStart,new(StringComparer.OrdinalIgnoreCase)) is {} error)return Fail(error,command.Range);
      break;
     default:return Fail("unsupported_conditional_effect:"+command.Name,command.Range);
    }
    omissions.Add(new(0,"control_conditional_reporting",command.Name,"conditional_reporting_not_executed",command.Range));
    continue;
   }
   switch(command.Kind){
    case "analysis":result.Add(new(command.Analysis!,new("direct",block.Range,command.Range,null)));break;
    case "run":
     // Verified native priority for unique jobs. Duplicate jobs are not silently
     // coalesced or reordered: they remain an explicit unqualified profile.
     foreach(var kind in new[]{"ac","dc","op","tran"})foreach(var analysis in catalog.Analyses.Where(a=>a.Kind==kind))result.Add(new(analysis,new("run",block.Range,command.Range,analysis.Range)));
     break;
    case "set":
     foreach(var setting in command.Settings){
      var name=setting.Name?.ToLowerInvariant();var accepted=name=="noaskquit"&&setting.Value==null||name=="filetype"&&setting.LiteralIdentifier?.Equals("ascii",StringComparison.OrdinalIgnoreCase)==true;
      if(name is "numdgt" or "xbrushwidth"&&setting.LiteralNumber&&setting.Value!=null){var value=ParameterEvaluator.EvaluateValue(catalog,ParameterEvaluator.Evaluate(catalog),setting.Value);accepted=value.Value is {} digits&&digits>=1&&digits<=(name=="numdgt"?17:64)&&digits==Math.Truncate(digits);}
      if(!accepted)return Fail("unsupported_control_setting:"+(name??"target"),setting.Range);
     }
     omissions.Add(new(0,"control_reporting_setting",command.Name,"reporting_setting_not_executed",command.Range));break;
    case "source_parameter_alter":
     if(lastAnalysisStart<0||command.Range.Start<=lastAnalysisStart||block.Commands.Count(c=>c.Kind=="analysis")!=1||
        block.Commands.Single(c=>c.Kind=="analysis").Analysis?.Kind!="op"||command.SourceParameterAlteration?.Values.Length!=7)
      return Fail("unsupported_source_parameter_alter_profile",command.Range);
     omissions.Add(new(0,"control_source_parameter",command.Name,"source_parameter_alter_executed_separately",command.Range));break;
    case "postprocessing":
     if(ControlSuffixAdmission.Admit(command,lastAnalysisStart,locals) is {} suffixFailure)return Fail(suffixFailure,command.Range);
     omissions.Add(new(0,"control_postprocessing",command.Name,"postprocessing_not_executed",command.Range));break;
    case "power_vector_type":
     // settype changes plot metadata only. Keep it after all analysis dispatch,
     // and preserve the omitted reporting command for the caller to audit.
     if(lastAnalysisStart<0||command.Range.Start<=lastAnalysisStart)return Fail("vector_type_before_last_analysis",command.Range);
     omissions.Add(new(0,"control_reporting_setting",command.Name,"vector_type_reporting_not_executed",command.Range));break;
    case "report":omissions.Add(new(0,"control_observation",command.Name,"control_output_not_executed",command.Range));break;
    case "report_redirect":
     if(lastAnalysisStart<0||command.Range.Start<=lastAnalysisStart)return Fail("redirect_before_last_analysis",command.Range);
     omissions.Add(new(0,"control_observation",command.Name,"redirect_captured_separately",command.Range));break;
    case "report_substitution":
     if(lastAnalysisStart<0||command.Range.Start<=lastAnalysisStart)return Fail("substitution_before_last_analysis",command.Range);
     omissions.Add(new(0,"control_observation",command.Name,"control_substitution_not_executed",command.Range));break;
    case "quit_status":
     if(lastAnalysisStart<0||command.Range.Start<=lastAnalysisStart)return Fail("exit_before_last_analysis",command.Range);
     goto case "quit";
    case "quit":quit=true;omissions.Add(new(0,"control_exit",command.Name,"final_process_exit_not_executed",command.Range));break;
    default:return Fail("unsupported_control_command:"+command.Name,command.Range);
   }
   if(result.Count>128)return Fail("control_analysis_budget",command.Range);
  }
  if(branches.Count!=0||inLoop)return Fail("unclosed_reporting_conditional",block.Range);
  if(!hasRun)foreach(var analysis in catalog.Analyses)omissions.Add(new(analysis.ScopeId,analysis.Kind,"."+analysis.Kind,"deck_analysis_not_dispatched_by_control",analysis.Range));
  return new(result.ToImmutable(),omissions.ToImmutable(),hasRun?"finite_deck_run":"direct_commands",null);
 }
}
