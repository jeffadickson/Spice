using System.Collections.Immutable;

namespace Simulator.Syntax;

/// <summary>Atomic execution-time branch selection; syntax recognition retains every original branch.</summary>
public sealed record NetlistConditionalResult(DeckSyntax Syntax, ParameterDiagnostic? Diagnostic);

/// <summary>Selects bounded root netlist branches using the existing generated scalar expression evaluator.</summary>
/// <remarks>Only unconditional root parameters define conditions. Conditional parameters, scoped conditions,
/// unresolved includes and temperature-dependent branch decisions require separate elaboration contracts.</remarks>
public static class NetlistConditionalSelection
{
    private sealed class Frame(bool parent, bool active)
    {
        internal bool Parent { get; } = parent;
        internal bool Active { get; set; } = active;
        internal bool Taken { get; set; } = active;
        internal bool ElseSeen { get; set; }
    }

    /// <summary>Returns selected immutable metadata or the unchanged input and a source-located failure.</summary>
    public static NetlistConditionalResult Select(DeckSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        if (syntax.Declarations is not { } catalog) return new(syntax, null);
        var markers=catalog.DeferredContexts.Where(c=>c.Kind is ".if" or ".elseif" or ".else" or ".endif")
            .OrderBy(c=>c.Range.Start).ToArray();
        if (markers.Length==0) return new(syntax,null);
        NetlistConditionalResult Fail(string code,SourceRange range)=>new(syntax,new(code,"",range));
        if(markers.Length>256 || markers.Any(m=>m.ScopeId!=0 || !m.ConditionalSyntaxValid)
            || !catalog.Resources.IsEmpty || catalog.DeferredContexts.Length!=markers.Length)
            return Fail("unsupported_netlist_conditional_profile",markers[0].Range);

        // Exclude deferred markers only in the private evaluation snapshot. Never
        // publish it: failure must not turn unresolved or duplicate branches into a circuit.
        var environmentCatalog=Copy(catalog,_=>true,[]);
        var environment=ParameterEvaluator.Evaluate(environmentCatalog);
        var stack=new Stack<Frame>();
        var removed=new List<(int Start,int End)>();
        var cursor=0;
        foreach(var marker in markers)
        {
            var active=stack.Count==0 || stack.Peek().Active;
            if(!active) removed.Add((cursor,marker.Range.Start));
            // Branch-local parameters need ordering and instance-specific evaluation;
            // refuse them in this profile, including inside an inactive branch.
            if(stack.Count>0 && catalog.Parameters.Any(p=>p.Syntax.Range.Start>=cursor && p.Syntax.Range.Start<marker.Range.Start))
                return Fail("unsupported_conditional_parameter",marker.Range);
            bool Condition()
            {
                var value=ParameterEvaluator.EvaluateValue(environmentCatalog,environment,marker.Condition!);
                if(value.Value is not {} scalar) throw new InvalidDataException("invalid_netlist_condition");
                return scalar!=0;
            }
            try
            {
                switch(marker.Kind)
                {
                    case ".if":
                        if(stack.Count>=64) return Fail("netlist_conditional_depth",marker.Range);
                        stack.Push(new(active,active && Condition()));
                        break;
                    case ".elseif":
                        if(stack.Count==0 || stack.Peek().ElseSeen) return Fail("netlist_conditional_structure",marker.Range);
                        var alternative=stack.Peek();
                        alternative.Active=alternative.Parent && !alternative.Taken && Condition();
                        alternative.Taken |= alternative.Active;
                        break;
                    case ".else":
                        if(stack.Count==0 || stack.Peek().ElseSeen) return Fail("netlist_conditional_structure",marker.Range);
                        var fallback=stack.Peek();
                        fallback.Active=fallback.Parent && !fallback.Taken;
                        fallback.ElseSeen=true;fallback.Taken=true;
                        break;
                    case ".endif":
                        if(stack.Count==0) return Fail("netlist_conditional_structure",marker.Range);
                        stack.Pop();
                        break;
                }
            }
            catch(InvalidDataException) { return Fail("invalid_netlist_condition",marker.Range); }
            removed.Add((marker.Range.Start,marker.Range.Start+marker.Range.Length));
            cursor=marker.Range.Start+marker.Range.Length;
        }
        if(stack.Count!=0) return Fail("netlist_conditional_structure",markers[^1].Range);
        bool Keep(SourceRange range)=>!removed.Any(span=>range.Start>=span.Start && range.Start<span.End);
        var selected=Copy(catalog,Keep,[]);
        return new(syntax with { Declarations=selected },null);
    }

    // Stable scope/symbol IDs and original source positions survive filtering.
    // Consumers must never observe a branch whose devices were removed while its
    // analyses, models, control blocks or initial conditions remained active.
    private static DeclarationCatalog Copy(DeclarationCatalog c,Func<SourceRange,bool> keep,
        ImmutableArray<DeferredDeclarationContext> deferred)=>new(c.Scopes,
        c.Symbols.Where(s=>keep(s.Range)).ToImmutableArray(),
        c.Parameters.Where(p=>keep(p.Syntax.Range)).ToImmutableArray(),c.Resources,deferred,
        c.Instances.Where(i=>keep(i.Range)).ToImmutableArray(),
        c.LibraryEnds.Where(e=>keep(e.Range)).ToImmutableArray(),
        c.Primitives.Where(p=>keep(p.Range)).ToImmutableArray(),
        c.Cards.Where(card=>keep(card.Range)).ToImmutableArray())
        {
            Analyses=c.Analyses.Where(a=>keep(a.Range)).ToImmutableArray(),
            InitialConditions=c.InitialConditions.Where(i=>keep(i.Range)).ToImmutableArray(),
            ControlBlocks=c.ControlBlocks.Where(b=>keep(b.Range)).ToImmutableArray(),
            XspiceInstances=c.XspiceInstances.Where(x=>keep(x.Range)).ToImmutableArray(),
            BehavioralSources=c.BehavioralSources.Where(b=>keep(b.Range)).ToImmutableArray()
        };
}
