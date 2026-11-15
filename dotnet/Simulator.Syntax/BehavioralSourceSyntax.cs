using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

/// <summary>Output relation: voltage P-minus-N or current flowing P to N.</summary>
public enum BehavioralQuantity
{
    /// <summary>Ideal imposed output voltage, volts.</summary>
    Voltage,
    /// <summary>Imposed output current flowing P to N, amperes.</summary>
    Current
}
/// <summary>Generated behavioral-source metadata, including G CUR aliases, retaining physical ranges and lexical ownership. Unsupported tails remain explicit.</summary>
public sealed record BehavioralSourceSyntax(string Name,int ScopeId,SourceSyntax? P,SourceSyntax? N,BehavioralQuantity Quantity,SourceSyntax? Expression,bool Supported,SourceRange Range)
{
    /// <summary>True when the generated output-relation header is complete and each retained tail has typed assignment metadata; physical admission remains separate.</summary>
    public bool HeaderSupported { get; init; }
    /// <summary>Generated additional attributes with original source ranges; strict affine preparation does not admit them.</summary>
    public ImmutableArray<ParameterSyntax> Parameters { get; init; } = [];
    /// <summary>Explicit multiplicity on a generated G CUR alias, evaluated in the source's lexical scope.</summary>
    public SourceSyntax? CurrentMultiplier { get; init; }
}

internal static class BehavioralSourceExtraction
{
    internal static BehavioralSourceSyntax? CurrentAlias(SpiceParser.VcontrolContext card,int scope,
        Func<ParserRuleContext,SourceSyntax> capture,Func<int,int,SourceSyntax> captureRange)
    {
        if(!card.Start.Text.StartsWith("g",StringComparison.OrdinalIgnoreCase)||card.tableTail()!=null)return null;
        var parser=SourceProfileExtraction.CreateParser(card.parameter());if(parser==null)return null;
        try {
            var profile=parser.currentAliasProfile();var value=profile.value();
            // The generated QUOTED token owns the delimiters. Preserve the
            // inner physical range; affine evaluation reuses the expression
            // grammar and never treats voltage references as scalar names.
            var expression=value.QUOTED() is {} quoted
                ?captureRange(quoted.Symbol.StartIndex+1,quoted.Symbol.StopIndex-1)
                :value.expression() is {} inner?capture(inner):capture(value);
            return new(card.Start.Text,scope,capture(card.twoNodes().node(0)),capture(card.twoNodes().node(1)),
                BehavioralQuantity.Current,expression,true,capture(card).Range)
            {HeaderSupported=true,CurrentMultiplier=profile.sourceMultiplicity() is {} multiplier?capture(multiplier.value()):null};
        }catch(ParseCanceledException){return null;}
    }

    internal static BehavioralSourceSyntax Extract(SpiceParser.BehaviorContext card,int scope,Func<ParserRuleContext,SourceSyntax> capture,Func<int,int,SourceSyntax> captureRange)
    {
        var range=capture(card).Range;var expression=card.expression();
        // A quoted expression is still a voltage-dependent law. Strip only
        // generated token delimiters and retain its physical inner range.
        SourceSyntax? law=expression==null?null:expression.QUOTED() is {} quoted
            ?captureRange(quoted.Symbol.StartIndex+1,quoted.Symbol.StopIndex-1):capture(expression);
        var quantity=card.identifier()?.GetText();var name=card.BEHAVIOR_NAME()?.GetText()??card.Start.Text;
        SourceSyntax? p=null,n=null;
        if(card.BEHAVIOR_START() is {} composite)
        {
            // The lexical flag suppresses composite B/.func tokens only in this
            // opt-in local pass. Default recognition is byte-for-byte unchanged.
            var token=composite.Symbol;
            var lexer=new SpiceLexer(new AntlrInputStream(token.Text)){SemanticHeaderOnly=true};
            lexer.Mode(SpiceLexer.BODY);lexer.RemoveErrorListeners();
            var parser=new SpiceParser(new CommonTokenStream(lexer)){ErrorHandler=new BailErrorStrategy()};parser.RemoveErrorListeners();
            try
            {
                var header=parser.behaviorHeaderProfile();name=header.BEHAVIOR_NAME().GetText();quantity=header.identifier().GetText();
                SourceSyntax CaptureLocal(ParserRuleContext context)=>captureRange(token.StartIndex+context.Start.StartIndex,token.StartIndex+context.Stop.StopIndex);
                p=CaptureLocal(header.twoNodes().node(0));n=CaptureLocal(header.twoNodes().node(1));
            }
            catch(ParseCanceledException){return new(name,scope,null,null,BehavioralQuantity.Voltage,law,false,range);}
        }
        else if(card.twoNodes() is {} nodes){p=capture(nodes.node(0));n=capture(nodes.node(1));}
        var recognized=quantity?.ToLowerInvariant() is "v" or "i";
        return new(name,scope,p,n,quantity?.Equals("i",StringComparison.OrdinalIgnoreCase)==true?BehavioralQuantity.Current:BehavioralQuantity.Voltage,
            law,recognized&&p!=null&&n!=null&&expression!=null&&card.parameter().Length==0,range)
        {
            HeaderSupported=recognized&&p!=null&&n!=null&&expression!=null&&card.parameter().All(item=>item.assignment()!=null),
            Parameters=card.parameter().Where(item=>item.assignment()!=null).Select(item=>
            {
                var assignment=item.assignment();
                var target=(ParserRuleContext)assignment.GetChild(0);
                var value=(ParserRuleContext)assignment.GetChild(2);
                return new ParameterSyntax(assignment.referenceName()?.GetText(),capture(target),capture(value),capture(assignment).Range);
            }).ToImmutableArray()
        };
    }
}
