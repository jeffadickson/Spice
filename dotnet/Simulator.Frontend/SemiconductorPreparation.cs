using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Strict binding policy for qualified semiconductor parameter subsets.
/// Unknown assignments refuse before any request is returned; parsing is not qualification.</summary>
internal static class SemiconductorPreparation
{
    internal static bool DiodeAssignmentsAreSupported(BoundInstance call)
    {
        if(call.Status!=BindingStatus.Bound||call.Target is not {} model||model.ModelFamily!="d"||model.HasOpaqueModelTerms||!call.Header.Parameters.IsEmpty)return false;
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in model.Parameters)
        {
            var name=parameter.Name?.ToUpperInvariant();if(name=="CJ0")name="CJO";
            if(name is not ("IS" or "N" or "CJO" or "VJ" or "M" or "FC" or "TT" or "TNOM")||!names.Add(name))return false;
        }
        return true;
    }
    internal static bool TryDiode(DeclarationCatalog catalog,ParameterEvaluation environment,
        BoundInstance call,out SpiceDiodeModel? result,out BridgeDiagnostic? diagnostic)
    {
        result=null;diagnostic=null;
        if(call.Status!=BindingStatus.Bound||call.Target is not {} model)
        {diagnostic=new(call.Code,call.Header.Range);return false;}
        if(model.ModelFamily!="d"||model.HasOpaqueModelTerms||!call.Header.Parameters.IsEmpty)
        {diagnostic=new("unsupported_diode_profile",call.Header.Range);return false;}
        var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in model.Parameters)
        {
            var name=parameter.Name?.ToUpperInvariant();
            if(name=="CJ0")name="CJO";
            if(name is not ("IS" or "N" or "CJO" or "VJ" or "M" or "FC" or "TT" or "TNOM")||values.ContainsKey(name))
            {diagnostic=new("unsupported_diode_parameter",parameter.Range);return false;}
            var evaluated=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value,model.ScopeId);
            if(evaluated.Value is not {} scalar)
            {diagnostic=new(evaluated.Diagnostics[0].Code,evaluated.Diagnostics[0].Range);return false;}
            values.Add(name,scalar);
        }
        double Value(string name,double fallback)=>values.GetValueOrDefault(name,fallback);
        var isat=Value("IS",1e-14);var n=Value("N",1);var c=Value("CJO",0);var vj=Value("VJ",1);
        var m=Value("M",.5);var fc=Value("FC",.5);var tt=Value("TT",0);
        // This first profile evaluates at SPICE's default 27 C. Temperature
        // transformations and non-default TNOM remain an explicit later profile.
        if(isat<=0||n<=0||c<0||vj<=0||m<0||m>=1||fc<0||fc>=1||tt<0||Value("TNOM",27)!=27)
        {diagnostic=new("invalid_or_unqualified_diode_parameter",model.Range);return false;}
        const double kOverQ=1.38064852e-23/1.6021766208e-19;
        var vt=n*kOverQ*300.15;
        if(!double.IsFinite(vt)||vt<=0)
        {diagnostic=new("invalid_diode_thermal_voltage",model.Range);return false;}
        result=new(isat,vt,c,vj,m,fc,tt);return true;
    }
}
