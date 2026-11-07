using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>Original control invocation that dispatches an individual requested analysis.</summary>
public sealed record RequestedControlOrigin(string Kind,SourceRange BlockRange,SourceRange CommandRange,SourceRange? DeclaredAnalysisRange);
internal sealed record ControlDirective(AnalysisSyntax Analysis,RequestedControlOrigin? Origin);
internal sealed record CompiledControlProfile(ImmutableArray<ControlDirective> Directives,ImmutableArray<OperatingPointOmission> Omissions,string Profile,BridgeDiagnostic? Diagnostic);
// Finite analysis dispatch only. Unmodeled effects refuse the complete input;
// no command interpreter or opaque command text is forwarded to the engine.
internal static class RequestedControlPreparation
{
 internal static CompiledControlProfile Compile(DeclarationCatalog catalog,IReadOnlyDictionary<string,double> constants){
  CompiledControlProfile Fail(string code,SourceRange? range)=>new([],[],"refused",new(code,range));
  if(catalog.ControlBlocks.IsEmpty)return new(catalog.Analyses.OrderBy(a=>a.Range.Start).Select(a=>new ControlDirective(a,null)).ToImmutableArray(),[],"deck",null);
  if(catalog.ControlBlocks.Length!=1)return Fail("unsupported_multiple_control_blocks",catalog.ControlBlocks[0].Range);
  var block=catalog.ControlBlocks[0];if(block.ScopeId!=0)return Fail("unsupported_control_scope",block.Range);if(block.Diagnostic!=null)return Fail(block.Diagnostic,block.Range);
  var result=ImmutableArray.CreateBuilder<ControlDirective>();var omissions=ImmutableArray.CreateBuilder<OperatingPointOmission>();
  var hasRun=block.Commands.Any(c=>c.Kind=="run");var hasDirect=block.Commands.Any(c=>c.Kind=="analysis");
  if(hasRun&&hasDirect)return Fail("unsupported_control_mixed_run_and_direct",block.Range);
  if(hasRun&&catalog.Analyses.GroupBy(a=>a.Kind).Any(g=>g.Count()!=1))return Fail("unsupported_control_run_duplicate_analysis_kind",block.Range);
  if(hasRun&&catalog.Analyses.IsEmpty)return Fail("control_run_without_declared_analysis",block.Range);
  if(hasRun&&catalog.Analyses.Any(a=>a.ScopeId!=0))return Fail("unsupported_control_run_analysis_scope",block.Range);
  var lastAnalysisStart=block.Commands.Where(c=>c.Kind is "analysis" or "run").Select(c=>c.Range.Start).DefaultIfEmpty(-1).Max();
  var prefix=block.Commands.Where(c=>c.Range.Start<lastAnalysisStart).ToArray();
  var hasLocalSettings=prefix.Any(c=>c.Kind=="local_set");
  var prefixReporting=hasDirect&&!hasRun&&block.Commands.Count(c=>c.Kind=="analysis")==1&&
      (hasLocalSettings||prefix.Any(c=>c.Postprocessing?.Kind==ControlPostprocessingKind.Define));
  if(prefixReporting){
   foreach(var command in prefix){
    if(command.Postprocessing?.Kind==ControlPostprocessingKind.Define){
     if(ControlSuffixAdmission.Admit(command,lastAnalysisStart,new(StringComparer.OrdinalIgnoreCase),allowPrefixDefinition:true) is {} error)
      return Fail(error,command.Range);
     continue;
    }
    if(command.Kind=="comment")continue;
    if(!hasLocalSettings||command.Kind is not ("if" or "else" or "end" or "local_set" or "report_substitution")&&
       !(command.Kind=="report"&&command.Name=="echo")&&command.Postprocessing?.Kind!=ControlPostprocessingKind.Let)
     return Fail("unsupported_control_prefix_effect",command.Range);
   }
   try{ControlVectorExecution.Execute(prefix,new Dictionary<string,double[]>(),constants:constants);}
   catch(InvalidDataException error){return Fail(error.Message,block.Range);}
  }
  var locals=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var quit=false;var branches=new Stack<bool>();
  foreach(var command in block.Commands){
   if(command.Kind=="comment")continue;
   if(prefixReporting&&command.Range.Start<lastAnalysisStart){
    omissions.Add(new(0,"control_prefix_reporting",command.Name,"prefix_reporting_executed_separately",command.Range));continue;
   }
   if(quit)return Fail("unsupported_control_command_after_quit",command.Range);
   if(command.Diagnostic!=null)return Fail(command.Diagnostic,command.Range);
   // Only a terminal reporting region can be projected away. No branch may
   // affect a later dispatch, physical setting, external command or artifact.
   if(command.Kind is "if" or "else" or "end" || branches.Count!=0){
    if(lastAnalysisStart<0||command.Range.Start<=lastAnalysisStart)return Fail("conditional_before_last_analysis",command.Range);
    switch(command.Kind){
     case "if":
      if(branches.Count>=8)return Fail("conditional_depth_budget",command.Range);
      branches.Push(false);break;
     case "else":
      if(branches.Count==0||branches.Pop())return Fail("invalid_conditional_else",command.Range);
      branches.Push(true);break;
     case "end":
      if(branches.Count==0)return Fail("invalid_conditional_end",command.Range);
      branches.Pop();break;
     case "report" when command.Name is "echo" or "print":
     case "report_substitution":
     case "quit":
     case "quit_status":break;
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
    case "postprocessing":
     if(ControlSuffixAdmission.Admit(command,lastAnalysisStart,locals) is {} suffixFailure)return Fail(suffixFailure,command.Range);
     omissions.Add(new(0,"control_postprocessing",command.Name,"postprocessing_not_executed",command.Range));break;
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
  if(branches.Count!=0)return Fail("unclosed_reporting_conditional",block.Range);
  if(!hasRun)foreach(var analysis in catalog.Analyses)omissions.Add(new(analysis.ScopeId,analysis.Kind,"."+analysis.Kind,"deck_analysis_not_dispatched_by_control",analysis.Range));
  return new(result.ToImmutable(),omissions.ToImmutable(),hasRun?"finite_deck_run":"direct_commands",null);
 }
}
