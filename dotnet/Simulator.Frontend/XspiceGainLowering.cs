using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Exact affine voltage relation V(outP)-V(outN)=Gain*(V(inP)-V(inN))+Offset, in volts. Existing VCVS and a series constant voltage source implement it.</summary>
public sealed record PreparedXspiceGain(string Name,SourceSyntax InputP,SourceSyntax InputN,SourceSyntax OutputP,SourceSyntax OutputN,double Gain,double Offset,SourceRange Range);
/// <summary>Atomic model admission: no prepared relation is returned on any unsupported parameter or port.</summary>
public sealed record XspiceGainPreparation(PreparedXspiceGain? Gain,ImmutableArray<BridgeDiagnostic> Diagnostics)
{
    /// <summary>True only for a fully evaluated supported scalar voltage gain relation.</summary>
    public bool Success=>Gain!=null&&Diagnostics.IsEmpty;
}
/// <summary>Bounded ngspice analog gain contract; this does not implement a generic XSPICE kernel.</summary>
public static class XspiceGainLowering
{
    /// <summary>Resolves a lexical model and evaluates its scalar parameters through the existing environment. Unsupported current, digital, vector and inverted ports remain explicit refusals.</summary>
    public static XspiceGainPreparation Prepare(DeclarationCatalog catalog,XspiceInstanceSyntax instance,ParameterEvaluation environment)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(instance);ArgumentNullException.ThrowIfNull(environment);
        XspiceGainPreparation Fail(string code,SourceRange? range=null)=>new(null,[new(code,range??instance.Range)]);
        // Public metadata is immutable but caller-constructible: reject unrelated
        // records, rather than resolve their model under an unrelated catalog.
        if(!catalog.XspiceInstances.Contains(instance))return Fail("foreign_xspice_instance");
        var validation=ParameterEvaluator.EvaluateValue(catalog,environment,new("0",instance.Range));
        if(validation.Value==null)return Fail(validation.Diagnostics[0].Code,validation.Diagnostics[0].Range);
        var lookup=catalog.Lookup(DeclarationKind.Model,instance.Model.Text,instance.ScopeId);
        if(lookup.Provisional||lookup.Status!=NameLookupStatus.Found)return Fail("unbound_xspice_model");
        var model=lookup.Candidates.Single();
        if(!string.Equals(model.ModelFamily,"gain",StringComparison.OrdinalIgnoreCase))return Fail("unsupported_xspice_model");
        if(model.HasOpaqueModelTerms)return Fail("unsupported_xspice_parameter",model.Range);
        if(instance.Ports.Length!=2||instance.Ports.Any(p=>!p.Supported))return Fail("unsupported_xspice_gain_ports");
        var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase){{"in_offset",0},{"gain",1},{"out_offset",0}};
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in model.Parameters)
        {
            if(parameter.Name==null||!values.ContainsKey(parameter.Name)||!seen.Add(parameter.Name))return Fail("unsupported_xspice_parameter",parameter.Range);
            var scalar=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value,model.ScopeId);
            if(scalar.Value is not {} value)return Fail(scalar.Diagnostics[0].Code,scalar.Diagnostics[0].Range);
            values[parameter.Name]=value;
        }
        var offset=values["gain"]*values["in_offset"]+values["out_offset"];
        if(!double.IsFinite(offset))return Fail("nonfinite_xspice_offset");
        var input=instance.Ports[0];var output=instance.Ports[1];
        return new(new(instance.Name,input.P!,input.N!,output.P!,output.N!,values["gain"],offset,instance.Range),[]);
    }
}
