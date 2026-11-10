using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Qualified Ebers–Moll subset of the default BJT parameter model.
/// The numerical engine still evaluates ordinary native junctions and sources.</summary>
internal sealed record PreparedBjt(bool Pnp,double BetaForward,double BetaReverse,
    SpiceDiodeModel ForwardStorage,SpiceDiodeModel ReverseStorage,SpiceDiodeModel SubstrateStorage,double BaseOhms,double CollectorOhms,double EmitterOhms)
{
    internal void Append(string id,int collector,int baseNode,int emitter,int? substrate,Func<int> privateNode,Action<Element> add)
    {
        var c=privateNode();var be=privateNode();var bc=privateNode();
        // The outer sensor measures total collector current. The BE/BC sensors
        // below deliberately exclude the parallel charge-only companions.
        add(new Voltage(id,collector,c,0));
        // Junctions, substrate shunts and charge use the intrinsic terminals.
        c=Series(c,CollectorOhms,"_rc");
        baseNode=Series(baseNode,BaseOhms,"_rb");
        emitter=Series(emitter,EmitterOhms,"_re");
        add(new Voltage(id+"_be_sense",Pnp?emitter:baseNode,be,0));
        add(new Diode(id+"_be",be,Pnp?baseNode:emitter,ForwardStorage.SaturationAmps/BetaForward,ForwardStorage.ThermalVolts));
        add(new Voltage(id+"_bc_sense",Pnp?c:baseNode,bc,0));
        add(new Diode(id+"_bc",bc,Pnp?baseNode:c,ReverseStorage.SaturationAmps/BetaReverse,ReverseStorage.ThermalVolts));
        add(new Cccs(id+"_forward",Pnp?emitter:c,Pnp?c:emitter,id+"_be_sense",BetaForward));
        add(new Cccs(id+"_reverse",Pnp?c:emitter,Pnp?emitter:c,id+"_bc_sense",BetaReverse));
        // Vertical NPN and lateral PNP both use V(substrate) minus V(reference).
        var substrateReference=Pnp?baseNode:c;
        var substrateNode=substrate??0; // Omitted substrate terminal is ground.
        if(substrateNode!=substrateReference)
        {
            if(substrate.HasValue)
                add(new Resistor(id+"_substrate",substrateNode,substrateReference,1e12));
            if(SubstrateStorage.JunctionFarads>0)
                add(new JunctionStorage(id+"_qsub",substrateNode,substrateReference,SubstrateStorage));
        }

        if(ForwardStorage.JunctionFarads>0||ForwardStorage.TransitSeconds>0)
            add(new JunctionStorage(id+"_qbe",Pnp?emitter:baseNode,Pnp?baseNode:emitter,ForwardStorage));
        if(ReverseStorage.JunctionFarads>0||ReverseStorage.TransitSeconds>0)
            add(new JunctionStorage(id+"_qbc",Pnp?c:baseNode,Pnp?baseNode:c,ReverseStorage));

        int Series(int external,double ohms,string suffix)
        {
            if(ohms==0)return external;
            var intrinsic=privateNode();
            add(new Resistor(id+suffix,external,intrinsic,ohms));
            return intrinsic;
        }
    }
}

/// <summary>Strict model-card policy; absent GP couplings reduce exactly to
/// Ebers–Moll. Unsupported non-default physics is never silently discarded.</summary>
internal static class BjtPreparation
{
    private static readonly HashSet<string> Allowed=new(StringComparer.OrdinalIgnoreCase)
        {"IS","BF","BR","NF","NR","CJE","VJE","MJE","CJC","VJC","MJC","FC","TF","TR","TNOM","LEVEL","RB","RC","RE","CJS","VJS","MJS"};
    internal static bool AssignmentsAreSupported(BoundInstance call)
    {
        if(call.Status!=BindingStatus.Bound||call.Target is not {} model||model.ModelFamily is not ("npn" or "pnp")
            ||model.HasOpaqueModelTerms||!call.Header.Parameters.IsEmpty||call.Ports.Length is not (3 or 4))return false;
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return model.Parameters.All(p=>p.Name!=null&&Allowed.Contains(CanonicalName(p.Name))&&names.Add(CanonicalName(p.Name)));
    }
    internal static bool TryPrepare(DeclarationCatalog catalog,ParameterEvaluation environment,BoundInstance call,
        out PreparedBjt? profile,out BridgeDiagnostic? diagnostic)
    {
        profile=null;diagnostic=null;
        if(!AssignmentsAreSupported(call)){diagnostic=new("unsupported_bjt_profile",call.Header.Range);return false;}
        var model=call.Target!;var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in model.Parameters)
        {
            var v=ParameterEvaluator.EvaluateValue(catalog,environment,p.Value,model.ScopeId);
            if(v.Value is not {} scalar){diagnostic=new(v.Diagnostics[0].Code,v.Diagnostics[0].Range);return false;}
            values.Add(CanonicalName(p.Name!),scalar);
        }
        var isat=Value(values,"IS",1e-16);var bf=Value(values,"BF",100);var br=Value(values,"BR",1);
        var nf=Value(values,"NF",1);var nr=Value(values,"NR",1);
        var cje=Value(values,"CJE",0);var cjc=Value(values,"CJC",0);
        var vje=Value(values,"VJE",.75);var vjc=Value(values,"VJC",.75);
        var mje=Value(values,"MJE",.33);var mjc=Value(values,"MJC",.33);var fc=Value(values,"FC",.5);
        var cjs=Value(values,"CJS",0);var vjs=Value(values,"VJS",.75);var mjs=Value(values,"MJS",0);
        var tf=Value(values,"TF",0);var tr=Value(values,"TR",0);
        var rb=Value(values,"RB",0);var rc=Value(values,"RC",0);var re=Value(values,"RE",0);
        if(rb<0||rc<0||re<0||rb>0&&!double.IsFinite(1/rb)||rc>0&&!double.IsFinite(1/rc)||re>0&&!double.IsFinite(1/re)||isat<=0||bf<=0||br<=0||nf<=0||nr<=0||cje<0||cjc<0||vje<=0||vjc<=0||mje<0||mje>=1||mjc<0||mjc>=1||fc<0||fc>=1||tf<0||tr<0
            ||cjs<0||vjs<=0||mjs<0||mjs>=1
            ||Value(values,"TNOM",27)!=27||Value(values,"LEVEL",1)!=1||!double.IsFinite(isat/bf)||!double.IsFinite(isat/br)||isat/bf<=0||isat/br<=0)
        {diagnostic=new("invalid_or_unqualified_bjt_parameter",model.Range);return false;}
        const double vt=1.38064852e-23/1.6021766208e-19*300.15;
        var vf=nf*vt;var vr=nr*vt;
        if(!double.IsFinite(vf)||!double.IsFinite(vr)||vf<=0||vr<=0)
        {diagnostic=new("invalid_bjt_thermal_voltage",model.Range);return false;}
        // Substrate depletion rolls into its forward continuation at zero bias.
        var substrateStorage=new SpiceDiodeModel(isat,vt,cjs,vjs,mjs,0,0);
        profile=new(model.ModelFamily=="pnp",bf,br,new(isat,vf,cje,vje,mje,fc,tf),new(isat,vr,cjc,vjc,mjc,fc,tr),substrateStorage,rb,rc,re);return true;
    }
    // Aliases represent the same parameter and must share duplicate detection.
    private static string CanonicalName(string name)=>name.ToUpperInvariant() switch
    {
        "CCS" or "CSUB"=>"CJS",
        "PS"=>"VJS",
        "MS"=>"MJS",
        var canonical=>canonical
    };

    private static double Value(IReadOnlyDictionary<string,double> values,string name,double fallback)=>values.TryGetValue(name,out var value)?value:fallback;
}
