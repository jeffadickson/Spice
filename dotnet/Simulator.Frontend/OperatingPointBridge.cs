using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>Expansion/output budgets, bounded by the existing Rust operating-point contract.</summary>
public sealed record BridgeLimits(int MaxDepth=32,int MaxInstances=128,int MaxElements=1024,int MaxNodes=128);
/// <summary>Capability/validation failure; ranges refer to the supplied syntax text.</summary>
public sealed record BridgeDiagnostic(string Code,SourceRange? Range);
/// <summary>Physical syntax and instance path for one uniquely named wire element.</summary>
public sealed record ElementOrigin(string WireId,string LocalName,ImmutableArray<string> InstancePath,SourceRange Range);
/// <summary>Atomic typed request preparation; this operation does not run a simulator.</summary>
public sealed record BridgeResult(SimulationRequest? Request,ImmutableDictionary<string,string> RootNodes,ImmutableArray<ElementOrigin> Origins,ImmutableArray<BridgeDiagnostic> Diagnostics)
{
    /// <summary>All active cards fit the bounded R/V/I operating-point profile.</summary>
    public bool Success=>Request!=null&&Diagnostics.IsEmpty;
}
/// <summary>Generated frontend to existing typed Rust API adapter, independent of solver implementation.</summary>
public static class OperatingPointBridge
{
    /// <summary>Flattens active R/V/I subcircuits with evaluated parameters into an operating-point request.</summary>
    public static BridgeResult Lower(DeckSyntax syntax,BridgeLimits? limits=null,string backend="lu")
    {
        ArgumentNullException.ThrowIfNull(syntax);limits??=new();
        if(limits.MaxDepth<0||limits.MaxDepth>128||limits.MaxInstances<1||limits.MaxElements<1||limits.MaxElements>1024||limits.MaxNodes<1||limits.MaxNodes>128)throw new ArgumentOutOfRangeException(nameof(limits));
        BridgeResult Failure(string code,SourceRange? range)=>new(null,ImmutableDictionary.Create<string,string>(StringComparer.OrdinalIgnoreCase),[],[new(code,range)]);
        if(syntax.Status!="accepted_structure"||syntax.Declarations is not {} catalog)return Failure("syntax_not_accepted",null);
        if(backend is not ("lu" or "qr" or "faer"))return Failure("unsupported_backend",null);
        var rootEnd=catalog.Cards.FirstOrDefault(c=>c.ScopeId==0&&c.Kind=="endCard");
        if(rootEnd!=null&&catalog.Cards.Any(c=>c.ScopeId==0&&c.Range.Start>rootEnd.Range.Start))return Failure("unprocessed_end_tail",rootEnd.Range);
        var nodes=new List<string>{"0"};var elements=new List<Element>();var origins=new List<ElementOrigin>();
        var roots=ImmutableDictionary.CreateBuilder<string,string>(StringComparer.OrdinalIgnoreCase);
        var bound=InstanceBinder.Bind(catalog).Instances.ToLookup(i=>i.Header.ScopeId);
        var primitives=catalog.Primitives.ToLookup(p=>p.ScopeId);var cards=catalog.Cards.ToLookup(c=>c.ScopeId);
        var localNodes=new Dictionary<int,Dictionary<string,int>>();var active=new HashSet<int>();var frames=0;
        var rootParameters=ParameterEvaluator.Evaluate(catalog);
        if(!rootParameters.Success)return Failure(rootParameters.Diagnostics[0].Code,rootParameters.Diagnostics[0].Range);
        try
        {
            Expand(0,rootParameters,new(StringComparer.OrdinalIgnoreCase),[],0);
            if(elements.Count==0)throw new BridgeFailure("empty_circuit",null);
            return new(new("frontend_op",new(nodes.ToArray(),elements.ToArray()),new OperatingPoint(),backend),roots.ToImmutable(),origins.ToImmutableArray(),[]);
        }
        catch(BridgeFailure e){return Failure(e.Code,e.Range);}
        void Expand(int scope,ParameterEvaluation environment,Dictionary<string,int> ports,ImmutableArray<string> path,int depth)
        {
            if(depth>limits.MaxDepth)throw new BridgeFailure("expansion_depth",catalog.Scopes[scope].Range);
            if(!active.Add(scope))throw new BridgeFailure("recursive_reference",catalog.Scopes[scope].Range);
            if(++frames>limits.MaxInstances)throw new BridgeFailure("instance_budget",catalog.Scopes[scope].Range);
            var frame=frames-1;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Explicit allowlist prevents ignored analyses, controls, global
                // aliases or unsupported devices from becoming successful circuits.
                foreach(var card in cards[scope])if(card.Kind=="endCard"&&scope!=0||card.Kind is not ("passive" or "source" or "subcall" or "paramCard" or "subcircuit" or "model" or "endCard" or "op"))
                    throw new BridgeFailure("unsupported_active_card",card.Range);
                foreach(var primitive in primitives[scope])
                {
                    if(primitive.Family is not ("R" or "V" or "I"))throw new BridgeFailure("unsupported_active_card",primitive.Range);
                    if(!names.Add(primitive.Name))throw new BridgeFailure("duplicate_element",primitive.Range);
                    if(!primitive.Supported||primitive.Value==null)throw new BridgeFailure("unsupported_primitive_header",primitive.Range);
                    var scalar=ParameterEvaluator.EvaluateValue(catalog,environment,primitive.Value);
                    if(scalar.Value is not {} value)throw new BridgeFailure(scalar.Diagnostics[0].Code,scalar.Diagnostics[0].Range);
                    if(primitive.Family=="R"&&value<=0)throw new BridgeFailure("invalid_resistance",primitive.Range);
                    var p=Node(primitive.P.Text);var n=Node(primitive.N.Text);
                    if(p==n)throw new BridgeFailure("shorted_primitive",primitive.Range);
                    if(elements.Count>=limits.MaxElements)throw new BridgeFailure("element_budget",primitive.Range);
                    // Compact wire identifiers are collision-free and stay within
                    // Rust's byte limits; original spellings remain in provenance.
                    var id="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    elements.Add(primitive.Family switch{"R"=>new Resistor(id,p,n,value),"V"=>new Voltage(id,p,n,value),_=>new Current(id,p,n,value)});
                    origins.Add(new(id,primitive.Name,path,primitive.Range));
                }
                foreach(var call in bound[scope])
                {
                    if(!names.Add(call.Header.Name))throw new BridgeFailure("duplicate_element",call.Header.Range);
                    if(call.Header.Family!="X")throw new BridgeFailure("unsupported_active_card",call.Header.Range);
                    if(call.Status!=BindingStatus.Bound)throw new BridgeFailure(call.Code,call.Header.Range);
                    var childPorts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                    foreach(var port in call.Ports)
                    {
                        if(port.Formal!.Text=="0")throw new BridgeFailure("reserved_formal_ground",port.Formal.Range);
                        childPorts.Add(port.Formal.Text,Node(port.Actual.Text));
                    }
                    var child=ParameterEvaluator.ForInstance(catalog,call,environment);
                    if(!child.Success)throw new BridgeFailure(child.Diagnostics[0].Code,child.Diagnostics[0].Range);
                    Expand(call.Target!.BodyScopeId!.Value,child,childPorts,path.Add(call.Header.Name),depth+1);
                }
            }
            finally{active.Remove(scope);}
            int Node(string name)
            {
                if(name=="0")return 0;
                if(ports.TryGetValue(name,out var index))return index;
                if(!localNodes.TryGetValue(frame,out var table)){table=new(StringComparer.OrdinalIgnoreCase);localNodes.Add(frame,table);}
                if(table.TryGetValue(name,out index))return index;
                if(nodes.Count>=limits.MaxNodes)throw new BridgeFailure("node_budget",catalog.Scopes[scope].Range);
                index=nodes.Count;var wire="n"+index.ToString(System.Globalization.CultureInfo.InvariantCulture);nodes.Add(wire);table.Add(name,index);
                if(scope==0)roots[name]=wire;return index;
            }
        }
    }
    private sealed class BridgeFailure(string code,SourceRange? range):Exception(code)
    {internal string Code{get;}=code;internal SourceRange? Range{get;}=range;}
}
