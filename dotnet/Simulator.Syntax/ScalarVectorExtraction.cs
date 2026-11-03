using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

// Existing generated tokens are cloned by the common semantic-entry adapter;
// no textual splitting or independent array parser is introduced.
internal static class ScalarVectorExtraction
{
    internal static ImmutableArray<SourceSyntax>? Extract(SpiceParser.GroupContext group,Func<ParserRuleContext,SourceSyntax> capture)
    {
        if(group.LBRACKET()==null)return null;
        var parser=SourceProfileExtraction.CreateParser([group]);if(parser==null)return null;
        try{return parser.scalarVectorProfile().value().Select(v=>capture(v)).ToImmutableArray();}
        catch(ParseCanceledException){return null;}
    }
}
