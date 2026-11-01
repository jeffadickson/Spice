using System.Collections.Immutable;
namespace Simulator.Syntax;
/// <summary>Independent observed-reference and root-parameter outcomes; never a circuit-support or solve verdict.</summary>
public sealed record SemanticSummary(string ReferenceStatus,int References,ImmutableDictionary<string,int> ReferenceCounts,
    int Ports,ImmutableDictionary<string,int> ReferenceDiagnostics,ImmutableDictionary<string,int> ReferenceProfiles,ImmutableDictionary<string,int> GraphDiagnostics,string ParameterStatus,int ParameterValues,ImmutableDictionary<string,int> ParameterDiagnostics,ImmutableDictionary<string,int> ParameterSymbols)
{
    /// <summary>Runs pure bounded binding/root-parameter observation without opening resources or executing commands.</summary>
    public static SemanticSummary From(DeclarationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var bindings=InstanceBinder.Bind(catalog);var parameters=ParameterEvaluator.Evaluate(catalog);
        return new(bindings.Instances.IsEmpty?"no_references_observed":bindings.Complete?"bound_observed_references":"incomplete_observed_references",
            bindings.Instances.Length,Enum.GetValues<BindingStatus>().ToImmutableDictionary(s=>s.ToString(),s=>bindings.Instances.Count(i=>i.Status==s)),
            bindings.Instances.Sum(i=>i.Ports.Length),bindings.Instances.GroupBy(i=>i.Code).ToImmutableDictionary(g=>g.Key,g=>g.Count()),
            bindings.Instances.GroupBy(i=>i.Header.Family+":"+i.Status).ToImmutableDictionary(g=>g.Key,g=>g.Count()),bindings.Diagnostics.GroupBy(d=>d.Code).ToImmutableDictionary(g=>g.Key,g=>g.Count()),
            !parameters.Success?parameters.Diagnostics[0].Code:catalog.Parameters.Any(p=>p.ScopeId==0)?"evaluated_root_scope":"no_root_parameters_observed",
            parameters.Values.Count,parameters.Diagnostics.GroupBy(d=>d.Code).ToImmutableDictionary(g=>g.Key,g=>g.Count()),
            parameters.Diagnostics.Where(d=>!string.IsNullOrEmpty(d.Name)).GroupBy(d=>d.Name,StringComparer.OrdinalIgnoreCase).ToImmutableDictionary(g=>g.Key,g=>g.Count(),StringComparer.OrdinalIgnoreCase));
    }
}
