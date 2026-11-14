using System.Collections.Immutable;
using Simulator.Syntax;

namespace Simulator.Frontend;

/// <summary>Explicit plot captures, distinct from live reads of restored source settings.</summary>
internal static class RequestedSourceDcCapture
{
    internal static ImmutableHashSet<string> Select(DeckSyntax syntax,ControlBlockSyntax block,int dispatchStart)
    {
        var selectors=ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        // Root .save cards configure analysis loading regardless of file order.
        foreach(var card in syntax.Declarations!.Cards.Where(c=>c.ScopeId==0&&c.Name.Equals(".save",StringComparison.OrdinalIgnoreCase)))
        {
            var statement=syntax.Statements.First(s=>s.Range.Start==card.Range.Start);
            selectors.UnionWith(Tokens(statement.Text).Skip(1));
        }
        // A control save must run before this dispatch. A later save cannot
        // reconstruct a vector in an already completed plot. "all" does not
        // imply device parameters, and no shell expansion or glob is inferred.
        foreach(var command in block.Commands.Where(c=>c.Name.Equals("save",StringComparison.OrdinalIgnoreCase)&&c.Range.Start<dispatchStart))
            selectors.UnionWith(Tokens(command.Argument??""));
        return selectors.ToImmutable();
    }

    private static IEnumerable<string> Tokens(string text)=>text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)
        .TakeWhile(token=>!token.StartsWith('$')).Select(token=>token.Trim('"','\''));
}
