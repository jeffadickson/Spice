using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Simulator.Syntax.Generated;

namespace Simulator.Syntax;

/// <summary>A physical source range; offsets are UTF-16 indexes into the original text.</summary>
public sealed record SourceRange(int Start, int Length, int Line, int Column);
/// <summary>An immutable statement, retaining source spelling without numerical interpretation.</summary>
public sealed record SyntaxStatement(string Kind, string Text, SourceRange Range);
/// <summary>A bounded diagnostic; a grammar gap is not proof the input is invalid.</summary>
public sealed record SyntaxDiagnostic(string Code, string Message, int Line, int Column);
/// <summary>Syntax-only outcome, independent of any simulator capability.</summary>
public sealed record DeckSyntax(string Status, string Title,
    System.Collections.Immutable.ImmutableArray<SyntaxStatement> Statements,
    System.Collections.Immutable.ImmutableArray<SyntaxDiagnostic> Diagnostics,string Format="deck")
{
    /// <summary>Opt-in syntactic observations; empty on rejected input. These do not establish execution support.</summary>
    public ImmutableArray<SyntaxFeature> Features { get; init; } = [];
    /// <summary>Opt-in declaration catalog, absent for rejected input or ordinary syntax-only calls.</summary>
    public DeclarationCatalog? Declarations { get; init; }
}

/// <summary>Generated-grammar frontend. It never executes cards or resolves external files.</summary>
public static class DeckParser
{
    // These directives have balanced structural tails, not complete semantic or
    // command-specific validation. Unknown dot directives remain explicit gaps.
    private static readonly HashSet<string> StructuralDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        ".print", ".plot", ".probe", ".save", ".meas", ".measure", ".options", ".option",
        ".temp", ".global", ".lib", ".endl", ".func", ".ic", ".nodeset", ".if", ".elseif",
        ".else", ".endif", ".csparam", ".title", ".backanno", ".four", ".tf", ".noise",
        ".pz", ".sens", ".disto", ".width", ".savebias", ".loadbias", ".sp", ".sparam", ".sndparam", ".sndprint", ".opt", ".endc"
    };

    /// <summary>Recognizes a complete titled SPICE deck under the declared bounded grammar.</summary>
    public static DeckSyntax Parse(string text,ParserLimits? limits=null,bool collectFeatures=false,bool collectDeclarations=false)
    {
        ArgumentNullException.ThrowIfNull(text);
        limits??=ParserLimits.Default;
        if (text.Length > limits.MaxInputCharacters) return Resource(FormattableString.Invariant($"Input exceeds {limits.MaxInputCharacters:N0} UTF-16 characters"));
        var errors = new List<SyntaxDiagnostic>();
        var positions = new PhysicalPositions(text);
        var lexer = new SpiceLexer(new AntlrInputStream(text.EndsWith('\n') || text.EndsWith('\r') ? text : text + "\n"));
        lexer.RemoveErrorListeners();
        var tokens = new CommonTokenStream(lexer);
        tokens.Fill();
        if (tokens.Size > limits.MaxTokens) return Resource(FormattableString.Invariant($"Input exceeds {limits.MaxTokens:N0} tokens including trivia"));
        // Inspect generated tokens, never raw characters: braces in strings,
        // titles and control commands must not affect the nesting guard.
        var depth = 0;
        var hierarchy = 0;
        var unary = 0;
        foreach (var token in tokens.GetTokens())
        {
            if(token.Channel!=TokenConstants.DefaultChannel)continue;
            // Group limits are per logical record. Legacy model-card outer
            // separators need not balance; continuation newlines are hidden.
            if(token.Type==SpiceLexer.NL)depth=0;
            unary=token.Type is SpiceLexer.PLUS or SpiceLexer.MINUS or SpiceLexer.BANG?unary+1:0;
            if(unary>128)return Resource("Unary recursion exceeds 128 operators");
            if(token.Type==SpiceLexer.SUBCKT && ++hierarchy>128)return Resource("Subcircuit recursion exceeds 128 blocks");
            if(token.Type==SpiceLexer.ENDS)hierarchy=Math.Max(0,hierarchy-1);
            if (token.Type is SpiceLexer.LPAREN or SpiceLexer.LBRACE or SpiceLexer.LBRACKET or SpiceLexer.EXPR_VREF_OPEN)
            {
                if (++depth > 128) return Resource("Nesting exceeds 128 groups");
            }
            else if (token.Type is SpiceLexer.RPAREN or SpiceLexer.RBRACE or SpiceLexer.RBRACKET) depth=Math.Max(0,depth-1);
        }
        var parser = Recognizer(limits.UseSllFirst);
        SpiceParser.DeckContext tree;
        try { tree=parser.deck(); }
        catch(Exception e) when(limits.UseSllFirst && e is ParseCanceledException or SyntaxDepthException)
        {
            // ANTLR's standard two-stage strategy: SLL never recovers partial
            // input. A failed attempt is discarded; LL owns final diagnostics.
            errors.Clear();tokens.Seek(0);parser=Recognizer(false);
            try { tree=parser.deck(); }
            catch(SyntaxDepthException) { return Resource("Generated parser rule depth exceeds 512"); }
        }
        catch(SyntaxDepthException) { return Resource("Generated parser rule depth exceeds 512"); }
        var statements = ImmutableArray.CreateBuilder<SyntaxStatement>();
        var features = ImmutableArray.CreateBuilder<SyntaxFeature>();
        // Recovered trees can contain incomplete rule contexts. Metadata is
        // derived only after recognition has succeeded without recovery errors.
        var declarations=collectDeclarations&&parser.NumberOfSyntaxErrors==0?new DeclarationExtraction(text,positions.At):null;
        var pending = new Stack<IParseTree>();
        pending.Push(tree);
        var gap = false;
        while (pending.TryPop(out var item))
        {
            if(collectFeatures&&parser.NumberOfSyntaxErrors==0) FeatureExtraction.Observe(item,features,positions.At,text.Length);
            declarations?.Observe(item);
            if (item is SpiceParser.StatementContext statement && statement.GetChild(0) is ParserRuleContext card)
            {
                var kind = SpiceParser.ruleNames[card.RuleIndex];
                var start = Math.Clamp(card.Start.StartIndex, 0, text.Length);
                var end = Math.Clamp(card.Stop?.StopIndex + 1 ?? start, start, text.Length);
                // Container statements include their closing newline in the
                // generated range; retain that spelling rather than rebuilding.
                var (line,column)=positions.At(start);
                statements.Add(new(kind, text[start..end], new(start, end-start, line, column)));
                if (card is SpiceParser.ExtensionContext extension &&
                    !StructuralDirectives.Contains(extension.Start.Text))
                {
                    gap = true;
                    if (errors.Count < 32) errors.Add(new("grammar_gap", $"Unspecified directive {extension.Start.Text}",line,column));
                }
                if (card is SpiceParser.UnknownDeviceContext)
                {
                    gap=true;
                    if(errors.Count<32) errors.Add(new("grammar_gap",$"Unspecified device family {card.Start.Text}",line,column));
                }
            }
            if(item is SpiceParser.ScriptLineContext scriptLine && scriptLine.COMMAND_LINE() is { } command)
            {
                var token=command.Symbol;
                var start=Math.Clamp(token.StartIndex,0,text.Length);
                var end=Math.Clamp(token.StopIndex+1,start,text.Length);
                var (line,column)=positions.At(start);
                statements.Add(new("command",text[start..end],new(start,end-start,line,column)));
            }
            for (var i = item.ChildCount-1; i >= 0; i--) pending.Push(item.GetChild(i));
        }
        var title = (tree.TITLE()?.GetText() ?? tree.script()?.SCRIPT_TITLE()?.GetText() ?? "").TrimEnd('\r','\n');
        var status = parser.NumberOfSyntaxErrors > 0 ? "syntax_rejected" : gap ? "grammar_gap" : "accepted_structure";
        return new(status,title,statements.ToImmutable(),errors.ToImmutableArray(),tree.script() is null?"deck":"command_script")
        { Features=status=="accepted_structure"?features.ToImmutable():[],
          Declarations=status=="accepted_structure"?declarations?.Build():null };

        SpiceParser Recognizer(bool sll)
        {
            var recognizer=new SpiceParser(tokens);
            recognizer.RemoveErrorListeners();
            recognizer.AddErrorListener(new DiagnosticListener(errors,positions));
            recognizer.AddParseListener(new RuleDepthGuard());
            if(sll)
            {
                recognizer.Interpreter.PredictionMode=PredictionMode.SLL;
                recognizer.ErrorHandler=new BailErrorStrategy();
            }
            return recognizer;
        }
    }

    private static DeckSyntax Resource(string reason) => new("resource_rejected","",[],[new("resource_limit",reason,0,0)]);

    private sealed class DiagnosticListener(List<SyntaxDiagnostic> errors,PhysicalPositions positions) : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            var actual=positions.At(Math.Max(0,offendingSymbol.StartIndex));
            if (errors.Count < 32) errors.Add(new("syntax",msg.Length > 512 ? msg[..512] : msg,actual.Line,actual.Column));
        }
    }

    // Grammar recursion is broader than delimiters (unary operators and nested
    // subcircuits also recurse). ANTLR's rule-entry hook bounds every production,
    // so new recursive grammar rules inherit the safeguard automatically.
    private sealed class RuleDepthGuard : IParseTreeListener
    {
        private int depth;
        public void EnterEveryRule(ParserRuleContext context)
        {
            if(++depth>512)throw new SyntaxDepthException();
        }
        public void ExitEveryRule(ParserRuleContext context)=>depth--;
        public void VisitTerminal(ITerminalNode node) { }
        public void VisitErrorNode(IErrorNode node) { }
    }
    private sealed class SyntaxDepthException : Exception { }

    // ANTLR counts LF lines. SPICE also permits bare CR: map generated offsets
    // to physical source locations independently of newline normalization.
    private sealed class PhysicalPositions
    {
        private readonly int[] starts;
        public PhysicalPositions(string text)
        {
            var lines=new List<int>{0};
            for(var i=0;i<text.Length;i++)
            {
                if(text[i]=='\r')
                {
                    if(i+1<text.Length && text[i+1]=='\n')i++;
                    lines.Add(i+1);
                }
                else if(text[i]=='\n')lines.Add(i+1);
            }
            starts=lines.ToArray();
        }
        public (int Line,int Column) At(int offset)
        {
            var index=Array.BinarySearch(starts,offset);
            if(index<0)index=~index-1;
            return(index+1,offset-starts[index]+1);
        }
    }
}

