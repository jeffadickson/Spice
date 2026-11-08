using System.Collections.Immutable;
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
        var parser=CreateParser(source.parameter());
        if(parser==null)return null;
        try
        {
            var profile=parser.sourceProfile();
            if(profile.value() is {} bare&&IsReserved(bare.GetText()))return null;
            var clauses=profile.sourceClause();
            var dc=clauses.Select(c=>c.dcExcitation()).Where(c=>c!=null).ToArray();
            var bareDc=clauses.Select(c=>c.barePwlDc()).Where(c=>c!=null).ToArray();
            var ac=clauses.Select(c=>c.acExcitation()).Where(c=>c!=null).ToArray();
            var waves=clauses.Select(c=>c.waveformExcitation()).Where(c=>c!=null).ToArray();
            var grouped=clauses.Select(c=>c.groupedPwlExcitation()).Where(c=>c!=null).ToArray();
            var dcCount=dc.Length+bareDc.Length;
            if(dcCount>1||ac.Length>1||waves.Length+grouped.Length>1||(dcCount>0&&profile.value()!=null))return null;
            var nominal=dc.Length==1?dc[0].value():profile.value();var phasor=ac.Length==1?ac[0].value():null;
            var distortion=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var clause in clauses)
                if(clause.distortionExcitation() is {} marker &&
                    (marker.NUMBER().GetText()!="0" || !distortion.Add(marker.identifier().GetText())))
                    return null;
            // Admit only disabled distortion markers on an otherwise valid
            // source; a nonzero coefficient or phase tail stays a refusal.
            if(distortion.Count>0&&nominal==null&&phasor==null&&waves.Length+grouped.Length==0)return null;
            SourceWaveformSyntax? waveform=null;
            if(waves.Length==1){
                var wave=waves[0];var kind=wave.identifier().GetText().ToLowerInvariant() switch{"pulse"=>SourceWaveformKind.Pulse,"sin"=>SourceWaveformKind.Sine,"pwl"=>SourceWaveformKind.Pwl,"sffm"=>SourceWaveformKind.FrequencyModulated,"am"=>SourceWaveformKind.AmplitudeModulated,_=>SourceWaveformKind.Exponential};
                waveform=new(kind,wave.waveformValues().waveformScalar().Select(v=>capture(v.value())).ToImmutableArray());
            }
            if(grouped.Length==1)
                waveform=new(SourceWaveformKind.Pwl,grouped[0].pwlPair().SelectMany(p=>p.waveformScalar()).Select(v=>capture(v.value())).ToImmutableArray());
            var dcValue=bareDc.Length==1?new SourceSyntax("0",capture(bareDc[0]).Range):nominal==null?null:capture(nominal);
            return new(dcValue,phasor==null?null:capture(phasor[0]),phasor?.Length==2?capture(phasor[1]):null){Waveform=waveform};
        }
        catch(ParseCanceledException){return null;}
    }
    internal static bool IsReserved(string text)=>text.ToLowerInvariant() is "dc" or "ac" or "pulse" or "sin" or "pwl" or "exp" or "sffm" or "am" or "distof1" or "distof2";
    // Shared bounded adapter for generated semantic entries. Recognition remains
    // in ANTLR; no source text is split or re-tokenized here.
    internal static SpiceParser? CreateParser(IEnumerable<ParserRuleContext> parameters)
    {
        var tokens=new List<IToken>();
        foreach(var parameter in parameters)Visit(parameter);
        if(tokens.Count==0||tokens.Count>256)return null;
        tokens.Add(new CommonToken(TokenConstants.EOF));
        var parser=new SpiceParser(new CommonTokenStream(new ListTokenSource(tokens))){ErrorHandler=new BailErrorStrategy()};
        parser.RemoveErrorListeners();
        return parser;
        void Visit(IParseTree node)
        {
            if(tokens.Count>256)return;
            if(node is ITerminalNode terminal){tokens.Add(new CommonToken(terminal.Symbol));return;}
            for(var i=0;i<node.ChildCount;i++)Visit(node.GetChild(i));
        }
    }
}
