using System.Collections.Immutable;
using Antlr4.Runtime;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;
/// <summary>Generated two-terminal primitive metadata, retaining unevaluated source syntax.</summary>
public sealed record PrimitiveSyntax(string Name,string Family,int ScopeId,SourceSyntax P,SourceSyntax N,SourceSyntax? Value,bool Supported,SourceRange Range)
{
    /// <summary>Separately generated DC/AC clauses; null means outside this bounded excitation profile.</summary>
    public SourceExcitationSyntax? Excitation { get; init; }
    /// <summary>A leading reserved DC/AC token must satisfy the bounded clause grammar rather than become a same-named scalar parameter.</summary>
    public bool RequiresExcitationProfile { get; init; }
}
/// <summary>Unevaluated SI DC and AC amplitude/degree-phase fields. A null DC field in an AC-only header denotes the specified zero DC default.</summary>
public sealed record SourceExcitationSyntax(SourceSyntax? Dc,SourceSyntax? AcMagnitude,SourceSyntax? AcPhase);
/// <summary>Every generated statement's lexical owner, for explicit active-capability checks.</summary>
public sealed record ScopedCard(int ScopeId,string Kind,SourceRange Range)
{
    /// <summary>Original generated leading-token spelling; composite lexical headers may span more than one word.</summary>
    public string Name { get; init; } = "";
    /// <summary>True only for a nonempty generated .option(s) tail composed entirely of the qualified output flags acct/noacct/noinit.</summary>
    public bool ReportingOptionsOnly { get; init; }
}
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
        return new(card.Start.Text,family,scope,capture(nodes.node(0)),capture(nodes.node(1)),value,supported,capture(card).Range)
        {RequiresExcitationProfile=card is SpiceParser.SourceContext&&parameters.Length>0&&parameters[0].Start.Text.ToLowerInvariant() is "dc" or "ac",
         Excitation=card is SpiceParser.SourceContext sourceCard
            ?supported&&value!=null&&!parameters[0].Start.Text.Equals("ac",StringComparison.OrdinalIgnoreCase)
                &&!(parameters.Length==1&&parameters[0].GetText().Equals("dc",StringComparison.OrdinalIgnoreCase))
                ?new(value,null,null):SourceProfileExtraction.Extract(sourceCard,capture)
            :null};
    }
}
