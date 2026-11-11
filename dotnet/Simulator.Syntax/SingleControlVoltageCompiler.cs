using System.Collections.Immutable;
using Antlr4.Runtime;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

/// <summary>Typed scalar-model construction boundary; syntax never depends on a native solver's DTO assembly.</summary>
public interface IBoundedScalarExpressionFactory<T> where T:class
{
    /// <summary>Construct a finite literal.</summary>
    T Constant(double value);
    /// <summary>Construct the one differential voltage input.</summary>
    T Input();
    /// <summary>Construct an ordered sum.</summary>
    T Add(T left,T right);
    /// <summary>Construct an ordered product.</summary>
    T Multiply(T left,T right);
    /// <summary>Construct a quotient with runtime denominator validation.</summary>
    T Divide(T left,T right);
    /// <summary>Construct a natural exponential.</summary>
    T Exp(T argument);
    /// <summary>Construct a positive-domain natural logarithm.</summary>
    T Log(T argument);
}
/// <summary>Atomic model lowering result with retained physical control-terminal spellings.</summary>
public sealed record SingleControlVoltageExpression<T>(T? Expression,SourceSyntax? ControlP,SourceSyntax? ControlN,
    ImmutableArray<ParameterDiagnostic> Diagnostics) where T:class
{
    /// <summary>Whether the complete expression fits the bounded single-control profile.</summary>
    public bool Success=>Expression!=null&&ControlP!=null&&ControlN!=null&&Diagnostics.IsEmpty;
}

/// <summary>Generated-expression compiler for stateless single-control scalar voltage models.</summary>
public static class SingleControlVoltageCompiler
{
    /// <summary>Preserves voltage dependence in a typed tree; only actual parameter leaves are evaluated during preparation.</summary>
    public static SingleControlVoltageExpression<T> Compile<T>(DeclarationCatalog catalog,ParameterEvaluation environment,
        SourceSyntax source,IBoundedScalarExpressionFactory<T> factory) where T:class
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(factory);
        SingleControlVoltageExpression<T> Fail(string code)=>new(null,null,null,[new(code,"",source.Range)]);
        if(source.Text.Length>4096)return Fail("expression_budget");
        var identity=ParameterEvaluator.EvaluateValue(catalog,environment,new("0",source.Range));
        if(identity.Value==null)return new(null,null,null,identity.Diagnostics);
        const string prefix="Behavior expression\n.param __behavior={";
        var text=prefix+source.Text+"}\n.end\n";
        if(DeckParser.Parse(text).Status!="accepted_structure")return Fail("unsupported_behavior_expression");
        var lexer=new SpiceLexer(new AntlrInputStream(text));lexer.RemoveErrorListeners();
        var parser=new SpiceParser(new CommonTokenStream(lexer));parser.RemoveErrorListeners();
        var deck=parser.deck();
        if(deck.statement().Length!=2||deck.statement(0).paramCard()?.assignment().Length!=1)return Fail("unsupported_behavior_expression");
        var root=deck.statement(0).paramCard().assignment(0).value()?.expression();
        if(root==null||root.Start.StartIndex!=prefix.Length||root.Stop.StopIndex!=prefix.Length+source.Text.Length-1)return Fail("unsupported_behavior_expression");
        SourceSyntax? cp=null,cn=null;var visited=0;
        var positions=new DeckParser.PhysicalPositions(source.Text);
        try
        {
            var expression=Eval(root,0);
            return cp==null?Fail("unsupported_behavior_control_count"):new(expression,cp,cn,[]);
        }
        catch(CompileFailure error){return Fail(error.Code);}

        T Eval(SpiceParser.ExpressionContext node,int depth)
        {
            if(++visited>1024||depth>64)throw new CompileFailure("expression_budget");
            var children=node.expression();
            if(node.voltageReference() is {} reference)
            {
                var terminals=reference.referenceNode();
                if(terminals.Any(n=>n.WORD()==null))throw new CompileFailure("unsupported_behavior_node");
                var p=Capture(terminals[0]);var n=terminals.Length==2?Capture(terminals[1]):new SourceSyntax("0",p.Range);
                if(cp==null){cp=p;cn=n;return factory.Input();}
                bool Same(SourceSyntax a,SourceSyntax b)=>a.Text.Equals(b.Text,StringComparison.OrdinalIgnoreCase);
                if(Same(cp,p)&&Same(cn!,n))return factory.Input();
                if(Same(cp,n)&&Same(cn!,p))return factory.Multiply(factory.Constant(-1),factory.Input());
                throw new CompileFailure("unsupported_behavior_control_count");
            }
            if(node.identifier()!=null&&node.LPAREN()!=null)
            {
                if(children.Length!=1)throw new CompileFailure("unsupported_behavior_function");
                var argument=Eval(children[0],depth+1);
                return node.identifier().GetText().ToLowerInvariant() switch
                {
                    "ln" or "log"=>factory.Log(argument),"exp"=>factory.Exp(argument),
                    "log10"=>factory.Divide(factory.Log(argument),factory.Constant(Math.Log(10))),
                    _=>throw new CompileFailure("unsupported_behavior_function")
                };
            }
            if(node.ChildCount==1)
            {
                if(node.identifier()?.GetText().ToLowerInvariant() is "time" or "temper" or "hertz")throw new CompileFailure("unsupported_behavior_runtime");
                var value=ParameterEvaluator.EvaluateValue(catalog,environment,Capture(node));
                if(value.Value is not {} scalar)throw new CompileFailure(value.Diagnostics[0].Code);
                return factory.Constant(scalar);
            }
            if(children.Length==1&&node.ChildCount==3)return Eval(children[0],depth+1);
            if(children.Length==1&&node.ChildCount==2)
            {
                var child=Eval(children[0],depth+1);
                return node.GetChild(0).GetText() switch {"+"=>child,"-"=>factory.Multiply(factory.Constant(-1),child),_=>throw new CompileFailure("unsupported_behavior_expression")};
            }
            if(children.Length==2&&node.ChildCount==3)
            {
                var left=Eval(children[0],depth+1);var right=Eval(children[1],depth+1);
                return node.GetChild(1).GetText() switch
                {
                    "+"=>factory.Add(left,right),"-"=>factory.Add(left,factory.Multiply(factory.Constant(-1),right)),
                    "*"=>factory.Multiply(left,right),"/"=>factory.Divide(left,right),
                    _=>throw new CompileFailure("unsupported_behavior_expression")
                };
            }
            throw new CompileFailure("unsupported_behavior_expression");
        }
        SourceSyntax Capture(ParserRuleContext node)
        {
            var start=node.Start.StartIndex-prefix.Length;var length=node.Stop.StopIndex-node.Start.StartIndex+1;
            var (line,column)=positions.At(start);
            return new(source.Text.Substring(start,length),new(source.Range.Start+start,length,
                source.Range.Line+line-1,line==1?source.Range.Column+column-1:column));
        }
    }
    private sealed class CompileFailure(string code):Exception { internal string Code{get;}=code; }
}
