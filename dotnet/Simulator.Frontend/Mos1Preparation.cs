using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Intrinsic channel with explicit body and overlap companions.
/// A drain sensor measures the complete drain current, including displacement.</summary>
internal sealed record PreparedMos1(Mos1Model Channel,SpiceDiodeModel DrainBody,SpiceDiodeModel SourceBody,
    double GateSourceFarads,double GateDrainFarads,double GateBulkFarads,double DrainOhms,double SourceOhms)
{
    internal void Append(string id,int drain,int gate,int source,int bulk,Func<int> privateNode,Action<Element> add)
    {
        // Keep the sensor on the external drain. Channel, junctions and
        // overlap capacitances use the intrinsic terminals after the drops.
        var d=privateNode();add(new Voltage(id,drain,d,0));
        if(DrainOhms>0){var intrinsic=privateNode();add(new Resistor(id+"_rd",d,intrinsic,DrainOhms));d=intrinsic;}
        if(SourceOhms>0){var intrinsic=privateNode();add(new Resistor(id+"_rs",source,intrinsic,SourceOhms));source=intrinsic;}
        if(d!=source)add(new Mos1Channel(id+"_channel",d,source,gate,bulk,Channel));
        // Junction polarity follows the substrate type, independently of channel mode.
        if(d!=bulk)add(new SpiceDiode(id+"_bd",Channel.PChannel?d:bulk,Channel.PChannel?bulk:d,DrainBody));
        if(source!=bulk)add(new SpiceDiode(id+"_bs",Channel.PChannel?source:bulk,Channel.PChannel?bulk:source,SourceBody));
        // Native MOS1 retains its default 1-pS body-junction floor even after
        // global continuation shunts are removed. Explicit branches keep this
        // compatibility convention visible to current sensors and audits.
        if(d!=bulk)add(new Resistor(id+"_bd_gmin",Channel.PChannel?d:bulk,Channel.PChannel?bulk:d,1e12));
        if(source!=bulk)add(new Resistor(id+"_bs_gmin",Channel.PChannel?source:bulk,Channel.PChannel?bulk:source,1e12));
        if(GateSourceFarads>0&&gate!=source)add(new Capacitor(id+"_cgs",gate,source,GateSourceFarads));
        if(GateDrainFarads>0&&gate!=d)add(new Capacitor(id+"_cgd",gate,d,GateDrainFarads));
        if(GateBulkFarads>0&&gate!=bulk)add(new Capacitor(id+"_cgb",gate,bulk,GateBulkFarads));
    }
}
/// <summary>Bounded LEVEL=1 lowering with optional oxide-derived Meyer displacement.</summary>
internal static class Mos1Preparation
{
    private static readonly HashSet<string> Allowed=new(StringComparer.OrdinalIgnoreCase)
      {"LEVEL","VTO","KP","GAMMA","PHI","LAMBDA","IS","CBD","CBS","PB","MJ","FC","CGSO","CGDO","CGBO","TNOM","TOX","L","W","RD","RS"};
    // Recognize only explicit Level 8/49 selectors at this boundary. An
    // identified card still refuses until an exact native kernel is present.
    internal static string? Bsim3Diagnostic(BoundInstance call)
    {
        if(call.Target is not { ModelFamily: "nmos" or "pmos" } model)return null;
        var levels=model.Parameters.Where(p=>string.Equals(p.Name,"level",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(levels.Length!=1||!double.TryParse(levels[0].Value.Text,System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,out var level)||level is not (8 or 49))return null;
        var versions=model.Parameters.Where(p=>string.Equals(p.Name,"version",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(versions.Length!=1||model.HasOpaqueModelTerms)return "invalid_bsim3_model_selector";
        return versions[0].Value.Text.Trim() == "3.3.0" ? "bsim3_kernel_unavailable" : "unsupported_bsim3_version";
    }
    internal static bool AssignmentsAreSupported(BoundInstance call)
    {
        if(call.Status!=BindingStatus.Bound||call.Target is not {} model||model.ModelFamily is not ("nmos" or "pmos")||model.HasOpaqueModelTerms||call.Ports.Length!=4)return false;
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(!model.Parameters.All(p=>p.Name!=null&&Allowed.Contains(p.Name)&&names.Add(p.Name)))return false;
        names.Clear();
        return call.Header.Parameters.All(p=>p.Name is {} name&&(name.Equals("L",StringComparison.OrdinalIgnoreCase)||name.Equals("W",StringComparison.OrdinalIgnoreCase))&&names.Add(name));
    }
    internal static bool TryPrepare(DeclarationCatalog catalog,ParameterEvaluation environment,BoundInstance call,out PreparedMos1? profile,out BridgeDiagnostic? diagnostic)
    {
        profile=null;diagnostic=null;
        if(!AssignmentsAreSupported(call)){diagnostic=new(Bsim3Diagnostic(call)??"unsupported_mos1_profile",call.Header.Range);return false;}
        var model=call.Target!;var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        // Dimensions are resolved separately, with instance geometry overriding
        // model geometry. Each expression is evaluated in its declaring scope.
        foreach(var (p,scope) in model.Parameters.Select(p=>(p,model.ScopeId))
            .Concat(call.Header.Parameters.Select(p=>(p,call.Header.ScopeId))))
        {
            var v=ParameterEvaluator.EvaluateValue(catalog,environment,p.Value,scope);
            if(v.Value is not {} scalar){diagnostic=new(v.Diagnostics[0].Code,v.Diagnostics[0].Range);return false;}
            values[p.Name!]=scalar;
        }
        var pmos=model.ModelFamily=="pmos";
        // ngspice's circuit defaults are 100 micrometres for each dimension.
        var l=V("L",1e-4);var w=V("W",1e-4);var tox=V("TOX",0);
        // Match ngspice's oxide permittivity and its default surface mobility (600 cm²/V/s).
        const double oxidePermittivity=3.9*8.854214871e-12;
        var cox=tox>0?oxidePermittivity/tox:0;
        var oxideFarads=cox*w*l;var kp=V("KP",tox>0?.06*cox:2e-5);var gamma=V("GAMMA",0);var phi=V("PHI",.6);var lambda=V("LAMBDA",0);
        var isat=V("IS",1e-14);var cbd=V("CBD",0);var cbs=V("CBS",0);var pb=V("PB",.8);var mj=V("MJ",.5);var fc=V("FC",.5);
        var cgso=V("CGSO",0);var cgdo=V("CGDO",0);var cgbo=V("CGBO",0);
        var rd=V("RD",0);var rs=V("RS",0);
        var beta=kp*w/l;var cgs=cgso*w;var cgd=cgdo*w;var cgb=cgbo*l;
        if(rd<0||rs<0||rd>0&&!double.IsFinite(1/rd)||rs>0&&!double.IsFinite(1/rs)||l<=0||w<=0||kp<=0||gamma<0||phi<=0||lambda<0||isat<=0||cbd<0||cbs<0||pb<=0||mj<0||mj>=1||fc<0||fc>=1||cgso<0||cgdo<0||cgbo<0
          ||V("LEVEL",1)!=1||V("TNOM",27)!=27||tox<0||!double.IsFinite(oxideFarads)||tox>0&&oxideFarads<=0||!double.IsFinite(beta)||beta<=0||!double.IsFinite(cgs)||!double.IsFinite(cgd)||!double.IsFinite(cgb)
          ||cgso>0&&cgs==0||cgdo>0&&cgd==0||cgbo>0&&cgb==0)
        {diagnostic=new("invalid_or_unqualified_mos1_parameter",model.Range);return false;}
        const double vt=1.38064852e-23/1.6021766208e-19*300.15;
        profile=new(new(pmos,V("VTO",0),beta,gamma,phi,lambda,oxideFarads),new(isat,vt,cbd,pb,mj,fc,0),new(isat,vt,cbs,pb,mj,fc,0),cgs,cgd,cgb,rd,rs);return true;
        double V(string name,double fallback)=>values.TryGetValue(name,out var value)?value:fallback;
    }
}
