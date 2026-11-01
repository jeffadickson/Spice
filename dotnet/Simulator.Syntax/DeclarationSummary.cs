namespace Simulator.Syntax;

// This profile measures catalog export and self-lookup invariants. It does not
// probe device references, evaluate parameters, expand hierarchy or run models.
/// <summary>Declaration counts and self-lookup consistency checks; not instance binding or execution qualification.</summary>
public sealed record DeclarationSummary(string Status,int Scopes,int Models,int Subcircuits,int ScopeParameters,
    int AssignedModelParameters,int Resources,int DeferredContexts,int Ambiguities,int SelfLookups,int SelfLookupFailures)
{
    /// <summary>Checks each observed declaration against its catalog index using grouped candidate sets.</summary>
    public static DeclarationSummary From(DeclarationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var probes=0;var failed=0;
        foreach(var scoped in catalog.Symbols.GroupBy(s=>(s.ScopeId,s.Kind)))
        foreach(var group in scoped.GroupBy(s=>s.Name,StringComparer.OrdinalIgnoreCase))
        {
            var result=catalog.Lookup(scoped.Key.Kind,group.Key,scoped.Key.ScopeId);
            var ids=result.Candidates.Select(c=>c.Id).ToHashSet();
            foreach(var symbol in group){probes++;if(!ids.Contains(symbol.Id))failed++;}
        }
        foreach(var scoped in catalog.Parameters.Where(p=>p.Syntax.Name!=null).GroupBy(p=>p.ScopeId))
        foreach(var group in scoped.GroupBy(p=>p.Syntax.Name!,StringComparer.OrdinalIgnoreCase))
        {
            var result=catalog.LookupParameter(group.Key,scoped.Key);
            var candidates=result.Candidates.ToHashSet();
            foreach(var parameter in group){probes++;if(!candidates.Contains(parameter))failed++;}
        }
        return new(catalog.Status,catalog.Scopes.Length,catalog.Symbols.Count(s=>s.Kind==DeclarationKind.Model),
            catalog.Symbols.Count(s=>s.Kind==DeclarationKind.Subcircuit),catalog.Parameters.Length,
            catalog.Symbols.Where(s=>s.Kind==DeclarationKind.Model).Sum(s=>s.Parameters.Length),catalog.Resources.Length,
            catalog.DeferredContexts.Length,catalog.Diagnostics.Length,probes,failed);
    }
}
