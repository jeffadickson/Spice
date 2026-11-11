using Simulator.Syntax;

namespace Simulator.Frontend;

// This is admission for unexecuted reporting declarations, not a vector
// evaluator. A declaration cannot influence a later dispatched analysis.
internal static class ControlSuffixAdmission
{
    internal static string? Admit(ControlCommandSyntax command,int lastAnalysisStart,HashSet<string> locals,bool allowPrefixDefinition=false,bool allowPrefixScalar=false)
    {
        var allowedPrefix=allowPrefixDefinition&&command.Postprocessing?.Kind==ControlPostprocessingKind.Define||
            allowPrefixScalar&&command.Postprocessing?.Kind==ControlPostprocessingKind.Let;
        if(!allowedPrefix&&(lastAnalysisStart<0||command.Range.Start<=lastAnalysisStart))
            return "unsupported_control_postprocessing_before_analysis";
        if(command.Postprocessing is not {} candidate||candidate.Names.IsEmpty||
           candidate.Names.Any(n=>!SimpleName(n)))return "unsupported_control_local_name";
        switch(candidate.Kind)
        {
            case ControlPostprocessingKind.Let:
                if(candidate.Names.Length!=1||candidate.Expression==null||Reserved(candidate.Names[0]))
                    return "unsupported_control_result_assignment";
                locals.Add(candidate.Names[0]);break;
            case ControlPostprocessingKind.Compose:
                if(candidate.Names.Length!=1||candidate.Values.IsEmpty||candidate.Values.Length>4096||Reserved(candidate.Names[0]))
                    return "unsupported_control_compose_values";
                locals.Add(candidate.Names[0]);break;
            case ControlPostprocessingKind.Define:
                if(candidate.Expression==null||candidate.Names.Skip(1).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=candidate.Names.Length-1)
                    return "unsupported_control_function_formals";
                break;
            case ControlPostprocessingKind.Unlet:
                if(candidate.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=candidate.Names.Length||candidate.Names.Any(n=>!locals.Contains(n)))
                    return "unsupported_control_unlet_target";
                foreach(var name in candidate.Names)locals.Remove(name);
                break;
            default:return "unsupported_control_postprocessing_kind";
        }
        return null;
    }

    // Generated identifiers own tokenization; this is the deliberately narrower
    // local-name contract, excluding plot/vector indexing and namespaces.
    private static bool SimpleName(string name)=>name.Length is >0 and <=64&&
        (char.IsAsciiLetter(name[0])||name[0]=='_')&&name.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_');
    private static bool Reserved(string name)=>name.Equals("all",StringComparison.OrdinalIgnoreCase)||
        name.Equals("time",StringComparison.OrdinalIgnoreCase)||name.Equals("frequency",StringComparison.OrdinalIgnoreCase);
}
