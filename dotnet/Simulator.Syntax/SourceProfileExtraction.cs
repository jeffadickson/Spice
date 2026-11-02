using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

// Reuse generated tokens and a generated semantic entry, never a second lexer
// or a whitespace parser. Clones prevent the local stream from reindexing the
// deck's tokens; offsets and quoted/braced scalar syntax remain original.
internal static class SourceProfileExtraction
{
    internal static SourceExcitationSyntax? Extract(SpiceParser.SourceContext source,Func<ParserRuleContext,SourceSyntax> capture)
    {
        var tokens=new List<IToken>();
        foreach(var parameter in source.parameter())Visit(parameter);
        if(tokens.Count==0||tokens.Count>256)return null;
        tokens.Add(new CommonToken(TokenConstants.EOF));
        var parser=new SpiceParser(new CommonTokenStream(new ListTokenSource(tokens))){ErrorHandler=new BailErrorStrategy()};
        parser.RemoveErrorListeners();
        try
        {
            var profile=parser.sourceProfile();
            if(profile.value()?.GetText().ToLowerInvariant() is "ac" or "dc")return null;
            var dc=profile.dcExcitation()?.value()??profile.value();
            var ac=profile.acExcitation()?.value();
            return new(dc==null?null:capture(dc),ac==null?null:capture(ac[0]),ac?.Length==2?capture(ac[1]):null);
        }
        catch(ParseCanceledException){return null;}
        void Visit(IParseTree node)
        {
            if(tokens.Count>256)return;
            if(node is ITerminalNode terminal){tokens.Add(new CommonToken(terminal.Symbol));return;}
            for(var i=0;i<node.ChildCount;i++)Visit(node.GetChild(i));
        }
    }
}
