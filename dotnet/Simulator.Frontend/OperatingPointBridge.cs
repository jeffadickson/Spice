using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>Expansion/output budgets, bounded by the existing Rust operating-point contract.</summary>
public sealed record BridgeLimits(int MaxDepth=32,int MaxInstances=128,int MaxElements=1024,int MaxNodes=128)
{
    /// <summary>Selected frontend dialect policy. Strict Lower retains its legacy literal-node contract.</summary>
    public GroundAliasPolicy GroundAliases {get;init;}=GroundAliasPolicy.Automatic;
}
/// <summary>Capability/validation failure; ranges refer to the supplied syntax text.</summary>
public sealed record BridgeDiagnostic(string Code,SourceRange? Range);
/// <summary>Physical syntax and instance path for one uniquely named wire element.</summary>
public sealed record ElementOrigin(string WireId,string LocalName,ImmutableArray<string> InstancePath,SourceRange Range)
{
    /// <summary>Original native branch identity, when a physical V/L/E/H or admitted voltage-output model has a directly comparable branch. Synthetic decomposition branches have no native identity.</summary>
    public string? NativeBranchName {get;init;}
}
/// <summary>Atomic typed request preparation; this operation does not run a simulator.</summary>
public sealed record BridgeResult(SimulationRequest? Request,ImmutableDictionary<string,string> RootNodes,ImmutableArray<ElementOrigin> Origins,ImmutableArray<BridgeDiagnostic> Diagnostics)
{
    /// <summary>Native hierarchical names for original internal nodes, excluding decomposition nodes.</summary>
    public ImmutableDictionary<string,string> InternalNodes {get;init;}=ImmutableDictionary<string,string>.Empty;
    /// <summary>Resolved selected policy, or Disabled for strict literal-node lowering. Oracle adapters must use the same native mode.</summary>
    public GroundAliasPolicy GroundAliases {get;init;}=GroundAliasPolicy.Disabled;
    /// <summary>Explicit complex AC replacements for every active independent source, including zero for DC-only sources. DC bias remains in Request.Circuit.</summary>
    public ImmutableArray<AcSourcePhasor> AcSources { get; init; } = [];
    // Requested AC can replace resistance by wire ID after checking linearity.
    internal ImmutableDictionary<string,double> AcResistances {get;init;}=ImmutableDictionary<string,double>.Empty;
    /// <summary>Number of active source headers with an explicitly declared AC amplitude.</summary>
    public int ExplicitAcSources { get; init; }
    /// <summary>Evaluated waveform fields, without an implicit transient analysis or guessed timing defaults.</summary>
    public ImmutableArray<PreparedSourceWaveform> SourceWaveforms {get;init;}=[];
    /// <summary>All active cards fit the requested bounded operating-point preparation profile.</summary>
    public bool Success=>Request!=null&&Diagnostics.IsEmpty;
}
/// <summary>Generated frontend to existing typed Rust API adapter, independent of solver implementation.</summary>
public static class OperatingPointBridge
{
    /// <summary>Flattens active R/V/I subcircuits with evaluated parameters into an operating-point request.</summary>
    public static BridgeResult Lower(DeckSyntax syntax,BridgeLimits? limits=null,string backend="lu")
        =>LowerCore(syntax,limits,backend,false,null);

    // Only the explicit selection adapter supplies omissions. The legacy public
    // Lower contract remains strict R/V/I with no ignored input instructions.
    internal static BridgeResult LowerSelected(DeckSyntax syntax,BridgeLimits? limits,string backend,ImmutableHashSet<int> omittedStarts,bool requested)
        =>LowerCore(syntax,limits,backend,true,omittedStarts,requested);

    private static BridgeResult LowerCore(DeckSyntax syntax,BridgeLimits? limits,string backend,bool selected,ImmutableHashSet<int>? omittedStarts,bool requested=false)
    {
        ArgumentNullException.ThrowIfNull(syntax);limits??=new();
        if(limits.MaxDepth<0||limits.MaxDepth>128||limits.MaxInstances<1||limits.MaxElements<1||limits.MaxElements>1024||limits.MaxNodes<1||limits.MaxNodes>128)throw new ArgumentOutOfRangeException(nameof(limits));
        if(!Enum.IsDefined(limits.GroundAliases))throw new ArgumentOutOfRangeException(nameof(limits));
        BridgeResult Failure(string code,SourceRange? range)=>new(null,ImmutableDictionary.Create<string,string>(StringComparer.OrdinalIgnoreCase),[],[new(code,range)]);
        if(syntax.Status!="accepted_structure"||syntax.Declarations is not {} catalog)return Failure("syntax_not_accepted",null);
        if(backend is not ("lu" or "qr" or "faer"))return Failure("unsupported_backend",null);
        // Extensions may have globally physical effects even when spelled in
        // an unused body. Only the explicit selection's named observations and
        // analyses may be omitted; all other extensions remain conservative.
        if(selected && catalog.Cards.FirstOrDefault(c=>c.Kind=="extension"&&omittedStarts?.Contains(c.Range.Start)!=true&&!c.Name.Equals(".global",StringComparison.OrdinalIgnoreCase)) is {} unsupportedDirective)
            return Failure("unsupported_physical_directive",unsupportedDirective.Range);
        var rootEnd=catalog.Cards.FirstOrDefault(c=>c.ScopeId==0&&c.Kind=="endCard");
        if(rootEnd!=null&&catalog.Cards.Any(c=>c.ScopeId==0&&c.Range.Start>rootEnd.Range.Start))return Failure("unprocessed_end_tail",rootEnd.Range);
        var nodes=new List<string>{"0"};var elements=new List<Element>();var origins=new List<ElementOrigin>();
        var acResistances=ImmutableDictionary.CreateBuilder<string,double>();
        var acSources=new List<AcSourcePhasor>();var explicitAcSources=0;var sourceWaveforms=new List<PreparedSourceWaveform>();
        var roots=ImmutableDictionary.CreateBuilder<string,string>(StringComparer.OrdinalIgnoreCase);
        var internalNodes=ImmutableDictionary.CreateBuilder<string,string>(StringComparer.OrdinalIgnoreCase);
        var bound=InstanceBinder.Bind(catalog).Instances.ToLookup(i=>i.Header.ScopeId);
        GroundBinding? groundBinding=null;
        var primitives=catalog.Primitives.ToLookup(p=>p.ScopeId);var cards=catalog.Cards.ToLookup(c=>c.ScopeId);
        var globalNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var globalNodes=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var localNodes=new Dictionary<int,Dictionary<string,int>>();var active=new HashSet<int>();var frames=0;
        var rootParameters=ParameterEvaluator.Evaluate(catalog);
        if(!rootParameters.Success)return Failure(rootParameters.Diagnostics[0].Code,rootParameters.Diagnostics[0].Range);
        try
        {
            if(selected){
                var reachable=new HashSet<int>();CollectGlobals(0,reachable,0);
                var requiresPspice=reachable.Any(scope=>bound[scope].Any(c=>c.Status==BindingStatus.Bound&&c.Target?.ModelFamily=="vswitch"));
                groundBinding=GroundBinding.Prepare(catalog,reachable,requiresPspice,limits.GroundAliases,out var groundFailure);
                if(groundFailure!=null)throw new BridgeFailure(groundFailure.Code,groundFailure.Range);
                if(groundBinding!.Policy!=GroundAliasPolicy.Disabled)globalNames.Remove("gnd");
            }
            Expand(0,rootParameters,new(StringComparer.OrdinalIgnoreCase),[],0);
            if(elements.Count==0)throw new BridgeFailure("empty_circuit",null);
            return new(new("frontend_op",new(nodes.ToArray(),elements.ToArray()),new OperatingPoint(),backend),roots.ToImmutable(),origins.ToImmutableArray(),[])
            {InternalNodes=internalNodes.ToImmutable(),AcSources=acSources.ToImmutableArray(),AcResistances=acResistances.ToImmutable(),ExplicitAcSources=explicitAcSources,SourceWaveforms=sourceWaveforms.ToImmutableArray(),GroundAliases=selected?groundBinding!.Policy:GroundAliasPolicy.Disabled};
        }
        catch(BridgeFailure e){return Failure(e.Code,e.Range);}
        void CollectGlobals(int scope,HashSet<int> seen,int depth)
        {
            // ngspice promotes .global only from instantiated bodies. Discover
            // all reachable declarations before allocating any instance nodes;
            // declaration order must not change aliases or formal precedence.
            if(depth>limits.MaxDepth)throw new BridgeFailure("expansion_depth",catalog.Scopes[scope].Range);
            if(seen.Contains(scope))return;
            if(seen.Count>=limits.MaxInstances)throw new BridgeFailure("instance_budget",catalog.Scopes[scope].Range);
            seen.Add(scope);
            foreach(var card in cards[scope].Where(c=>c.Name.Equals(".global",StringComparison.OrdinalIgnoreCase))){
                if(card.GlobalNodes is not {} globals)throw new BridgeFailure("unsupported_global_declaration",card.Range);
                foreach(var node in globals)globalNames.Add(node.Text);
            }
            foreach(var call in bound[scope].Where(c=>c.Header.Family=="X"&&c.Status==BindingStatus.Bound&&c.Target?.BodyScopeId!=null))CollectGlobals(call.Target!.BodyScopeId!.Value,seen,depth+1);
        }
        void Expand(int scope,ParameterEvaluation environment,Dictionary<string,int> ports,ImmutableArray<string> path,int depth)
        {
            if(depth>limits.MaxDepth)throw new BridgeFailure("expansion_depth",catalog.Scopes[scope].Range);
            if(!active.Add(scope))throw new BridgeFailure("recursive_reference",catalog.Scopes[scope].Range);
            if(++frames>limits.MaxInstances)throw new BridgeFailure("instance_budget",catalog.Scopes[scope].Range);
            var scopeFailure=selected?groundBinding!.CheckScope(scope):null;
            if(scopeFailure!=null)throw new BridgeFailure(scopeFailure.Code,scopeFailure.Range);
            var frame=frames-1;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Reject every instruction not supported or explicitly recorded
                // by the selection adapter; physical aliases/devices stay strict.
                foreach(var card in cards[scope])
                {
                    if(omittedStarts?.Contains(card.Range.Start)==true)continue;
                    if(selected&&card.GlobalNodes!=null)continue;
                    if(card.Kind=="endCard"&&scope!=0||card.Kind is not ("passive" or "source" or "subcall" or "paramCard" or "subcircuit" or "model" or "endCard" or "op")&&!(selected&&card.Kind is ("vcontrol" or "icontrol" or "switchCard" or "xspice" or "diode" or "behavior" or "bjt" or "mos")))
                        throw new BridgeFailure("unsupported_active_card",card.Range);
                }
                var frameIds=new Dictionary<string,(string Id,string Family)>(StringComparer.OrdinalIgnoreCase);
                foreach(var primitive in primitives[scope]){
                    var id="e"+(elements.Count+frameIds.Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if(!frameIds.TryAdd(primitive.Name,(id,primitive.Family)))throw new BridgeFailure("duplicate_element",primitive.Range);
                }
                foreach(var primitive in primitives[scope])
                {
                    if(selected&&primitive.RequiresExcitationProfile&&primitive.Excitation==null)throw new BridgeFailure("unsupported_source_profile",primitive.Range);
                    if(primitive.Family is not ("R" or "V" or "I") && !(selected && primitive.Family is ("C" or "L" or "E" or "G" or "F" or "H")))throw new BridgeFailure("unsupported_active_card",primitive.Range);
                    if(!names.Add(primitive.Name))throw new BridgeFailure("duplicate_element",primitive.Range);
                    double value;double? acOhms=null;var dcOpen=false;double magnitude=0,phase=0;SourceWaveformSyntax? waveformSyntax=null;ImmutableArray<double> waveformValues=[];
                    if(selected&&primitive.Family is ("V" or "I")&&primitive.Excitation is {} excitation)
                    {
                        double initial=0;
                        if(excitation.Waveform is {} wave){
                            waveformSyntax=wave;waveformValues=wave.Values.Select(Evaluate).ToImmutableArray();
                            if(!SourceWaveformPreparation.TryInitial(wave.Kind,waveformValues,out initial))throw new BridgeFailure("unsupported_source_waveform",primitive.Range);
                        }
                        value=excitation.Dc==null?initial:Evaluate(excitation.Dc);
                        // Validate explicitly supplied AC fields even though this
                        // selected operating point does not use their excitation.
                        if(excitation.AcMagnitude!=null){magnitude=Evaluate(excitation.AcMagnitude);explicitAcSources++;}
                        if(excitation.AcPhase!=null)phase=Evaluate(excitation.AcPhase);
                    }

                    else if(selected&&primitive.Family=="R")
                    {
                        var preparedResistor=requested?ResistorModelLowering.PrepareRequested(catalog,primitive,environment):ResistorModelLowering.Prepare(catalog,primitive,environment);
                        if(!preparedResistor.Success)throw new BridgeFailure(preparedResistor.Diagnostics[0].Code,preparedResistor.Diagnostics[0].Range);
                        value=preparedResistor.Resistor!.Ohms;acOhms=preparedResistor.Resistor.AcOhms;dcOpen=preparedResistor.Resistor.DcOpen;
                    }
                    else
                    {
                        if(!primitive.Supported||primitive.Value==null)throw new BridgeFailure("unsupported_primitive_header",primitive.Range);
                        value=Evaluate(primitive.Value);
                    }
                    if(primitive.Family=="R"&&value<=0)throw new BridgeFailure("invalid_resistance",primitive.Range);
                    if(primitive.Family=="C"&&value<=0)throw new BridgeFailure("invalid_capacitance",primitive.Range);
                    if(primitive.Family=="L"&&value<=0)throw new BridgeFailure("invalid_inductance",primitive.Range);
                    var p=Node(primitive.P.Text);var n=Node(primitive.N.Text);
                    if(p==n)throw new BridgeFailure("shorted_primitive",primitive.Range);
                    if(elements.Count>=limits.MaxElements)throw new BridgeFailure("element_budget",primitive.Range);
                    // Compact wire identifiers are collision-free and stay within
                    // Rust's byte limits; original spellings remain in provenance.
                    var id="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if(acOhms is {} acValue)acResistances.Add(id,acValue);
                    if(primitive.Family is "F" or "H")
                    {
                        if(primitive.CurrentReference is not {} reference||!frameIds.TryGetValue(reference.Text,out var controller))throw new BridgeFailure("missing_current_control",primitive.Range);
                        // Native ngspice admits voltage-defined V/E/H branches;
                        // direct inductor control is not this qualified profile.
                        if(controller.Family is not ("V" or "E" or "H"))throw new BridgeFailure("current_control_not_voltage_branch",reference.Range);
                        elements.Add(primitive.Family=="F"?new Cccs(id,p,n,controller.Id,value):new Ccvs(id,p,n,controller.Id,value));
                    }
                    else if(primitive.Family is "E" or "G")
                    {
                        if(primitive.Control==null)throw new BridgeFailure("unsupported_control_profile",primitive.Range);
                        var cp=Node(primitive.Control.P.Text);var cn=Node(primitive.Control.N.Text);
                        elements.Add(Controlled(id,p,n,cp,cn,value,primitive.Family=="E"));
                    }
                    else if(dcOpen)elements.Add(new Vccs(id,p,n,p,n,0)); // Exact open with stable terminals and wire ID.
                    else elements.Add(primitive.Family switch{"R"=>new Resistor(id,p,n,value),"C"=>new Capacitor(id,p,n,value),"L"=>new Inductor(id,p,n,value),"V"=>new Voltage(id,p,n,value),_=>new Current(id,p,n,value)});
                    if(selected&&primitive.Family is ("V" or "I"))
                    {
                        // Degree-to-radian multiplication avoids an overflowing
                        // intermediate phase*pi for otherwise finite angles.
                        var radians=phase*(Math.PI/180.0);
                        acSources.Add(new(id,magnitude*Math.Cos(radians),magnitude*Math.Sin(radians)));
                    }
                    if(waveformSyntax!=null)sourceWaveforms.Add(new(id,waveformSyntax.Kind,waveformValues,primitive.Range));
                    origins.Add(new(id,primitive.Name,path,primitive.Range){NativeBranchName=primitive.Family is "V" or "L" or "E" or "H"?primitive.Name:null});
                }
                foreach(var instance in catalog.XspiceInstances.Where(i=>i.ScopeId==scope))
                {
                    if(!selected)throw new BridgeFailure("unsupported_active_card",instance.Range);
                    foreach(var port in instance.Ports.Where(p=>p.IsVector)){
                        var vectorFailure=groundBinding!.CheckVector(port,scope);
                        if(vectorFailure!=null)throw new BridgeFailure(vectorFailure.Code,vectorFailure.Range);
                    }
                    if(!names.Add(instance.Name))throw new BridgeFailure("duplicate_element",instance.Range);
                    var modelLookup=catalog.Lookup(DeclarationKind.Model,instance.Model.Text,scope);
                    if(modelLookup is {Status:NameLookupStatus.Found,Provisional:false}&&modelLookup.Candidates.Single().ModelFamily?.Equals("summer",StringComparison.OrdinalIgnoreCase)==true)
                    {
                        var result=XspiceSummerLowering.Prepare(catalog,instance,environment);
                        if(!result.Success)throw new BridgeFailure(result.Diagnostics[0].Code,result.Diagnostics[0].Range);
                        var sum=result.Summer!;var output=Node(sum.OutputP.Text);var ground=Node(sum.OutputN.Text);
                        if(output==ground)throw new BridgeFailure("shorted_xspice_output",sum.Range);
                        if(elements.Count+sum.Inputs.Length+1>limits.MaxElements)throw new BridgeFailure("element_budget",sum.Range);
                        var previous=output;var index=0;
                        foreach(var input in sum.Inputs)
                        {
                            var controlP=Node(input.P.Text);var controlN=Node(input.N.Text);
                            if(nodes.Count>=limits.MaxNodes)throw new BridgeFailure("node_budget",sum.Range);
                            var next=nodes.Count;nodes.Add("n"+next.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            var summerId="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            elements.Add(Controlled(summerId,previous,next,controlP,controlN,input.Gain,true));
                            origins.Add(new(summerId,index==0?sum.Name:sum.Name+"$term"+index,path,sum.Range){NativeBranchName=index==0?sum.Name:null});
                            previous=next;index++;
                        }
                        var offsetId="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        elements.Add(new Voltage(offsetId,previous,ground,sum.Offset));
                        origins.Add(new(offsetId,sum.Name+"$offset",path,sum.Range));acSources.Add(new(offsetId,0,0));
                        continue;
                    }
                    var lowered=XspiceGainLowering.Prepare(catalog,instance,environment);
                    if(!lowered.Success)throw new BridgeFailure(lowered.Diagnostics[0].Code,lowered.Diagnostics[0].Range);
                    var gain=lowered.Gain!;var p=Node(gain.OutputP.Text);var n=Node(gain.OutputN.Text);var cp=Node(gain.InputP.Text);var cn=Node(gain.InputN.Text);
                    if(p==n)throw new BridgeFailure("shorted_xspice_output",gain.Range);
                    if(elements.Count+(gain.Offset==0?1:2)>limits.MaxElements)throw new BridgeFailure("element_budget",gain.Range);
                    var id="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);var mid=n;
                    if(gain.Offset!=0){if(nodes.Count>=limits.MaxNodes)throw new BridgeFailure("node_budget",gain.Range);mid=nodes.Count;nodes.Add("n"+mid.ToString(System.Globalization.CultureInfo.InvariantCulture));}
                    elements.Add(Controlled(id,p,mid,cp,cn,gain.Gain,true));origins.Add(new(id,gain.Name,path,gain.Range){NativeBranchName=gain.Name});
                    if(gain.Offset!=0){
                        var offsetId="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        elements.Add(new Voltage(offsetId,mid,n,gain.Offset));origins.Add(new(offsetId,gain.Name+"$offset",path,gain.Range));
                        acSources.Add(new(offsetId,0,0));
                    }
                }
                foreach(var instance in catalog.BehavioralSources.Where(i=>i.ScopeId==scope))
                {
                    if(!selected)throw new BridgeFailure("unsupported_active_card",instance.Range);
                    if(!names.Add(instance.Name))throw new BridgeFailure("duplicate_element",instance.Range);
                    var lowered=BehavioralSourceLowering.Prepare(catalog,instance,environment);
                    if(!lowered.Success)throw new BridgeFailure(lowered.Diagnostics[0].Code,lowered.Diagnostics[0].Range);
                    var source=lowered.Source!;var p=Node(source.P.Text);var n=Node(source.N.Text);
                    if(p==n)throw new BridgeFailure("shorted_behavior_output",source.Range);
                    // Ground substitution can make a named voltage term
                    // identically zero. Remove it before allocating auxiliaries
                    // or checking element budgets; retain the offset source.
                    var terms=source.Terms.Select(t=>(Control:Node(t.Node.Text),t.Coefficient)).Where(t=>t.Control!=0).ToArray();
                    if(elements.Count+terms.Length+1>limits.MaxElements)throw new BridgeFailure("element_budget",source.Range);
                    var previous=p;var termIndex=0;
                    foreach(var term in terms)
                    {
                        var control=term.Control;var id="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var next=n;
                        if(source.Quantity==BehavioralQuantity.Voltage)
                        {
                            if(nodes.Count>=limits.MaxNodes)throw new BridgeFailure("node_budget",source.Range);
                            next=nodes.Count;nodes.Add("n"+next.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            elements.Add(new Vcvs(id,previous,next,control,0,term.Coefficient));previous=next;
                        }
                        else elements.Add(new Vccs(id,p,n,control,0,term.Coefficient));
                        var original=termIndex++==0;
                        origins.Add(new(id,original?source.Name:source.Name+"$term"+termIndex,path,source.Range){NativeBranchName=original&&source.Quantity==BehavioralQuantity.Voltage?source.Name:null});
                    }
                    // A constant contributes DC bias only. Explicit zero AC
                    // prevents the independent-source default from exciting it.
                    var offsetId="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    elements.Add(source.Quantity==BehavioralQuantity.Voltage?new Voltage(offsetId,previous,n,source.Offset):new Current(offsetId,p,n,source.Offset));
                    origins.Add(new(offsetId,termIndex==0?source.Name:source.Name+"$offset",path,source.Range){NativeBranchName=termIndex==0&&source.Quantity==BehavioralQuantity.Voltage?source.Name:null});acSources.Add(new(offsetId,0,0));
                }
                double Evaluate(SourceSyntax valueSyntax)
                {
                    var scalar=ParameterEvaluator.EvaluateValue(catalog,environment,valueSyntax);
                    if(scalar.Value is not {} result)throw new BridgeFailure(scalar.Diagnostics[0].Code,scalar.Diagnostics[0].Range);
                    return result;
                }
                foreach(var call in bound[scope])
                {
                    if(!names.Add(call.Header.Name))throw new BridgeFailure("duplicate_element",call.Header.Range);
                    if(selected&&call.Header.Family=="M")
                    {
                        if(!Mos1Preparation.TryPrepare(catalog,environment,call,out var mos,out var error))throw new BridgeFailure(error!.Code,error.Range);
                        var d=Node(call.Ports[0].Actual.Text);var g=Node(call.Ports[1].Actual.Text);var source=Node(call.Ports[2].Actual.Text);var b=Node(call.Ports[3].Actual.Text);
                        var id="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        mos!.Append(id,d,g,source,b,PrivateNode,element=>{if(elements.Count>=limits.MaxElements)throw new BridgeFailure("element_budget",call.Header.Range);elements.Add(element);});
                        origins.Add(new(id,call.Header.Name,path,call.Header.Range));continue;
                    }
                    if(selected&&call.Header.Family=="Q")
                    {
                        if(!BjtPreparation.TryPrepare(catalog,environment,call,out var bjt,out var error))throw new BridgeFailure(error!.Code,error.Range);
                        var c=Node(call.Ports[0].Actual.Text);var b=Node(call.Ports[1].Actual.Text);var e=Node(call.Ports[2].Actual.Text);
                        var id="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        bjt!.Append(id,c,b,e,PrivateNode,element=>{if(elements.Count>=limits.MaxElements)throw new BridgeFailure("element_budget",call.Header.Range);elements.Add(element);});
                        origins.Add(new(id,call.Header.Name,path,call.Header.Range));continue;
                    }
                    if(selected&&call.Header.Family=="D")
                    {
                        if(!SemiconductorPreparation.TryDiode(catalog,environment,call,out var diode,out var error))
                            throw new BridgeFailure(error!.Code,error.Range);
                        var p=Node(call.Ports[0].Actual.Text);var n=Node(call.Ports[1].Actual.Text);
                        if(p==n)throw new BridgeFailure("shorted_primitive",call.Header.Range);
                        if(elements.Count>=limits.MaxElements)throw new BridgeFailure("element_budget",call.Header.Range);
                        var id="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        elements.Add(new SpiceDiode(id,p,n,diode!));origins.Add(new(id,call.Header.Name,path,call.Header.Range));
                        continue;
                    }
                    if(selected&&call.Header.Family=="S")
                    {
                        if(call.Status!=BindingStatus.Bound)throw new BridgeFailure(call.Code,call.Header.Range);
                        var model=call.Target!;
                        if(model.ModelFamily!="vswitch"||model.HasOpaqueModelTerms||!call.Header.Parameters.IsEmpty)throw new BridgeFailure("unsupported_switch_profile",call.Header.Range);
                        var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
                        foreach(var parameter in model.Parameters)
                        {
                            if(parameter.Name is not {} name||!new[]{"ron","roff","von","voff"}.Contains(name,StringComparer.OrdinalIgnoreCase)||values.ContainsKey(name))throw new BridgeFailure("unsupported_switch_parameter",parameter.Range);
                            var value=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value,model.ScopeId);
                            if(value.Value is not {} scalar)throw new BridgeFailure(value.Diagnostics[0].Code,value.Diagnostics[0].Range);
                            values.Add(name,scalar);
                        }
                        if(values.Count!=4)throw new BridgeFailure("missing_switch_parameter",model.Range);
                        var ron=values["ron"];var roff=values["roff"];var von=values["von"];var voff=values["voff"];var span=von-voff;
                        if(ron<1e-3||roff<ron||roff>1e12||!double.IsFinite(span)||Math.Abs(span)<1e-12)throw new BridgeFailure("invalid_switch_parameter",model.Range);
                        var p=Node(call.Ports[0].Actual.Text);var n=Node(call.Ports[1].Actual.Text);var cp=Node(call.Ports[2].Actual.Text);var cn=Node(call.Ports[3].Actual.Text);
                        if(p==n)throw new BridgeFailure("shorted_primitive",call.Header.Range);
                        if(elements.Count>=limits.MaxElements)throw new BridgeFailure("element_budget",call.Header.Range);
                        var id="e"+elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        elements.Add(new PspiceSwitch(id,p,n,cp,cn,ron,roff,von,voff));origins.Add(new(id,call.Header.Name,path,call.Header.Range));
                        continue;
                    }
                    if(call.Header.Family!="X")throw new BridgeFailure("unsupported_active_card",call.Header.Range);
                    if(call.Status!=BindingStatus.Bound)throw new BridgeFailure(call.Code,call.Header.Range);
                    var childPorts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                    foreach(var port in call.Ports)
                    {
                        if(port.Formal!.Text=="0"&&!selected)throw new BridgeFailure("reserved_formal_ground",port.Formal.Range);
                        // Native automatic normalization precedes elaboration.
                        // A grounded formal ignores its actual, which must not
                        // allocate an unconnected matrix node or observable.
                        if(selected&&groundBinding!.IsGround(port.Formal.Text,call.Target!.BodyScopeId!.Value)){
                            childPorts.Add(port.Formal.Text,0);continue;
                        }
                        // A global formal is replaced before ordinary port
                        // substitution. Its unused actual must not create a
                        // floating matrix unknown or a fictitious observable.
                        childPorts.Add(port.Formal.Text,Node(selected&&globalNames.Contains(port.Formal.Text)?port.Formal.Text:port.Actual.Text));
                    }
                    var child=ParameterEvaluator.ForInstance(catalog,call,environment);
                    if(!child.Success)throw new BridgeFailure(child.Diagnostics[0].Code,child.Diagnostics[0].Range);
                    Expand(call.Target!.BodyScopeId!.Value,child,childPorts,path.Add(call.Header.Name),depth+1);
                }
            }
            finally{active.Remove(scope);}
            int Node(string name)
            {
                if(name=="0"||selected&&groundBinding!.IsGround(name,scope))return 0;
                if(selected&&globalNames.Contains(name)){
                    if(globalNodes.TryGetValue(name,out var global))return global;
                    if(nodes.Count>=limits.MaxNodes)throw new BridgeFailure("node_budget",catalog.Scopes[scope].Range);
                    global=nodes.Count;var globalWire="n"+global.ToString(System.Globalization.CultureInfo.InvariantCulture);nodes.Add(globalWire);globalNodes.Add(name,global);roots[name]=globalWire;return global;
                }
                if(ports.TryGetValue(name,out var index))return index;
                if(!localNodes.TryGetValue(frame,out var table)){table=new(StringComparer.OrdinalIgnoreCase);localNodes.Add(frame,table);}
                if(table.TryGetValue(name,out index))return index;
                if(nodes.Count>=limits.MaxNodes)throw new BridgeFailure("node_budget",catalog.Scopes[scope].Range);
                index=nodes.Count;var wire="n"+index.ToString(System.Globalization.CultureInfo.InvariantCulture);nodes.Add(wire);table.Add(name,index);
                if(scope==0)roots[name]=wire;
                else {
                    var nativeName=string.Join('.',path)+"."+name;
                    if(internalNodes.TryGetValue(nativeName,out var existing)&&existing!=wire)
                        throw new BridgeFailure("ambiguous_native_internal_node",catalog.Scopes[scope].Range);
                    internalNodes[nativeName]=wire;
                }
                return index;
            }
            int PrivateNode()
            {
                if(nodes.Count>=limits.MaxNodes)throw new BridgeFailure("node_budget",catalog.Scopes[scope].Range);
                var index=nodes.Count;nodes.Add("n"+index.ToString(System.Globalization.CultureInfo.InvariantCulture));return index;
            }
        }
        Element Controlled(string id,int p,int n,int cp,int cn,double coefficient,bool voltage)
        {
            if(cp!=cn)return voltage?new Vcvs(id,p,n,cp,cn,coefficient):new Vccs(id,p,n,cp,cn,coefficient);
            // The exact difference of identical controls is zero. Preserve a
            // voltage-output branch/current identity while avoiding invalid
            // controls, and explicitly suppress independent-source AC defaults.
            acSources.Add(new(id,0,0));
            return voltage?new Voltage(id,p,n,0):new Current(id,p,n,0);
        }
    }
    private sealed class BridgeFailure(string code,SourceRange? range):Exception(code)
    {internal string Code{get;}=code;internal SourceRange? Range{get;}=range;}
}
