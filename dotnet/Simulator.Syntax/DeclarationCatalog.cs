using System.Collections.Immutable;

namespace Simulator.Syntax;

/// <summary>Declaration namespaces; numerical model support is a separate capability.</summary>
public enum DeclarationKind
{
    /// <summary>A named model declaration.</summary>
    Model,
    /// <summary>A named subcircuit declaration with a lexical body scope.</summary>
    Subcircuit
}
/// <summary>Lookup outcome under the conservative declaration-catalog policy.</summary>
public enum NameLookupStatus
{
    /// <summary>Observed candidates exist; parameter candidates remain ordered and unevaluated.</summary>
    Found,
    /// <summary>No observed declaration exists and no unresolved source can provide one.</summary>
    Missing,
    /// <summary>Multiple named definitions exist in the nearest matching scope.</summary>
    Ambiguous,
    /// <summary>Unresolved resources, conditional contexts or opaque parameter targets prevent a definitive absence claim.</summary>
    Deferred
}
/// <summary>Exact source syntax, without parameter evaluation or whitespace reconstruction.</summary>
public sealed record SourceSyntax(string Text,SourceRange Range);
/// <summary>A parameter target and value. Null Name denotes a non-simple, uninterpreted assignment target.</summary>
public sealed record ParameterSyntax(string? Name,SourceSyntax Target,SourceSyntax Value,SourceRange Range);
/// <summary>A lexical scope; IDs are catalog-local and parents precede children.</summary>
public sealed record DeclarationScope(int Id,int? ParentId,string? Name,SourceRange Range);
/// <summary>A named definition. Header arguments and model/default parameters remain syntax.</summary>
public sealed record NamedDeclaration(int Id,DeclarationKind Kind,string Name,int ScopeId,int? BodyScopeId,
    string? ModelFamily,ImmutableArray<SourceSyntax> HeaderArguments,ImmutableArray<ParameterSyntax> Parameters,SourceRange Range)
{
    /// <summary>Generated positional formal ports under the bounded header profile.</summary>
    public ImmutableArray<SourceSyntax> FormalPorts { get; init; } = [];
    /// <summary>False when header structure needs a further semantic profile.</summary>
    public bool HasTypedPorts { get; init; }
    /// <summary>True when model terms include syntax beyond direct scalar assignments and separators.</summary>
    public bool HasOpaqueModelTerms { get; init; }
}
/// <summary>An ordered header-default or .param assignment in a lexical scope.</summary>
public sealed record ScopedParameter(int ScopeId,string Origin,ParameterSyntax Syntax);
/// <summary>An unresolved include/library declaration, never an instruction to open a file.</summary>
public sealed record DeclaredResource(int ScopeId,string Kind,SourceSyntax Target,SourceRange Range)
{
    /// <summary>Requested external library section; null for include cards or one-argument library declarations.</summary>
    public SourceSyntax? Section { get; init; }
}
/// <summary>A generated library closing delimiter, with an optional section name.</summary>
public sealed record LibrarySectionEnd(SourceSyntax? Name,bool Supported,SourceRange Range)
{
    /// <summary>Lexical declaration scope containing the delimiter.</summary>
    public int ScopeId { get; init; }
}
/// <summary>A conditional or alternate parameter declaration whose semantics remain unresolved.</summary>
public sealed record DeferredDeclarationContext(string Kind,SourceRange Range);
/// <summary>Catalog ambiguity with both physical declaration locations; not a ngspice syntax verdict.</summary>
public sealed record DeclarationDiagnostic(string Code,string Name,SourceRange Range,SourceRange Related);
/// <summary>Observed lookup candidates. Provisional means unresolved inputs may still affect the catalog.</summary>
public sealed record DeclarationLookup<T>(NameLookupStatus Status,ImmutableArray<T> Candidates,bool Provisional);

/// <summary>Immutable declaration catalog and lexical lookup, independent of instance binding and execution.</summary>
public sealed class DeclarationCatalog
{
    private readonly Dictionary<(DeclarationKind Kind,string Name),ImmutableArray<NamedDeclaration>>[] symbolsByScope;
    private readonly Dictionary<string,ImmutableArray<ScopedParameter>>[] parametersByScope;
    private readonly bool opaqueParameters;
    /// <summary>Ordered generated analysis headers, retaining scope and physical source fields.</summary>
    public ImmutableArray<AnalysisSyntax> Analyses { get; init; } = [];
    /// <summary>Generated primitive headers; evaluation and execution are separate stages.</summary>
    public ImmutableArray<PrimitiveSyntax> Primitives { get; }
    /// <summary>Generated code-model ports; model-specific execution admission remains separate.</summary>
    public ImmutableArray<XspiceInstanceSyntax> XspiceInstances { get; internal init; } = [];
    /// <summary>All statement kinds with lexical owners, including unsupported/opaque cards.</summary>
    public ImmutableArray<ScopedCard> Cards { get; }
    /// <summary>Generated library closing delimiters; not external file requests.</summary>
    public ImmutableArray<LibrarySectionEnd> LibraryEnds { get; }
    /// <summary>Observed X/D/Q/M/J/S/W headers; other device families are outside reference binding.</summary>
    public ImmutableArray<InstanceHeader> Instances { get; }
    /// <summary>All lexical scopes, with root at ID zero.</summary>
    public ImmutableArray<DeclarationScope> Scopes { get; }
    /// <summary>Definitions in physical traversal order, including unused declarations.</summary>
    public ImmutableArray<NamedDeclaration> Symbols { get; }
    /// <summary>Ordered scope parameters; model parameters stay attached to their model definitions.</summary>
    public ImmutableArray<ScopedParameter> Parameters { get; }
    /// <summary>Unresolved resources; this catalog performs no filesystem operations.</summary>
    public ImmutableArray<DeclaredResource> Resources { get; }
    /// <summary>Uninterpreted conditions and alternate parameter declarations; lookup stays provisional.</summary>
    public ImmutableArray<DeferredDeclarationContext> DeferredContexts { get; }
    /// <summary>Duplicate-definition ambiguities under this profile.</summary>
    public ImmutableArray<DeclarationDiagnostic> Diagnostics { get; }
    /// <summary>Catalog completeness, never an elaborated-circuit or simulation readiness claim.</summary>
    public string Status=>Diagnostics.IsEmpty?"catalogued":"ambiguous_declarations";

    internal DeclarationCatalog(ImmutableArray<DeclarationScope> scopes,ImmutableArray<NamedDeclaration> symbols,
        ImmutableArray<ScopedParameter> parameters,ImmutableArray<DeclaredResource> resources,ImmutableArray<DeferredDeclarationContext> deferredContexts,ImmutableArray<InstanceHeader> instances,ImmutableArray<LibrarySectionEnd> libraryEnds,ImmutableArray<PrimitiveSyntax> primitives,ImmutableArray<ScopedCard> cards)
    {
        Scopes=scopes;Symbols=symbols;Parameters=parameters;Resources=resources;DeferredContexts=deferredContexts;Instances=instances;LibraryEnds=libraryEnds;Primitives=primitives;Cards=cards;
        symbolsByScope=new Dictionary<(DeclarationKind,string),ImmutableArray<NamedDeclaration>>[scopes.Length];
        parametersByScope=new Dictionary<string,ImmutableArray<ScopedParameter>>[scopes.Length];
        var diagnostics=ImmutableArray.CreateBuilder<DeclarationDiagnostic>();
        // Group once, rather than rescanning every declaration for every scope
        // (large decks contain tens of thousands of independent definitions).
        var symbolGroups=symbols.ToLookup(s=>s.ScopeId);var parameterGroups=parameters.ToLookup(p=>p.ScopeId);
        for(var scope=0;scope<scopes.Length;scope++)
        {
            var table=new Dictionary<(DeclarationKind,string),ImmutableArray<NamedDeclaration>>(new SymbolComparer());
            foreach(var group in symbolGroups[scope].GroupBy(s=>(s.Kind,s.Name),new SymbolComparer()))
            {
                var candidates=group.ToImmutableArray();table.Add(group.Key,candidates);
                foreach(var duplicate in candidates.Skip(1))diagnostics.Add(new("duplicate_definition",duplicate.Name,duplicate.Range,candidates[0].Range));
            }
            symbolsByScope[scope]=table;
            parametersByScope[scope]=parameterGroups[scope].Where(p=>p.Syntax.Name!=null)
                .GroupBy(p=>p.Syntax.Name!,StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g=>g.Key,g=>g.OrderBy(p=>p.Syntax.Range.Start).ToImmutableArray(),StringComparer.OrdinalIgnoreCase);
        }
        opaqueParameters=parameters.Any(p=>p.Syntax.Name==null);
        Diagnostics=diagnostics.ToImmutable();
    }

    /// <summary>Finds all definitions in the nearest matching lexical scope; forward definitions are visible.</summary>
    public DeclarationLookup<NamedDeclaration> Lookup(DeclarationKind kind,string name,int scopeId)
    {
        Validate(name,scopeId);
        if(kind is not (DeclarationKind.Model or DeclarationKind.Subcircuit))throw new ArgumentOutOfRangeException(nameof(kind));
        var provisional=!Resources.IsEmpty||!DeferredContexts.IsEmpty;
        for(int? scope=scopeId;scope!=null;scope=Scopes[scope.Value].ParentId)
            if(symbolsByScope[scope.Value].TryGetValue((kind,name),out var candidates))
                return new(candidates.Length==1?NameLookupStatus.Found:NameLookupStatus.Ambiguous,candidates,provisional);
        return new(provisional?NameLookupStatus.Deferred:NameLookupStatus.Missing,[],provisional);
    }

    /// <summary>Returns ordered unevaluated assignments from the nearest scope containing the parameter.</summary>
    /// <remarks>Repeated assignments are retained; this does not select a final value or implement override semantics.</remarks>
    public DeclarationLookup<ScopedParameter> LookupParameter(string name,int scopeId)
    {
        Validate(name,scopeId);var provisional=!Resources.IsEmpty||!DeferredContexts.IsEmpty||opaqueParameters;
        for(int? scope=scopeId;scope!=null;scope=Scopes[scope.Value].ParentId)
            if(parametersByScope[scope.Value].TryGetValue(name,out var candidates))return new(NameLookupStatus.Found,candidates,provisional);
        return new(provisional?NameLookupStatus.Deferred:NameLookupStatus.Missing,[],provisional);
    }

    private void Validate(string name,int scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if(scopeId<0||scopeId>=Scopes.Length)throw new ArgumentOutOfRangeException(nameof(scopeId));
    }
    private sealed class SymbolComparer : IEqualityComparer<(DeclarationKind Kind,string Name)>
    {
        public bool Equals((DeclarationKind Kind,string Name) x,(DeclarationKind Kind,string Name) y)=>x.Kind==y.Kind&&StringComparer.OrdinalIgnoreCase.Equals(x.Name,y.Name);
        public int GetHashCode((DeclarationKind Kind,string Name) value)=>HashCode.Combine(value.Kind,StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name));
    }
}
