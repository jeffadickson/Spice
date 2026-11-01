using System.Collections.Immutable;
using Antlr4.Runtime;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;
/// <summary>Generated two-terminal primitive metadata, retaining unevaluated source syntax.</summary>
public sealed record PrimitiveSyntax(string Name,string Family,int ScopeId,SourceSyntax P,SourceSyntax N,SourceSyntax? Value,bool Supported,SourceRange Range);
/// <summary>Every generated statement's lexical owner, for explicit active-capability checks.</summary>
public sealed record ScopedCard(int ScopeId,string Kind,SourceRange Range);
internal static class PrimitiveExtraction
{
    internal static PrimitiveSyntax? Extract(ParserRuleContext card,int scope,Func<ParserRuleContext,SourceSyntax> capture,Func<int,int,SourceSyntax> captureRange)
    {
        SpiceParser.TwoNodesContext nodes;SpiceParser.ParameterContext[] parameters;
        if(card is SpiceParser.PassiveContext passive){nodes=passive.twoNodes();parameters=passive.parameter();}
        else if(card is SpiceParser.SourceContext source){nodes=source.twoNodes();parameters=source.parameter();}
        else return null;
        var family=card.Start.Text[..1].ToUpperInvariant();
        // The general grammar represents "DC -2" as arithmetic (DC minus 2).
        // Interpret the source-only DC keyword using generated operator tokens;
        // retain the signed physical slice for the existing scalar evaluator.
        if(card is SpiceParser.SourceContext&&parameters.Length==1&&parameters[0].arithmetic() is {} arithmetic
            &&arithmetic.value()?.GetText().Equals("dc",StringComparison.OrdinalIgnoreCase)==true
            &&(arithmetic.PLUS()!=null||arithmetic.MINUS()!=null))
        {
            var sign=arithmetic.PLUS()??arithmetic.MINUS();
            return new(card.Start.Text,family,scope,capture(nodes.node(0)),capture(nodes.node(1)),
                captureRange(sign.Symbol.StartIndex,arithmetic.Stop.StopIndex),true,capture(card).Range);
        }
        var valueIndex=card is SpiceParser.SourceContext&&parameters.Length>0&&parameters[0].GetText().Equals("dc",StringComparison.OrdinalIgnoreCase)?1:0;
        var supported=parameters.Length==valueIndex+1&&parameters[valueIndex].assignment()==null&&!(card is SpiceParser.SourceContext&&parameters[valueIndex].call()!=null);
        var value=supported?capture(parameters[valueIndex]):null;
        return new(card.Start.Text,family,scope,capture(nodes.node(0)),capture(nodes.node(1)),value,supported,capture(card).Range);
    }
}
