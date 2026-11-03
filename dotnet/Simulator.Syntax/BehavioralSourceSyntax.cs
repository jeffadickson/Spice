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
/// <summary>Generated B-card metadata, retaining physical ranges and lexical ownership. Unsupported tails remain explicitly unsupported.</summary>
public sealed record BehavioralSourceSyntax(string Name,int ScopeId,SourceSyntax? P,SourceSyntax? N,BehavioralQuantity Quantity,SourceSyntax? Expression,bool Supported,SourceRange Range);

internal static class BehavioralSourceExtraction
{
    internal static BehavioralSourceSyntax Extract(SpiceParser.BehaviorContext card,int scope,Func<ParserRuleContext,SourceSyntax> capture,Func<int,int,SourceSyntax> captureRange)
    {
        var range=capture(card).Range;var expression=card.expression();
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
            catch(ParseCanceledException){return new(name,scope,null,null,BehavioralQuantity.Voltage,expression==null?null:capture(expression),false,range);}
        }
        else if(card.twoNodes() is {} nodes){p=capture(nodes.node(0));n=capture(nodes.node(1));}
        var recognized=quantity?.ToLowerInvariant() is "v" or "i";
        return new(name,scope,p,n,quantity?.Equals("i",StringComparison.OrdinalIgnoreCase)==true?BehavioralQuantity.Current:BehavioralQuantity.Voltage,
            expression==null?null:capture(expression),recognized&&p!=null&&n!=null&&expression!=null&&card.parameter().Length==0,range);
    }
}
