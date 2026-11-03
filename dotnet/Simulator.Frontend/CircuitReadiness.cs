using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>One observed integration/capability issue, never a verdict that SPICE input is invalid.</summary>
public sealed record ReadinessIssue(string Layer,string Code,string Kind,int ScopeId,ImmutableArray<string> InstancePath,SourceRange? Range);
/// <summary>Observed model reference metadata; assignment names are not normalized model equations or defaults.</summary>
public sealed record ModelReadiness(string Instance,string Family,string Binding,string? ModelFamily,ImmutableArray<string> ParameterAssignments);
/// <summary>Structural multi-issue audit. Numeric preparation is still atomic and its first diagnostic is retained separately.</summary>
public sealed record CircuitReadinessReport(bool CanPrepare,bool ActiveGraphComplete,bool NumericalValidationExhaustive,
    ImmutableArray<ReadinessIssue> Issues,ImmutableArray<ModelReadiness> Models,ImmutableArray<BridgeDiagnostic> PreparationDiagnostics)
{
    /// <summary>True when the4096-observation or128-model-assignment diagnostic bound hid additional observations.</summary>
    public bool ObservationsTruncated { get; init; }
}

/// <summary>Explains known active structural blockers without the bridge's first-error bailout.</summary>
public static class CircuitReadiness
{
    /// <summary>Audits known card/header/binding capabilities across bounded reachable scopes, plus global directive policy.</summary>
    /// <remarks>Conditional/unbound/recursive/oversized graphs are explicitly incomplete. Unknown scalar errors behind an earlier
    /// preparation failure are not exhaustively evaluated. No issues list proves solvability, model validity or numeric accuracy.</remarks>
    public static CircuitReadinessReport Audit(DeckSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        var prepared=OperatingPointSelection.Select(syntax).Prepared;
        if(syntax.Declarations is not {} catalog)return new(false,false,false,[],[],prepared.Diagnostics);
        var issues=ImmutableArray.CreateBuilder<ReadinessIssue>();var models=ImmutableArray.CreateBuilder<ModelReadiness>();
        var bound=InstanceBinder.Bind(catalog).Instances.ToLookup(i=>i.Header.ScopeId);
        var cards=catalog.Cards.ToLookup(c=>c.ScopeId);var primitives=catalog.Primitives.ToLookup(p=>p.ScopeId);
        var stack=new HashSet<int>();var frames=0;var truncated=false;var complete=catalog.Resources.IsEmpty&&catalog.DeferredContexts.IsEmpty;
        foreach(var card in catalog.Cards.Where(c=>c.Kind=="extension"&&c.GlobalNodes==null&&OperatingPointSelection.OmissionReason(c)==null))
            AddIssue(new("directive_policy","unqualified_directive",card.Name,card.ScopeId,[],card.Range));
        foreach(var resource in catalog.Resources)AddIssue(new("resource","unresolved_resource",resource.Kind,resource.ScopeId,[],resource.Range));
        foreach(var context in catalog.DeferredContexts)AddIssue(new("elaboration","deferred_context",context.Kind,0,[],context.Range));
        Visit(0,[],0);
        return new(prepared.Success,complete,false,issues.ToImmutable(),models.ToImmutable(),prepared.Diagnostics){ObservationsTruncated=truncated};
        void AddIssue(ReadinessIssue issue){if(issues.Count<4096)issues.Add(issue);else truncated=true;}

        void Visit(int scope,ImmutableArray<string> path,int depth)
        {
            if(depth>32||++frames>128||!stack.Add(scope))
            {complete=false;AddIssue(new("binding","graph_limit_or_recursion","subcall",scope,path,catalog.Scopes[scope].Range));return;}
            try
            {
                foreach(var card in cards[scope])
                {
                    if(OperatingPointSelection.OmissionReason(card)!=null||card.Kind=="extension")continue;
                    if(card.Kind is "passive" or "source" or "vcontrol" or "icontrol" or "subcall" or "paramCard" or "subcircuit" or "model" or "endCard")continue;
                    if(card.Kind=="behavior"&&catalog.BehavioralSources.FirstOrDefault(i=>i.Range==card.Range) is {} behavior)
                    {
                        if(!behavior.Supported)AddIssue(new("behavior_contract","unsupported_behavior_header",card.Kind,scope,path,card.Range));
                        continue;
                    }
                    if(card.Kind=="xspice"&&catalog.XspiceInstances.FirstOrDefault(i=>i.Range==card.Range) is {} xi&&catalog.Lookup(DeclarationKind.Model,xi.Model.Text,scope) is {Status:NameLookupStatus.Found,Provisional:false} lookup&&lookup.Candidates.Single().ModelFamily=="gain"){
                        // Structural analog-gain recognition is distinct from
                        // full parameter/port admission, reported by preparation.
                        if(xi.Ports.Length!=2||xi.Ports.Any(p=>!p.Supported))AddIssue(new("model_contract","unsupported_xspice_gain_ports",card.Kind,scope,path,card.Range));
                        continue;
                    }
                    // Structural unsupported terms remain visible even if an
                    // earlier directive prevented active value evaluation.
                    if(card.Kind=="diode")
                    {
                        var diode=bound[scope].FirstOrDefault(i=>i.Header.Range==card.Range);
                        if(diode!=null&&!SemiconductorPreparation.DiodeAssignmentsAreSupported(diode))
                            AddIssue(new("model_contract","unsupported_diode_profile",card.Kind,scope,path,card.Range));
                        continue;
                    }
                    if(card.Kind=="bjt")
                    {
                        var bjt=bound[scope].FirstOrDefault(i=>i.Header.Range==card.Range);
                        if(bjt!=null&&!BjtPreparation.AssignmentsAreSupported(bjt))AddIssue(new("model_contract","unsupported_bjt_profile",card.Kind,scope,path,card.Range));
                        continue;
                    }
                    if(card.Kind=="switchCard"&&bound[scope].FirstOrDefault(i=>i.Header.Range==card.Range&&i.Status==BindingStatus.Bound&&i.Target?.ModelFamily=="vswitch") is {} sw)
                    {
                        var model=sw.Target!;var names=model.Parameters.Select(p=>p.Name).ToArray();
                        if(model.HasOpaqueModelTerms||!sw.Header.Parameters.IsEmpty||names.Length!=4||names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=4||names.Any(n=>n==null||!new[]{"ron","roff","von","voff"}.Contains(n,StringComparer.OrdinalIgnoreCase)))
                            AddIssue(new("model_contract","unsupported_switch_profile",card.Kind,scope,path,card.Range));
                        continue;
                    }
                    var layer=card.Kind is "diode" or "bjt" or "mos" or "jfet" or "xspice" or "osdi"?"runtime_model":"runtime_family";
                    AddIssue(new(layer,"unsupported_active_family",card.Kind,scope,path,card.Range));
                }
                foreach(var primitive in primitives[scope])
                {
                    if(primitive.Family is "E" or "G"&&primitive.Control==null)
                        AddIssue(new("control_contract","unsupported_control_profile",primitive.Family,scope,path,primitive.Range));
                    else if(primitive.RequiresExcitationProfile&&primitive.Excitation==null)
                        AddIssue(new("source_contract","unsupported_source_profile",primitive.Family,scope,path,primitive.Range));
                    else if(!primitive.Supported&&primitive.Excitation==null)
                        AddIssue(new("primitive_contract","unsupported_primitive_header",primitive.Family,scope,path,primitive.Range));
                }
                foreach(var call in bound[scope])
                {
                    if(call.Header.Family!="X")
                    {
                        if(models.Count<4096)models.Add(new(call.Header.Name,call.Header.Family,call.Status.ToString(),call.Target?.ModelFamily,
                            call.Target?.Parameters.Take(128).Select(p=>p.Name??"<opaque>").ToImmutableArray()??[]));
                        else truncated=true;
                        if(call.Target?.Parameters.Length>128)truncated=true;
                    }
                    if(call.Status!=BindingStatus.Bound)
                    {AddIssue(new("binding",call.Code,call.Header.Family,scope,path,call.Header.Range));if(call.Header.Family=="X")complete=false;continue;}
                    if(call.Header.Family=="X"&&call.Target?.BodyScopeId is {} child)Visit(child,path.Add(call.Header.Name),depth+1);
                }
            }
            finally{stack.Remove(scope);}
        }
    }
}
