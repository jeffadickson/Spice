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
            if(!SupportsDiodeAssignment(name)||!names.Add(name))return false;
        }
        return true;
    }
    private static bool SupportsDiodeAssignment([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? name) =>
        name is "IS" or "N" or "CJO" or "VJ" or "M" or "FC" or "TT" or "TNOM" or "RS";

    internal static bool TryDiode(DeclarationCatalog catalog,ParameterEvaluation environment,
        BoundInstance call,out SpiceDiodeModel? result,out double seriesOhms,out BridgeDiagnostic? diagnostic,double celsius=27)
    {
        result=null;seriesOhms=0;diagnostic=null;
        if(call.Status!=BindingStatus.Bound||call.Target is not {} model)
        {diagnostic=new(call.Code,call.Header.Range);return false;}
        if(model.ModelFamily!="d"||model.HasOpaqueModelTerms||!call.Header.Parameters.IsEmpty)
        {diagnostic=new("unsupported_diode_profile",call.Header.Range);return false;}
        var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in model.Parameters)
        {
            var name=parameter.Name?.ToUpperInvariant();
            if(name=="CJ0")name="CJO";
            if(!SupportsDiodeAssignment(name)||values.ContainsKey(name))
            {diagnostic=new("unsupported_diode_parameter",parameter.Range);return false;}
            var evaluated=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value,model.ScopeId);
            if(evaluated.Value is not {} scalar)
            {diagnostic=new(evaluated.Diagnostics[0].Code,evaluated.Diagnostics[0].Range);return false;}
            values.Add(name,scalar);
        }
        double Value(string name,double fallback)=>values.GetValueOrDefault(name,fallback);
        var isat=Value("IS",1e-14);var n=Value("N",1);var c=Value("CJO",0);var vj=Value("VJ",1);
        var m=Value("M",.5);var fc=Value("FC",.5);var tt=Value("TT",0);var rs=Value("RS",0);
        // This first profile evaluates at SPICE's default 27 C. Temperature
        // transformations and non-default TNOM remain an explicit later profile.
        if(rs<0||rs>0&&!double.IsFinite(1/rs)||isat<=0||n<=0||c<0||vj<=0||m<0||m>=1||fc<0||fc>=1||tt<0||Value("TNOM",27)!=27)
        {diagnostic=new("invalid_or_unqualified_diode_parameter",model.Range);return false;}
        const double kOverQ=1.38064852e-23/1.6021766208e-19;
        var kelvin=celsius+273.15;
        var vt=n*kOverQ*kelvin;
        if(celsius!=27)
        {
            if(c!=0||tt!=0||rs!=0)
            {diagnostic=new("unsupported_diode_temperature_profile",model.Range);return false;}
            // Default TLEV=0, EG=1.11 eV and XTI=3, relative to TNOM=27 C.
            // Evaluate the card's IS at the requested temperature before scaling.
            var ratio=kelvin/300.15;
            isat*=Math.Exp((ratio-1)*1.11/vt+(3/n)*Math.Log(ratio));
            if(!double.IsFinite(isat)||isat<=0)
            {diagnostic=new("invalid_diode_temperature_scaling",model.Range);return false;}
        }
        if(!double.IsFinite(vt)||vt<=0)
        {diagnostic=new("invalid_diode_thermal_voltage",model.Range);return false;}
        result=new(isat,vt,c,vj,m,fc,tt);seriesOhms=rs;return true;
    }
}
