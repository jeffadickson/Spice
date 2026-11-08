using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Qualified scalar resistance provenance; model preparation is distinct from original scalar evaluation.</summary>
public enum ResistorProfile
{
    /// <summary>Existing numeric or parameter-valued primitive, before model lookup.</summary>
    Scalar,
    /// <summary>Constant positive R model with no instance assignments.</summary>
    ConstantModel,
    /// <summary>Positive sheet resistance with explicit effective geometry.</summary>
    SheetModel
}
/// <summary>Effective resistance in ohms, with original terminals, declaration provenance and an explicit DC-open state.</summary>
public sealed record PreparedResistor(string Name,SourceSyntax P,SourceSyntax N,double Ohms,ResistorProfile Profile,SourceRange Range)
{
    /// <summary>Effective AC-only resistance in ohms when explicitly supplied.</summary>
    public double? AcOhms {get;init;}
    /// <summary>True when exact zero effective width yields zero DC conductance and Ohms is positive infinity.</summary>
    public bool DcOpen {get;init;}
}
/// <summary>Atomic resistor admission; a refused profile publishes no approximate or partial value.</summary>
public sealed record ResistorPreparation(PreparedResistor? Resistor,ImmutableArray<BridgeDiagnostic> Diagnostics)
{
    /// <summary>True only for a fully evaluated resistance.</summary>
    public bool Success=>Resistor!=null&&Diagnostics.IsEmpty;
}
/// <summary>Bounded scalar, constant-model and sheet-model preparation using generated syntax and retained lexical environments.</summary>
public static class ResistorModelLowering
{
    /// <summary>Preserves scalar precedence; evaluates model parameters in declaration scope and geometry in the actual instance environment. Dimensions are SI metres, sheet resistance is ohms per square.</summary>
    public static ResistorPreparation Prepare(DeclarationCatalog catalog,PrimitiveSyntax primitive,ParameterEvaluation environment)
        =>PrepareCore(catalog,primitive,environment,requested:false);

    internal static ResistorPreparation PrepareRequested(DeclarationCatalog catalog,PrimitiveSyntax primitive,ParameterEvaluation environment)
        =>PrepareCore(catalog,primitive,environment,requested:true);

    private static ResistorPreparation PrepareCore(DeclarationCatalog catalog,PrimitiveSyntax primitive,ParameterEvaluation environment,bool requested)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(primitive);ArgumentNullException.ThrowIfNull(environment);
        ResistorPreparation Fail(string code,SourceRange? range=null)=>new(null,[new(code,range??primitive.Range)]);
        double multiplicity=1,scale=1;
        double? ac=null;
        ResistorPreparation Finish(double ohms,ResistorProfile profile,bool dcOpen=false)
        {
            // Native conductance is M / (R * SCALE), separately for DC and AC.
            var effective=ohms*scale/multiplicity;
            var effectiveAc=ac*scale/multiplicity;
            if(!dcOpen&&(!double.IsFinite(effective)||effective<=0))return Fail("invalid_resistance");
            if(effectiveAc is {} a&&(!double.IsFinite(a)||a<=0))return Fail("invalid_ac_resistance");
            return new(new(primitive.Name,primitive.P,primitive.N,effective,profile,primitive.Range){AcOhms=effectiveAc,DcOpen=dcOpen},[]);
        }
        if(!catalog.Primitives.Contains(primitive))return Fail("foreign_resistor_instance");
        if(primitive.Family!="R")return Fail("unsupported_resistor_profile");
        // A same-named .param (and every numeric/braced expression) wins over
        // a model name in native ngspice. Do not reinterpret an evaluation error.
        if(primitive.Supported&&primitive.Value is {} scalarSyntax)
        {
            var scalar=ParameterEvaluator.EvaluateValue(catalog,environment,scalarSyntax);
            if(scalar.Value is {} ohms)return Finish(ohms,ResistorProfile.Scalar);
            if(scalar.Diagnostics[0].Code!="missing_parameter"||primitive.ResistorModel==null)return Fail(scalar.Diagnostics[0].Code,scalar.Diagnostics[0].Range);
        }
        if(primitive.ResistorModel is not {} syntax)return Fail("unsupported_resistor_profile");
        var geometryTerms=ImmutableArray.CreateBuilder<ParameterSyntax>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var term in syntax.Parameters)
        {
            var key=term.Name?.ToLowerInvariant();
            if(!requested||key is not ("ac" or "m" or "scale")){geometryTerms.Add(term);continue;}
            if(!seen.Add(key))return Fail("unsupported_resistor_instance_parameter",term.Range);
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,term.Value);
            if(value.Value is not {} number)return Fail(value.Diagnostics[0].Code,value.Diagnostics[0].Range);
            if(!double.IsFinite(number)||number<=0)return Fail(key=="ac"?"invalid_ac_resistance":"invalid_resistor_instance_parameter",term.Range);
            switch(key){case "ac":ac=number;break;case "m":multiplicity=number;break;case "scale":scale=number;break;}
        }
        var reference=ParameterEvaluator.EvaluateValue(catalog,environment,syntax.Reference);
        if(reference.Value is {} scalarOhms)
            return requested&&geometryTerms.Count==0?Finish(scalarOhms,ResistorProfile.Scalar):Fail("unsupported_resistor_instance_parameter");
        if(reference.Diagnostics[0].Code!="missing_parameter")return Fail(reference.Diagnostics[0].Code,reference.Diagnostics[0].Range);
        var lookup=catalog.Lookup(DeclarationKind.Model,syntax.Reference.Text,primitive.ScopeId);
        if(lookup.Provisional||lookup.Status!=NameLookupStatus.Found)return Fail("unbound_resistor_model",syntax.Reference.Range);
        var model=lookup.Candidates.Single();
        if(model.ModelFamily!="r")return Fail("unsupported_resistor_model",model.Range);
        if(model.HasOpaqueModelTerms)return Fail("unsupported_resistor_model_parameter",model.Range);
        var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in model.Parameters)
        {
            var name=parameter.Name?.ToLowerInvariant();
            // ngspice can apply the settable resistance instance parameter from
            // a model card. The constant-only profile uses the same scalar slot.
            if(name=="resistance")name="r";
            if(name is not ("r" or "rsh" or "short" or "narrow")||values.ContainsKey(name))return Fail("unsupported_resistor_model_parameter",parameter.Range);
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value,model.ScopeId);
            if(value.Value is not {} number)return Fail(value.Diagnostics[0].Code,value.Diagnostics[0].Range);
            values.Add(name,number);
        }
        if(values.TryGetValue("r",out var constant))
        {
            if(values.Count!=1)return Fail("unsupported_resistor_model_parameter",model.Range);
            if(geometryTerms.Count!=0)return Fail("unsupported_resistor_instance_parameter");
            return Finish(constant,ResistorProfile.ConstantModel);
        }
        if(!values.TryGetValue("rsh",out var sheet)||sheet<=0)return Fail("unsupported_resistor_model_parameter",model.Range);
        var geometry=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in geometryTerms)
        {
            if(parameter.Name is not {} name||!new[]{"l","w"}.Contains(name,StringComparer.OrdinalIgnoreCase)||geometry.ContainsKey(name))return Fail("unsupported_resistor_instance_parameter",parameter.Range);
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value);
            if(value.Value is not {} number)return Fail(value.Diagnostics[0].Code,value.Diagnostics[0].Range);
            geometry.Add(name,number);
        }
        if(!geometry.TryGetValue("l",out var length)||!geometry.TryGetValue("w",out var width)||length<=0||width<=0)return Fail("invalid_resistor_geometry");
        // Preserve native branch selection: an underflowed L*W*RSH would
        // select its 1 mOhm fallback, which this explicit sheet profile refuses.
        if(length*width*sheet<=0)return Fail("unsupported_resistor_geometry_underflow");
        // ngspice subtracts both etched edges. Finite negative reductions are
        // permitted, but neither effective dimension may vanish or reverse.
        var effectiveLength=length-2*values.GetValueOrDefault("short");var effectiveWidth=width-2*values.GetValueOrDefault("narrow");
        if(!double.IsFinite(effectiveLength)||!double.IsFinite(effectiveWidth)||effectiveLength<=0||effectiveWidth<0)return Fail("invalid_resistor_geometry");
        // With an explicit finite AC resistance the original array fixture has
        // an exact DC open circuit. Preserve that limit without a large fake R.
        if(effectiveWidth==0)return requested&&ac.HasValue
            ?Finish(double.PositiveInfinity,ResistorProfile.SheetModel,dcOpen:true):Fail("invalid_resistor_geometry");
        return Finish(effectiveLength/effectiveWidth*sheet,ResistorProfile.SheetModel);
    }
}
