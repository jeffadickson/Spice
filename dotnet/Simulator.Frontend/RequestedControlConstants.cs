using System.Collections.Immutable;
using Simulator.Syntax;

namespace Simulator.Frontend;

public static partial class RequestedAnalysis
{
    // Native startup constants plus the common analysis axes and vector
    // selectors. Keep exports separate from both circuit parameters and results.
    private static readonly ImmutableHashSet<string> ReservedControlConstants=ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,"yes","no","true","false","pi","e","c","i","kelvin",
        "echarge","boltz","planck","time","frequency","all");

    private static ImmutableDictionary<string,double> BindControlConstants(DeclarationCatalog catalog,out BridgeDiagnostic? error)
    {
        var values=ImmutableDictionary.CreateBuilder<string,double>(StringComparer.OrdinalIgnoreCase);
        var exports=catalog.Cards.SelectMany(c=>c.ControlConstants).Take(129).ToArray();
        error=null;
        if(exports.Length==0)return values.ToImmutable();
        if(exports.Length>128)
        {
            error=new("control_constant_budget",exports[128].Range);
            return ImmutableDictionary<string,double>.Empty;
        }
        var parameters=ParameterEvaluator.Evaluate(catalog);
        foreach(var export in exports)
        {
            var name=export.Name!;
            if(name.Length is <1 or >64||!(char.IsAsciiLetter(name[0])||name[0]=='_')
                ||name.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c=='_'))||ReservedControlConstants.Contains(name)
                ||values.ContainsKey(name))
            {
                error=new("invalid_or_duplicate_control_constant",export.Target.Range);
                return ImmutableDictionary<string,double>.Empty;
            }
            var scalar=ParameterEvaluator.EvaluateValue(catalog,parameters,export.Value);
            if(scalar.Value is not {} value)
            {
                error=new("invalid_control_constant_value",export.Value.Range);
                return ImmutableDictionary<string,double>.Empty;
            }
            values.Add(name,value);
        }
        foreach(var command in catalog.ControlBlocks.SelectMany(b=>b.Commands))
            if(command.Postprocessing is {} operation && operation.Names.Any(values.ContainsKey))
            {
                error=new("control_constant_assignment",command.Range);
                return ImmutableDictionary<string,double>.Empty;
            }
        return values.ToImmutable();
    }
}
