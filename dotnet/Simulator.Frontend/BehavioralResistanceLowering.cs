using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Requested-only complete R(Vcontrol) preparation; no guessed bias or constant substitution.</summary>
internal static class BehavioralResistanceLowering
{
    internal sealed record Prepared(SourceSyntax Cp,SourceSyntax Cn,BoundedExpression Expression);
    internal sealed record Result(Prepared? Resistance,ImmutableArray<BridgeDiagnostic> Diagnostics);

    internal static Result Prepare(DeclarationCatalog catalog,PrimitiveSyntax primitive,ParameterEvaluation environment,double celsius)
    {
        Result Fail(string code,SourceRange? range=null)=>new(null,[new(code,range??primitive.Range)]);
        if(primitive.BehavioralResistance is not {} source)return Fail("unsupported_behavioral_resistance");
        var coefficients=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in source.Parameters)
        {
            if(parameter.Name is not {} name||name.ToLowerInvariant() is not ("tc1" or "tc2")||coefficients.ContainsKey(name))
                return Fail("unsupported_behavioral_resistance_parameter",parameter.Range);
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value);
            if(value.Value is not {} scalar)return Fail(value.Diagnostics[0].Code,value.Diagnostics[0].Range);
            coefficients.Add(name,scalar);
        }
        // ASRC polynomial nominal is 27 C. Temperature scales the resistance,
        // hence current and both runtime partials scale inversely in the native law.
        var delta=celsius-27;
        var factor=1+coefficients.GetValueOrDefault("tc1")*delta+coefficients.GetValueOrDefault("tc2")*delta*delta;
        if(!double.IsFinite(factor)||factor==0)return Fail("invalid_behavioral_resistance_temperature_factor");
        var compiled=SingleControlVoltageCompiler.Compile(catalog,environment,source.Expression,new BehavioralSourceLowering.NativeExpressionFactory());
        if(!compiled.Success)return new(null,compiled.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
        BoundedExpression expression=new ExpressionMultiply(compiled.Expression!,new ExpressionConstant(factor));
        if(!BehavioralSourceLowering.FitsNativeBudget(expression))return Fail("expression_budget");
        return new(new(compiled.ControlP!,compiled.ControlN!,expression),[]);
    }
}
