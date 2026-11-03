using System.Collections.Immutable;
namespace Simulator.Syntax;

/// <summary>Reference-binding outcome; binding never implies numerical support.</summary>
public enum BindingStatus
{
    /// <summary>A unique supported reference and terminal contract was bound.</summary>
    Bound,
    /// <summary>No definition was found.</summary>
    Missing,
    /// <summary>Several applicable definitions were found.</summary>
    Ambiguous,
    /// <summary>Unresolved external or conditional input prevents a definitive binding.</summary>
    Deferred,
    /// <summary>An observed header violates the bounded binding contract.</summary>
    Invalid,
    /// <summary>The header or family requires an unimplemented semantic profile.</summary>
    Unsupported
}
/// <summary>Generated-tree positional header and ordered unevaluated assignments.</summary>
public sealed record InstanceHeader(string Name,string Family,int ScopeId,ImmutableArray<SourceSyntax> Positional,
    ImmutableArray<ParameterSyntax> Parameters,bool Supported,SourceRange Range);
/// <summary>One zero-based connection; Formal is null for model terminal ordinals without named source declarations.</summary>
public sealed record PortBinding(SourceSyntax? Formal,SourceSyntax Actual,int Ordinal);
/// <summary>A reference binding or explicit diagnostic, without hierarchy flattening.</summary>
public sealed record BoundInstance
{
    /// <summary>The immutable generated header belonging to the source catalog.</summary>
    public InstanceHeader Header { get; }
    /// <summary>Reference outcome under the bounded binding profile.</summary>
    public BindingStatus Status { get; }
    /// <summary>Stable outcome code.</summary>
    public string Code { get; }
    /// <summary>The uniquely bound definition, when available.</summary>
    public NamedDeclaration? Target { get; }
    /// <summary>Ordered connections; empty on failure or a zero-port call.</summary>
    public ImmutableArray<PortBinding> Ports { get; }
    internal DeclarationCatalog Catalog { get; }
    // Only the binder can create a result. Get-only properties prevent a public
    // record 'with' expression from changing its header/target after validation.
    internal BoundInstance(DeclarationCatalog catalog,InstanceHeader header,BindingStatus status,string code,NamedDeclaration? target,ImmutableArray<PortBinding> ports)
    {Catalog=catalog;Header=header;Status=status;Code=code;Target=target;Ports=ports;}
}
/// <summary>A graph diagnostic with a physical source origin.</summary>
public sealed record BindingDiagnostic(string Code,SourceRange Range);
/// <summary>Bindings cover observed X/D/Q/M/J/S/W references, not every device or an executable circuit.</summary>
public sealed record ReferenceBindingReport(ImmutableArray<BoundInstance> Instances,ImmutableArray<BindingDiagnostic> Diagnostics)
{
    /// <summary>All observed references satisfy this profile and the declaration graph is acyclic.</summary>
    public bool Complete=>Diagnostics.IsEmpty&&Instances.All(i=>i.Status==BindingStatus.Bound);
}
/// <summary>Pure lexical reference binding against immutable declarations; performs no I/O or simulation.</summary>
public static class InstanceBinder
{
    /// <summary>Binds ordered subcircuit ports and supported model-family terminal contracts.</summary>
    public static ReferenceBindingReport Bind(DeclarationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var bound=catalog.Instances.Select(h=>BindOne(catalog,h)).ToImmutableArray();
        var diagnostics=ImmutableArray.CreateBuilder<BindingDiagnostic>();
        foreach(var group in catalog.Instances.GroupBy(h=>h.ScopeId).SelectMany(g=>g.GroupBy(h=>h.Name,StringComparer.OrdinalIgnoreCase)).Where(g=>g.Count()>1))
            foreach(var duplicate in group.Skip(1))diagnostics.Add(new("duplicate_instance",duplicate.Range));
        var edges=bound.Where(i=>i.Header.Family=="X"&&i.Status==BindingStatus.Bound).ToLookup(i=>i.Header.ScopeId);
        // A scope's graph is checked once. Cycles are diagnostics about potential
        // expansion, including unused definitions, never a flattened-circuit claim.
        var colors=new byte[catalog.Scopes.Length];
        foreach(var scope in catalog.Scopes)Visit(scope.Id,0);
        return new(bound,diagnostics.ToImmutable());
        void Visit(int scope,int depth)
        {
            if(colors[scope]==2)return;
            if(depth>128){diagnostics.Add(new("binding_depth",catalog.Scopes[scope].Range));return;}
            colors[scope]=1;
            foreach(var edge in edges[scope])
            {
                var target=edge.Target!.BodyScopeId!.Value;
                if(colors[target]==1)diagnostics.Add(new("recursive_reference",edge.Header.Range));
                else if(colors[target]==0)Visit(target,depth+1);
            }
            colors[scope]=2;
        }
    }
    private static BoundInstance BindOne(DeclarationCatalog catalog,InstanceHeader header)
    {
        BoundInstance Fail(BindingStatus status,string code)=>new(catalog,header,status,code,null,[]);
        if(!header.Supported||header.Positional.IsEmpty)return Fail(BindingStatus.Unsupported,"opaque_header");
        if(header.Family=="X")
        {
            var lookup=catalog.Lookup(DeclarationKind.Subcircuit,header.Positional[^1].Text,header.ScopeId);
            var failure=LookupFailure(lookup);if(failure!=null)return Fail(failure.Value.Status,failure.Value.Code);
            var target=lookup.Candidates[0];
            if(!target.HasTypedPorts)return Fail(BindingStatus.Unsupported,"opaque_formal_ports");
            if(target.FormalPorts.Select(p=>p.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=target.FormalPorts.Length)
                return Fail(BindingStatus.Invalid,"duplicate_formal_port");
            if(header.Positional.Length-1!=target.FormalPorts.Length)return Fail(BindingStatus.Invalid,"port_arity");
            return new(catalog,header,BindingStatus.Bound,"bound",target,target.FormalPorts.Select((f,i)=>new PortBinding(f,header.Positional[i],i)).ToImmutableArray());
        }
        // Enumerate only declared model positions allowed by explicit device
        // contracts. The grammar's Q/M partition is not a semantic arity oracle.
        var arities=header.Family switch{"D" or "W"=>new[]{2},"Q"=>new[]{3,4},"M"=>new[]{4},"J"=>new[]{3},"S"=>new[]{4},_=>Array.Empty<int>()};
        var candidates=new List<(NamedDeclaration Model,int Arity)>();var provisional=false;var ambiguous=false;var queried=false;var foundName=false;
        foreach(var arity in arities)
        {
            if(header.Positional.Length!=arity+1+(header.Family=="W"?1:0))continue;
            queried=true;
            var lookup=catalog.Lookup(DeclarationKind.Model,header.Positional[^1].Text,header.ScopeId);
            foundName|=!lookup.Candidates.IsEmpty;
            provisional|=lookup.Provisional;ambiguous|=lookup.Status==NameLookupStatus.Ambiguous;
            foreach(var model in lookup.Candidates.Where(m=>FamilyMatches(header.Family,m.ModelFamily)))candidates.Add((model,arity));
        }
        if(provisional)return Fail(BindingStatus.Deferred,"provisional_reference");
        if(ambiguous||candidates.Count>1)return Fail(BindingStatus.Ambiguous,"ambiguous_reference");
        if(candidates.Count==0)return queried&&!foundName?Fail(BindingStatus.Missing,"missing_reference"):Fail(BindingStatus.Unsupported,"unsupported_model_contract");
        var found=candidates[0];
        // Model terminals have ordinal positions; model-specific names are not
        // invented. W's controlling source remains in its positional header.
        return new(catalog,header,BindingStatus.Bound,"bound",found.Model,header.Positional.Take(found.Arity).Select((a,i)=>new PortBinding(null,a,i)).ToImmutableArray());
    }
    private static (BindingStatus Status,string Code)? LookupFailure(DeclarationLookup<NamedDeclaration> lookup)
    {
        if(lookup.Provisional)return(BindingStatus.Deferred,"provisional_reference");
        return lookup.Status switch{NameLookupStatus.Missing=>(BindingStatus.Missing,"missing_reference"),NameLookupStatus.Ambiguous=>(BindingStatus.Ambiguous,"ambiguous_reference"),NameLookupStatus.Deferred=>(BindingStatus.Deferred,"provisional_reference"),_=>null};
    }
    private static bool FamilyMatches(string device,string? model)=>device switch
    {"D"=>model=="d","Q"=>model is "npn" or "pnp","M"=>model is "nmos" or "pmos","J"=>model is "njf" or "pjf","S"=>model is "sw" or "vswitch","W"=>model=="csw",_=>false};
}
