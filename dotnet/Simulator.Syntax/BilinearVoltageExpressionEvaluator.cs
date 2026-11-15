using System.Collections.Immutable;
using Antlr4.Runtime;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;
/// <summary>Product of two affine voltage factors; coefficient uses the behavioral numeric SI convention.</summary>
public sealed record AffineVoltageProduct(AffineVoltageForm Left,AffineVoltageForm Right,double Coefficient);
/// <summary>Bounded sum of an affine voltage law and at most eight products of affine voltage factors.</summary>
public sealed record BilinearVoltageForm(AffineVoltageForm Affine,ImmutableArray<AffineVoltageProduct> Products);
/// <summary>Atomic bilinear preparation result; diagnostics never carry a partial expression.</summary>
public sealed record BilinearVoltageEvaluation(BilinearVoltageForm? Form,ImmutableArray<ParameterDiagnostic> Diagnostics);
/// <summary>Reuses generated expressions and the existing affine evaluator; no alternate lexer or nonlinear approximation.</summary>
public static class BilinearVoltageExpressionEvaluator
{
    /// <summary>Accepts sums/differences and scalar multiples of affine-factor products. Higher degree, dynamic variables and nonlinear divisors refuse.</summary>
    public static BilinearVoltageEvaluation Evaluate(DeclarationCatalog catalog,ParameterEvaluation environment,SourceSyntax source)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(environment);ArgumentNullException.ThrowIfNull(source);
        BilinearVoltageEvaluation Fail(string code)=>new(null,[new(code,"",source.Range)]);
        if(source.Text.Length>4096)return Fail("expression_budget");
        const string prefix="Bilinear expression\n.param __bilinear={";
        var text=prefix+source.Text+"}\n.end\n";
        if(DeckParser.Parse(text).Status!="accepted_structure")return Fail("unsupported_behavior_expression");
        var lexer=new SpiceLexer(new AntlrInputStream(text));lexer.RemoveErrorListeners();
        var parser=new SpiceParser(new CommonTokenStream(lexer));parser.RemoveErrorListeners();
        var deck=parser.deck();if(deck.statement().Length!=2)return Fail("unsupported_behavior_expression");
        var root=deck.statement(0).paramCard()?.assignment(0).value()?.expression();
        if(deck.statement().Length!=2||root==null||root.Start.StartIndex!=prefix.Length||root.Stop.StopIndex!=prefix.Length+source.Text.Length-1)return Fail("unsupported_behavior_expression");
        var terms=new Dictionary<string,AffineVoltageTerm>(StringComparer.OrdinalIgnoreCase);var offset=0d;var products=ImmutableArray.CreateBuilder<AffineVoltageProduct>();var visited=0;
        try{Visit(root,1,0);return new(new(new(offset,terms.Values.OrderBy(t=>t.Node.Text,StringComparer.OrdinalIgnoreCase).ToImmutableArray()),products.ToImmutable()),[]);}
        catch(BilinearFailure failure){return Fail(failure.Code);}
        void Visit(SpiceParser.ExpressionContext node,double factor,int depth)
        {
            if(++visited>1024||depth>64||!double.IsFinite(factor))throw new BilinearFailure("expression_budget");
            var affine=AffineVoltageExpressionEvaluator.Evaluate(catalog,environment,Capture(node));
            if(affine.Form is {} value){Add(value,factor);return;}
            var children=node.expression();
            if(children.Length==1&&node.ChildCount==3&&node.GetChild(0).GetText() is "(" or "{"){Visit(children[0],factor,depth+1);return;}
            if(children.Length==1&&node.ChildCount==2&&node.GetChild(0).GetText() is "+" or "-"){Visit(children[0],node.GetChild(0).GetText()=="-"?-factor:factor,depth+1);return;}
            if(children.Length!=2||node.ChildCount!=3)throw new BilinearFailure("non_affine_behavior");
            var op=node.GetChild(1).GetText();
            if(op is "+" or "-"){Visit(children[0],factor,depth+1);Visit(children[1],op=="-"?-factor:factor,depth+1);return;}
            var left=AffineVoltageExpressionEvaluator.Evaluate(catalog,environment,Capture(children[0]));
            var right=AffineVoltageExpressionEvaluator.Evaluate(catalog,environment,Capture(children[1]));
            if(op=="*"&&left.Form is {Terms.IsEmpty:true} scalarLeft){Visit(children[1],factor*scalarLeft.Offset,depth+1);return;}
            if(op=="*"&&right.Form is {Terms.IsEmpty:true} scalarRight){Visit(children[0],factor*scalarRight.Offset,depth+1);return;}
            if(op=="/"&&right.Form is {Terms.IsEmpty:true} denominator&&denominator.Offset!=0){Visit(children[0],factor/denominator.Offset,depth+1);return;}
            if(op!="*"||left.Form==null||right.Form==null)throw new BilinearFailure("non_affine_behavior");
            if(products.Count>=8)throw new BilinearFailure("behavior_product_budget");
            products.Add(new(left.Form,right.Form,factor));
        }
        void Add(AffineVoltageForm form,double factor)
        {
            offset+=form.Offset*factor;
            if(!double.IsFinite(offset))throw new BilinearFailure("nonfinite_behavior_coefficient");
            foreach(var term in form.Terms)
            {
                var coefficient=term.Coefficient*factor+(terms.TryGetValue(term.Node.Text,out var prior)?prior.Coefficient:0);
                if(!double.IsFinite(coefficient))throw new BilinearFailure("nonfinite_behavior_coefficient");
                if(coefficient==0)terms.Remove(term.Node.Text);else terms[term.Node.Text]=new(term.Node,coefficient);
                if(terms.Count>16)throw new BilinearFailure("behavior_term_budget");
            }
        }
        SourceSyntax Capture(ParserRuleContext node)
        {
            var start=node.Start.StartIndex-prefix.Length;var length=node.Stop.StopIndex-node.Start.StartIndex+1;
            var (line,column)=new DeckParser.PhysicalPositions(source.Text).At(start);
            return new(source.Text.Substring(start,length),new(source.Range.Start+start,length,source.Range.Line+line-1,line==1?source.Range.Column+column-1:column));
        }
    }
    private sealed class BilinearFailure(string code):Exception { internal string Code{get;}=code; }
}
