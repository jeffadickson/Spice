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
 internal static CompiledControlProfile Compile(DeclarationCatalog catalog){
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
  var locals=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var quit=false;
  foreach(var command in block.Commands){
   if(command.Kind=="comment")continue;
   if(quit)return Fail("unsupported_control_command_after_quit",command.Range);
   if(command.Diagnostic!=null)return Fail(command.Diagnostic,command.Range);
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
    case "quit":quit=true;omissions.Add(new(0,"control_exit",command.Name,"final_process_exit_not_executed",command.Range));break;
    default:return Fail("unsupported_control_command:"+command.Name,command.Range);
   }
   if(result.Count>128)return Fail("control_analysis_budget",command.Range);
  }
  if(!hasRun)foreach(var analysis in catalog.Analyses)omissions.Add(new(analysis.ScopeId,analysis.Kind,"."+analysis.Kind,"deck_analysis_not_dispatched_by_control",analysis.Range));
  return new(result.ToImmutable(),omissions.ToImmutable(),hasRun?"finite_deck_run":"direct_commands",null);
 }
}
