using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>One high-impedance differential voltage input and its dimensionless contribution to output voltage.</summary>
public sealed record PreparedSummerInput(SourceSyntax P,SourceSyntax N,double Gain);
/// <summary>V(OutputP)-V(OutputN)=sum(Gain[i]*(V(P[i])-V(N[i])))+Offset, in volts. Original controls and their order remain explicit, including zero gains.</summary>
public sealed record PreparedXspiceSummer(string Name,SourceSyntax OutputP,SourceSyntax OutputN,ImmutableArray<PreparedSummerInput> Inputs,double Offset,SourceRange Range);
/// <summary>Atomic summer admission: no partial inputs or guessed default arrays escape an unsupported profile.</summary>
public sealed record XspiceSummerPreparation(PreparedXspiceSummer? Summer,ImmutableArray<BridgeDiagnostic> Diagnostics)
{
    /// <summary>True only for a fully evaluated bounded scalar-voltage summer relation.</summary>
    public bool Success=>Summer!=null&&Diagnostics.IsEmpty;
}
/// <summary>Bounded analog summer preparation over generated vector metadata and the existing parameter evaluator; no new model kernel.</summary>
public static class XspiceSummerLowering
{
    /// <summary>Resolves and prepares 2..16 voltage inputs plus a scalar voltage output. Per-input arrays must match port count; omitted arrays receive the documented per-input defaults.</summary>
    public static XspiceSummerPreparation Prepare(DeclarationCatalog catalog,XspiceInstanceSyntax instance,ParameterEvaluation environment)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(instance);ArgumentNullException.ThrowIfNull(environment);
        XspiceSummerPreparation Fail(string code,SourceRange? range=null)=>new(null,[new(code,range??instance.Range)]);
        if(!catalog.XspiceInstances.Contains(instance))return Fail("foreign_xspice_instance");
        var lookup=catalog.Lookup(DeclarationKind.Model,instance.Model.Text,instance.ScopeId);
        if(lookup.Provisional||lookup.Status!=NameLookupStatus.Found)return Fail("unbound_xspice_model");
        var validation=ParameterEvaluator.EvaluateValue(catalog,environment,new("0",instance.Range));
        if(validation.Value==null)return Fail(validation.Diagnostics[0].Code,validation.Diagnostics[0].Range);
        var model=lookup.Candidates.Single();
        if(!string.Equals(model.ModelFamily,"summer",StringComparison.OrdinalIgnoreCase))return Fail("unsupported_xspice_model");
        if(model.HasOpaqueModelTerms)return Fail("unsupported_summer_parameter",model.Range);
        if(instance.Ports.Length!=2)return Fail("unsupported_summer_ports");
        var vector=instance.Ports[0];var output=instance.Ports[1];var count=vector.Elements.Length;
        if(!vector.IsVector||!vector.IsVoltageVector||count is <2 or >16||vector.Elements.Any(p=>!p.Supported)||!output.Supported)return Fail("unsupported_summer_ports");
        var gains=Enumerable.Repeat(1d,count).ToArray();var offsets=new double[count];var outGain=1d;var outOffset=0d;
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in model.Parameters)
        {
            var name=parameter.Name?.ToLowerInvariant();
            if(name is not ("in_gain" or "in_offset" or "out_gain" or "out_offset")||!seen.Add(name))return Fail("unsupported_summer_parameter",parameter.Range);
            if(name is "in_gain" or "in_offset")
            {
                if(parameter.ScalarVectorValues is not {} values)return Fail("unsupported_summer_vector",parameter.Range);
                if(values.Length!=count)return Fail("summer_vector_length",parameter.Range);
                var target=name=="in_gain"?gains:offsets;
                for(var i=0;i<count;i++)
                {
                    var value=ParameterEvaluator.EvaluateValue(catalog,environment,values[i],model.ScopeId);
                    if(value.Value is not {} scalar)return Fail(value.Diagnostics[0].Code,value.Diagnostics[0].Range);
                    target[i]=scalar;
                }
            }
            else
            {
                var value=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value,model.ScopeId);
                if(value.Value is not {} scalar)return Fail(value.Diagnostics[0].Code,value.Diagnostics[0].Range);
                if(name=="out_gain")outGain=scalar;else outOffset=scalar;
            }
        }
        var inputs=ImmutableArray.CreateBuilder<PreparedSummerInput>();var accumulate=0d;
        for(var i=0;i<count;i++)
        {
            var coefficient=outGain*gains[i];accumulate+=gains[i]*offsets[i];
            if(!double.IsFinite(coefficient)||!double.IsFinite(accumulate))return Fail("nonfinite_summer_relation",model.Range);
            inputs.Add(new(vector.Elements[i].P!,vector.Elements[i].N!,coefficient));
        }
        var offset=outGain*accumulate+outOffset;
        if(!double.IsFinite(offset))return Fail("nonfinite_summer_relation",model.Range);
        return new(new(instance.Name,output.P!,output.N!,inputs.ToImmutable(),offset,instance.Range),[]);
    }
}
