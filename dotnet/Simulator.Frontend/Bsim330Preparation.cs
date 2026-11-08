using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Finite, explicit parameter slice for the fixed-temperature bulk kernel.</summary>
internal static class Bsim330Preparation
{
    private static readonly string[] ModelNames = ["TOX","NCH","NSUB","VTH0"];
    private static readonly string[] InstanceNames = ["W","L","PD","PS","AD","AS"];

    internal static bool TryBind(DeclarationCatalog catalog, ParameterEvaluation environment, BoundInstance call,
        out Bsim330Model? result)
    {
        result = null;
        if(call.Status != BindingStatus.Bound || call.Target is not {} model || call.Ports.Length != 4)
            return false;
        var values = new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var assignment in model.Parameters)
        {
            if(assignment.Name is not {} name) return false;
            if(name.Equals("level",StringComparison.OrdinalIgnoreCase) || name.Equals("version",StringComparison.OrdinalIgnoreCase))
                continue;
            if(!Bind(name,assignment.Value,model.ScopeId,ModelNames)) return false;
        }
        foreach(var assignment in call.Header.Parameters)
        {
            if(assignment.Name is not {} name || !Bind(name,assignment.Value,call.Header.ScopeId,InstanceNames))
                return false;
        }
        if(ModelNames.Any(n=>!values.ContainsKey(n)) || !values.ContainsKey("L") || !values.ContainsKey("W"))
            return false;
        if(new[]{"TOX","NCH","NSUB","L","W"}.Any(n=>values[n]<=0) ||
            new[]{"PD","PS","AD","AS"}.Any(n=>values.GetValueOrDefault(n)<0))
            return false;
        result = new(model.ModelFamily=="pmos"?-1:1,values["TOX"],values["NCH"],values["NSUB"],values["VTH0"],
            values["W"],values["L"],values.GetValueOrDefault("PD"),values.GetValueOrDefault("PS"),
            values.GetValueOrDefault("AD"),values.GetValueOrDefault("AS"));
        return true;

        bool Bind(string name, SourceSyntax value, int scope, string[] allowed) =>
            allowed.Contains(name,StringComparer.OrdinalIgnoreCase) &&
            ParameterEvaluator.EvaluateValue(catalog,environment,value,scope).Value is double scalar &&
            double.IsFinite(scalar) && values.TryAdd(name,scalar);
    }
}
