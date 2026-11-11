using System.Collections.Immutable;
using Simulator.Syntax;
using Simulator.Client;
namespace Simulator.Frontend;

/// <summary>Prepared affine relation; the requested compiler may instead attach an internal typed DC expression. Voltage is P-minus-N; current flows P to N.</summary>
public sealed record PreparedAffineBehavioralSource(string Name,SourceSyntax P,SourceSyntax N,BehavioralQuantity Quantity,ImmutableArray<AffineVoltageTerm> Terms,double Offset,SourceRange Range)
{
    internal PreparedBehavioralVoltage? Runtime { get; init; }
}
internal sealed record PreparedBehavioralVoltage(SourceSyntax Cp,SourceSyntax Cn,BoundedExpression Expression);
/// <summary>Atomic behavioral-source admission; no partial request or approximate coefficients are returned on refusal.</summary>
public sealed record BehavioralSourcePreparation(PreparedAffineBehavioralSource? Source,ImmutableArray<BridgeDiagnostic> Diagnostics)
{
    /// <summary>True only when the whole source fits the explicitly selected preparation profile.</summary>
    public bool Success=>Source!=null&&Diagnostics.IsEmpty;
}
/// <summary>Generated-metadata adapter: strict public affine preparation and explicit requested DC expression lowering.</summary>
public static class BehavioralSourceLowering
{
    /// <summary>Prepares one catalog-owned B source in a retained lexical environment. Root bridge integration decides node allocation and existing typed VCVS/VCCS/source emission.</summary>
    public static BehavioralSourcePreparation Prepare(DeclarationCatalog catalog,BehavioralSourceSyntax source,ParameterEvaluation environment)
        =>PrepareCore(catalog,source,environment,false);

    internal static BehavioralSourcePreparation PrepareRequested(DeclarationCatalog catalog,BehavioralSourceSyntax source,ParameterEvaluation environment)
        =>PrepareCore(catalog,source,environment,true);

    private static BehavioralSourcePreparation PrepareCore(DeclarationCatalog catalog,BehavioralSourceSyntax source,ParameterEvaluation environment,bool requested)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(environment);
        if(!catalog.BehavioralSources.Contains(source))return new(null,[new("foreign_behavior_source",source.Range)]);
        if(!source.Supported||source.P==null||source.N==null||source.Expression==null)return new(null,[new("unsupported_behavior_header",source.Range)]);
        var affine=AffineVoltageExpressionEvaluator.Evaluate(catalog,environment,source.Expression);
        if(affine.Form is not {} form)
        {
            // Preserve the strict public affine API. The requested compiler
            // explicitly opts into the separately bounded DC model contract.
            if(requested&&source.Quantity==BehavioralQuantity.Voltage&&affine.Diagnostics.Any(d=>d.Code=="non_affine_behavior"))
            {
                var runtime=SingleControlVoltageCompiler.Compile(catalog,environment,source.Expression,new NativeExpressionFactory());
                if(runtime.Success)
                {
                    // Desugaring subtraction/log10 adds nodes. Validate the
                    // emitted model, not merely the original parser-tree size.
                    if(!FitsNativeBudget(runtime.Expression!))return new(null,[new("expression_budget",source.Range)]);
                    return new(new(source.Name,source.P,source.N,source.Quantity,[],0,source.Range)
                        {Runtime=new(runtime.ControlP!,runtime.ControlN!,runtime.Expression!)},[]);
                }
                return new(null,runtime.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
            }
            return new(null,affine.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
        }
        return new(new(source.Name,source.P,source.N,source.Quantity,form.Terms,form.Offset,source.Range),[]);
    }
    private static bool FitsNativeBudget(BoundedExpression expression)
    {
        var pending=new Stack<(BoundedExpression Node,int Depth)>();pending.Push((expression,0));
        var visited=0;
        while(pending.TryPop(out var item))
        {
            if(++visited>1024||item.Depth>64)return false;
            switch(item.Node)
            {
                case ExpressionConstant constant:if(!double.IsFinite(constant.Value))return false;break;
                case ExpressionInput:break;
                case ExpressionAdd add:pending.Push((add.Left,item.Depth+1));pending.Push((add.Right,item.Depth+1));break;
                case ExpressionMultiply multiply:pending.Push((multiply.Left,item.Depth+1));pending.Push((multiply.Right,item.Depth+1));break;
                case ExpressionDivide divide:pending.Push((divide.Left,item.Depth+1));pending.Push((divide.Right,item.Depth+1));break;
                case ExpressionExp exp:pending.Push((exp.Argument,item.Depth+1));break;
                case ExpressionLog log:pending.Push((log.Argument,item.Depth+1));break;
                default:return false;
            }
        }
        return true;
    }
    private sealed class NativeExpressionFactory:IBoundedScalarExpressionFactory<BoundedExpression>
    {
        public BoundedExpression Constant(double value)=>new ExpressionConstant(value);
        public BoundedExpression Input()=>new ExpressionInput();
        public BoundedExpression Add(BoundedExpression left,BoundedExpression right)=>new ExpressionAdd(left,right);
        public BoundedExpression Multiply(BoundedExpression left,BoundedExpression right)=>new ExpressionMultiply(left,right);
        public BoundedExpression Divide(BoundedExpression left,BoundedExpression right)=>new ExpressionDivide(left,right);
        public BoundedExpression Exp(BoundedExpression argument)=>new ExpressionExp(argument);
        public BoundedExpression Log(BoundedExpression argument)=>new ExpressionLog(argument);
    }
}
