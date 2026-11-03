using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Finite prepared relation: output quantity equals Offset plus the sum of node-voltage terms. Voltage output is P-minus-N; current flows P to N.</summary>
public sealed record PreparedAffineBehavioralSource(string Name,SourceSyntax P,SourceSyntax N,BehavioralQuantity Quantity,ImmutableArray<AffineVoltageTerm> Terms,double Offset,SourceRange Range);
/// <summary>Atomic behavioral-source admission; no partial request or approximate coefficients are returned on refusal.</summary>
public sealed record BehavioralSourcePreparation(PreparedAffineBehavioralSource? Source,ImmutableArray<BridgeDiagnostic> Diagnostics)
{
    /// <summary>True only when every expression/header term fits the bounded affine profile.</summary>
    public bool Success=>Source!=null&&Diagnostics.IsEmpty;
}
/// <summary>Preparation adapter over generated metadata and affine derivation; no runtime expression interpreter or new model kernel.</summary>
public static class BehavioralSourceLowering
{
    /// <summary>Prepares one catalog-owned B source in a retained lexical environment. Root bridge integration decides node allocation and existing typed VCVS/VCCS/source emission.</summary>
    public static BehavioralSourcePreparation Prepare(DeclarationCatalog catalog,BehavioralSourceSyntax source,ParameterEvaluation environment)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(environment);
        if(!catalog.BehavioralSources.Contains(source))return new(null,[new("foreign_behavior_source",source.Range)]);
        if(!source.Supported||source.P==null||source.N==null||source.Expression==null)return new(null,[new("unsupported_behavior_header",source.Range)]);
        var affine=AffineVoltageExpressionEvaluator.Evaluate(catalog,environment,source.Expression);
        if(affine.Form is not {} form)return new(null,affine.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
        return new(new(source.Name,source.P,source.N,source.Quantity,form.Terms,form.Offset,source.Range),[]);
    }
}
