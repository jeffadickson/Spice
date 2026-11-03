using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

/// <summary>One finite coefficient times a node voltage relative to ground; node spelling/range remain physical.</summary>
public sealed record AffineVoltageTerm(SourceSyntax Node,double Coefficient);
/// <summary>Finite affine relation, evaluated only during preparation, not in a numerical hot loop.</summary>
public sealed record AffineVoltageForm(double Offset,ImmutableArray<AffineVoltageTerm> Terms);
/// <summary>Atomic affine preparation result; no partial coefficients escape a refusal.</summary>
public sealed record AffineVoltageEvaluation(AffineVoltageForm? Form,ImmutableArray<ParameterDiagnostic> Diagnostics);

/// <summary>Derives a bounded affine voltage form from the existing generated expression grammar. Constants reuse ParameterEvaluator.</summary>
public static class AffineVoltageExpressionEvaluator
{
    /// <summary>Admits voltage references, sums, scalar products and scalar divisors. Nonlinear/runtime operations, conditions and quoted node references are explicit refusals.</summary>
    public static AffineVoltageEvaluation Evaluate(DeclarationCatalog catalog,ParameterEvaluation environment,SourceSyntax syntax,int maxTerms=16)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(environment);ArgumentNullException.ThrowIfNull(syntax);
        if(maxTerms is <1 or >64)throw new ArgumentOutOfRangeException(nameof(maxTerms));
        AffineVoltageEvaluation Failure(string code,SourceRange? range=null)=>new(null,[new(code,"",range??syntax.Range)]);
        if(syntax.Text.Length>4096)return Failure("expression_budget");
        var validation=ParameterEvaluator.EvaluateValue(catalog,environment,new("0",syntax.Range));
        if(validation.Value==null)return new(null,validation.Diagnostics);
        const string prefix="Affine expression\n.param __affine={";
        var text=prefix+syntax.Text+"}\n.end\n";
        if(DeckParser.Parse(text).Status!="accepted_structure")return Failure("unsupported_behavior_expression");
        var lexer=new SpiceLexer(new AntlrInputStream(text));lexer.RemoveErrorListeners();
        var parser=new SpiceParser(new CommonTokenStream(lexer));parser.RemoveErrorListeners();
        var deck=parser.deck();
        if(deck.statement().Length!=2||deck.statement(0).paramCard()?.assignment().Length!=1)return Failure("unsupported_behavior_expression");
        var expression=deck.statement(0).paramCard().assignment(0).value()?.expression();
        if(expression==null||expression.Start.StartIndex!=prefix.Length||expression.Stop.StopIndex!=prefix.Length+syntax.Text.Length-1)return Failure("unsupported_behavior_expression");
        var visited=0;
        try
        {
            // Conditions can hide inactive nonlinear branches. Refuse them
            // structurally rather than claiming support after constant folding.
            var pending=new Stack<IParseTree>();pending.Push(expression);
            while(pending.TryPop(out var item))
            {
                if(item is SpiceParser.ExpressionContext e && (e.QUESTION()!=null||e.AND()!=null||e.OR()!=null||e.COMPARE()!=null||e.EQUAL()!=null))throw new AffineFailure("unsupported_behavior_expression",Capture(e).Range);
                if(item is SpiceParser.ExpressionContext atom && atom.identifier()?.GetText().ToLowerInvariant() is "time" or "temper" or "hertz")throw new AffineFailure("unsupported_behavior_runtime",Capture(atom).Range);
                for(var i=0;i<item.ChildCount;i++)pending.Push(item.GetChild(i));
            }
            return new(Eval(expression,0),[]);
        }
        catch(AffineFailure failure){return Failure(failure.Code,failure.Range);}

        AffineVoltageForm Eval(SpiceParser.ExpressionContext node,int depth)
        {
            if(++visited>1024||depth>64)throw new AffineFailure("expression_budget",Capture(node).Range);
            if(node.voltageReference() is {} reference)
            {
                var nodes=reference.referenceNode();
                if(nodes.Any(n=>n.WORD()==null))throw new AffineFailure("unsupported_behavior_node",Capture(reference).Range);
                var form=new AffineVoltageForm(0,[]);
                form=AddTerm(form,Capture(nodes[0]),1);
                return nodes.Length==2?AddTerm(form,Capture(nodes[1]),-1):form;
            }
            if(!HasVoltage(node))
            {
                var scalar=ParameterEvaluator.EvaluateValue(catalog,environment,Capture(node));
                if(scalar.Value is not {} value)throw new AffineFailure(scalar.Diagnostics[0].Code,scalar.Diagnostics[0].Range);
                return new(value,[]);
            }
            var children=node.expression();
            if(node.ChildCount==2&&children.Length==1)
                return node.GetChild(0).GetText() switch{"+"=>Eval(children[0],depth+1),"-"=>Scale(Eval(children[0],depth+1),-1,node),_=>throw new AffineFailure("unsupported_behavior_expression",Capture(node).Range)};
            if(node.ChildCount==3&&node.GetChild(0).GetText() is "(" or "{")return Eval(children[0],depth+1);
            if(children.Length!=2||node.ChildCount!=3)throw new AffineFailure("non_affine_behavior",Capture(node).Range);
            var left=Eval(children[0],depth+1);var right=Eval(children[1],depth+1);
            return node.GetChild(1).GetText() switch
            {
                "+"=>Combine(left,right,1,node),"-"=>Combine(left,right,-1,node),
                "*" when left.Terms.IsEmpty=>Scale(right,left.Offset,node),
                "*" when right.Terms.IsEmpty=>Scale(left,right.Offset,node),
                "/" when right.Terms.IsEmpty&&right.Offset!=0=>Scale(left,1/right.Offset,node),
                _=>throw new AffineFailure("non_affine_behavior",Capture(node).Range)
            };
        }
        bool HasVoltage(IParseTree node)
        {
            var stack=new Stack<IParseTree>();stack.Push(node);
            while(stack.TryPop(out var child))
            {
                if(child is SpiceParser.VoltageReferenceContext)return true;
                for(var i=0;i<child.ChildCount;i++)stack.Push(child.GetChild(i));
            }
            return false;
        }
        AffineVoltageForm AddTerm(AffineVoltageForm form,SourceSyntax node,double coefficient)
        {
            if(node.Text=="0")return form;
            var terms=form.Terms.ToDictionary(t=>t.Node.Text,StringComparer.OrdinalIgnoreCase);
            if(terms.TryGetValue(node.Text,out var previous))coefficient+=previous.Coefficient;
            if(!double.IsFinite(coefficient))throw new AffineFailure("nonfinite_behavior_coefficient",node.Range);
            if(coefficient==0)terms.Remove(node.Text);else terms[node.Text]=new(previous?.Node??node,coefficient);
            if(terms.Count>maxTerms)throw new AffineFailure("behavior_term_budget",node.Range);
            return form with{Terms=terms.Values.OrderBy(t=>t.Node.Text,StringComparer.OrdinalIgnoreCase).ToImmutableArray()};
        }
        AffineVoltageForm Combine(AffineVoltageForm left,AffineVoltageForm right,double sign,SpiceParser.ExpressionContext node)
        {
            var offset=left.Offset+sign*right.Offset;
            if(!double.IsFinite(offset))throw new AffineFailure("nonfinite_behavior_offset",Capture(node).Range);
            var form=left with{Offset=offset};
            foreach(var term in right.Terms)form=AddTerm(form,term.Node,sign*term.Coefficient);
            return form;
        }
        AffineVoltageForm Scale(AffineVoltageForm form,double scalar,SpiceParser.ExpressionContext node)
        {
            var offset=form.Offset*scalar;
            if(!double.IsFinite(scalar)||!double.IsFinite(offset))throw new AffineFailure("nonfinite_behavior_offset",Capture(node).Range);
            var result=new AffineVoltageForm(offset,[]);
            foreach(var term in form.Terms)result=AddTerm(result,term.Node,term.Coefficient*scalar);
            return result;
        }
        SourceSyntax Capture(ParserRuleContext node)
        {
            var start=node.Start.StartIndex-prefix.Length;var length=node.Stop.StopIndex-node.Start.StartIndex+1;
            var line=syntax.Range.Line+node.Start.Line-2;
            var prefixColumn=prefix.Length-prefix.LastIndexOf('\n')-1;
            var column=node.Start.Line==2?syntax.Range.Column+node.Start.Column-prefixColumn:node.Start.Column+1;
            return new(syntax.Text.Substring(start,length),new(syntax.Range.Start+start,length,line,column));
        }
    }
    private sealed class AffineFailure(string code,SourceRange range):Exception
    {
        internal string Code{get;}=code;
        internal SourceRange Range{get;}=range;
    }
}
