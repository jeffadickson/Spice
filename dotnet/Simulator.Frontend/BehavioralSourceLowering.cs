using System.Collections.Immutable;
using Simulator.Syntax;
using Simulator.Client;
namespace Simulator.Frontend;

/// <summary>Prepared affine relation; the requested compiler may instead attach an internal typed DC expression. Voltage is P-minus-N; current flows P to N.</summary>
public sealed record PreparedAffineBehavioralSource(string Name,SourceSyntax P,SourceSyntax N,BehavioralQuantity Quantity,ImmutableArray<AffineVoltageTerm> Terms,double Offset,SourceRange Range)
{
    internal PreparedBehavioralVoltage? Runtime { get; init; }
    internal ImmutableArray<AffineVoltageProduct> Products { get; init; } = [];
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

    internal static BehavioralSourcePreparation PrepareRequested(DeclarationCatalog catalog,BehavioralSourceSyntax source,ParameterEvaluation environment,double temperatureCelsius=27)
        =>PrepareCore(catalog,source,environment,true,temperatureCelsius);

    private static BehavioralSourcePreparation PrepareCore(DeclarationCatalog catalog,BehavioralSourceSyntax source,ParameterEvaluation environment,bool requested,double temperatureCelsius=27)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(environment);
        if(!catalog.BehavioralSources.Contains(source))return new(null,[new("foreign_behavior_source",source.Range)]);
        if(!(source.Supported||requested&&source.HeaderSupported)||source.P==null||source.N==null||source.Expression==null)return new(null,[new("unsupported_behavior_header",source.Range)]);
        var affine=AffineVoltageExpressionEvaluator.Evaluate(catalog,environment,source.Expression);
        if(affine.Form is not {} form)
        {
            // Preserve the strict public affine API. The requested compiler
            // explicitly opts into the separately bounded DC model contract.
            if(requested&&source.Parameters.IsEmpty&&source.Quantity==BehavioralQuantity.Voltage&&affine.Diagnostics.Any(d=>d.Code=="non_affine_behavior"))
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
                if(runtime.Diagnostics.Any(d=>d.Code=="unsupported_behavior_control_count"))
                {
                    var bilinear=BilinearVoltageExpressionEvaluator.Evaluate(catalog,environment,source.Expression);
                    if(bilinear.Form is {} polynomial&&!polynomial.Products.IsEmpty)
                        return new(new(source.Name,source.P,source.N,source.Quantity,polynomial.Affine.Terms,polynomial.Affine.Offset,source.Range){Products=polynomial.Products},[]);
                    if(bilinear.Diagnostics.Any(d=>d.Code is "behavior_product_budget" or "expression_budget"))
                        return new(null,bilinear.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
                }
                return new(null,runtime.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
            }
            return new(null,affine.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
        }
        var coefficients=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in source.Parameters)
        {
            if(parameter.Name is not {} name||name.ToLowerInvariant() is not ("tc1" or "tc2" or "m")||coefficients.ContainsKey(name))
                return new(null,[new("unsupported_behavior_parameter",parameter.Range)]);
            var evaluated=ParameterEvaluator.EvaluateValue(catalog,environment,parameter.Value);
            if(evaluated.Value is not {} value)return new(null,evaluated.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
            if(name.Equals("m",StringComparison.OrdinalIgnoreCase)&&(source.Quantity!=BehavioralQuantity.Current||!double.IsFinite(value)||value<=0))
                return new(null,[new("invalid_source_multiplier",parameter.Range)]);
            coefficients.Add(name,value);
        }
        // Scale the complete affine law, including its DC offset. The same
        // finite coefficients then drive the existing DC and AC typed stamps.
        var delta=temperatureCelsius-27;
        var factor=(1+coefficients.GetValueOrDefault("tc1")*delta+coefficients.GetValueOrDefault("tc2")*delta*delta)*coefficients.GetValueOrDefault("m",1);
        if(source.CurrentMultiplier is {} multiplier)
        {
            var evaluated=ParameterEvaluator.EvaluateValue(catalog,environment,multiplier);
            if(evaluated.Value is not {} value)return new(null,evaluated.Diagnostics.Select(d=>new BridgeDiagnostic(d.Code,d.Range)).ToImmutableArray());
            if(!double.IsFinite(value)||value<=0)return new(null,[new("invalid_source_multiplier",multiplier.Range)]);
            factor*=value;
        }
        var terms=form.Terms.Select(term=>term with{Coefficient=term.Coefficient*factor}).ToImmutableArray();
        var offset=form.Offset*factor;
        if(!double.IsFinite(factor)||!double.IsFinite(offset)||terms.Any(term=>!double.IsFinite(term.Coefficient)))
            return new(null,[new(source.CurrentMultiplier==null?"invalid_behavior_temperature_factor":"invalid_source_multiplier",source.Range)]);
        return new(new(source.Name,source.P,source.N,source.Quantity,terms,offset,source.Range),[]);
    }
    internal static bool FitsNativeBudget(BoundedExpression expression)
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
                case ExpressionAbs a:pending.Push((a.Argument,item.Depth+1));break;
                case ExpressionAsin a:pending.Push((a.Argument,item.Depth+1));break;
                case ExpressionAcos a:pending.Push((a.Argument,item.Depth+1));break;
                default:return false;
            }
        }
        return true;
    }
    internal sealed class NativeExpressionFactory:IBoundedScalarExpressionFactory<BoundedExpression>
    {
        public BoundedExpression Constant(double value)=>new ExpressionConstant(value);
        public BoundedExpression Input()=>new ExpressionInput();
        public BoundedExpression Add(BoundedExpression left,BoundedExpression right)=>new ExpressionAdd(left,right);
        public BoundedExpression Multiply(BoundedExpression left,BoundedExpression right)=>new ExpressionMultiply(left,right);
        public BoundedExpression Divide(BoundedExpression left,BoundedExpression right)=>new ExpressionDivide(left,right);
        public BoundedExpression Exp(BoundedExpression argument)=>new ExpressionExp(argument);
        public BoundedExpression Log(BoundedExpression argument)=>new ExpressionLog(argument);
        public BoundedExpression Abs(BoundedExpression argument)=>new ExpressionAbs(argument);
        public BoundedExpression Asin(BoundedExpression argument)=>new ExpressionAsin(argument);
        public BoundedExpression Acos(BoundedExpression argument)=>new ExpressionAcos(argument);
        public bool TryConstant(BoundedExpression expression,out double value) {
            var visited=0;
            double Read(BoundedExpression e,int depth) {
                if(++visited>1024||depth>64)return double.NaN;
                double F(BoundedExpression child)=>Read(child,depth+1);
                return e switch {
                    ExpressionConstant c=>c.Value,ExpressionAdd a=>F(a.Left)+F(a.Right),
                    ExpressionMultiply m=>F(m.Left)*F(m.Right),ExpressionDivide d=>F(d.Left)/F(d.Right),
                    ExpressionExp u=>Math.Exp(F(u.Argument)),ExpressionLog u=>Math.Log(F(u.Argument)),
                    ExpressionAbs u=>Math.Abs(F(u.Argument)),ExpressionAsin u=>Math.Asin(F(u.Argument)),ExpressionAcos u=>Math.Acos(F(u.Argument)),
                    _=>double.NaN
                };
            }
            value=Read(expression,0);return double.IsFinite(value);
        }
    }
}
