using System.Collections.Immutable;
using System.Numerics;
using Simulator.Syntax;

namespace Simulator.Frontend;

internal static partial class RequestedControlPreparation
{
    // Analysis arguments remain statically resolved by requested preparation.
    // Reporting branches cannot change dispatch, physical settings or files.
    private static CompiledControlProfile CompileOrdered(DeclarationCatalog catalog,ControlBlockSyntax block,
        IReadOnlyDictionary<string,double> constants)
    {
        CompiledControlProfile Fail(string code,SourceRange range)=>new([],[],"refused",new(code,range));
        var dispatch=block.Commands.Where(c=>c.Kind=="analysis").ToArray();
        if(dispatch.Length is <1 or >128)return Fail("control_analysis_budget",block.Range);
        var first=dispatch[0].Range.Start;var last=dispatch[^1].Range.Start;
        var prefix=block.Commands.Where(c=>c.Range.Start<first).ToArray();
        if(prefix.Any(c=>c.Kind!="comment"&&c.Postprocessing?.Kind is not (ControlPostprocessingKind.Let or ControlPostprocessingKind.Define)))
            return Fail("unsupported_control_prefix_effect",block.Range);
        try{
            var initial=ControlVectorExecution.ExecuteComplex(prefix,new Dictionary<string,Complex[]>(),constants);
            if(!initial.Diagnostics.IsEmpty||initial.Locals.Values.Any(v=>v.Length!=1))
                return Fail("unsupported_control_prefix_effect",block.Range);
        }catch(InvalidDataException error){return Fail(error.Message,block.Range);}

        var result=ImmutableArray.CreateBuilder<ControlDirective>();
        var observations=ImmutableArray.CreateBuilder<OperatingPointOmission>();
        var branches=new Stack<bool>();var quit=false;
        var locals=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var command in block.Commands)
        {
            if(command.Kind=="comment")continue;
            if(command.Diagnostic is {} diagnostic)return Fail(diagnostic,command.Range);
            if(quit)return Fail("unsupported_control_command_after_quit",command.Range);
            switch(command.Kind)
            {
                case "analysis":
                    if(branches.Count!=0)return Fail("conditional_analysis_dispatch",command.Range);
                    result.Add(new(command.Analysis!,new("direct",block.Range,command.Range,null)));
                    continue;
                case "if":
                    if(branches.Count>=8)return Fail("conditional_depth_budget",command.Range);
                    branches.Push(false);break;
                case "else":
                    if(branches.Count==0||branches.Pop())return Fail("invalid_conditional_else",command.Range);
                    branches.Push(true);break;
                case "end":
                    if(branches.Count==0)return Fail("invalid_conditional_end",command.Range);
                    branches.Pop();break;
                case "postprocessing":
                    if(command.Postprocessing?.Expression?.Text.Contains('$')==true)
                        return Fail("control_loop_reference_outside_loop",command.Range);
                    if(ControlSuffixAdmission.Admit(command,first-1,locals,allowPrefixDefinition:true,allowPrefixScalar:true) is {} invalid)
                        return Fail(invalid,command.Range);
                    break;
                case "report" when command.Name is "echo" or "print":
                case "report_substitution":
                case "report_redirect":break;
                case "quit":case "quit_status":
                    if(command.Range.Start<last)return Fail("exit_before_last_analysis",command.Range);
                    quit=branches.Count==0;break;
                default:return Fail("unsupported_ordered_control_effect:"+command.Name,command.Range);
            }
            observations.Add(new(0,"control_ordered_reporting",command.Name,"ordered_reporting_executed_separately",command.Range));
        }
        if(branches.Count!=0)return Fail("unclosed_reporting_conditional",block.Range);
        observations.AddRange(catalog.Analyses.Select(a=>new OperatingPointOmission(a.ScopeId,a.Kind,"."+a.Kind,"deck_analysis_not_dispatched_by_control",a.Range)));
        return new(result.ToImmutable(),observations.ToImmutable(),"ordered_direct_commands",null);
    }
}
