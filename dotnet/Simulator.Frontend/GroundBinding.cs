using System.Text.RegularExpressions;
using Simulator.Syntax;
namespace Simulator.Frontend;

// Immutable semantic context over generated metadata. Kept outside the MNA
// emitter so ground policy has no matrix/provider or trial-state dependency.
internal sealed class GroundBinding(DeclarationCatalog catalog,GroundAliasPolicy policy,HashSet<int> protectedScopes)
{
    internal GroundAliasPolicy Policy {get;}=policy;

    internal static GroundBinding? Prepare(DeclarationCatalog catalog,IReadOnlySet<int> reachable,bool requiresPspice,
        GroundAliasPolicy requested,out BridgeDiagnostic? diagnostic)
    {
        diagnostic=null;
        if(requiresPspice&&requested==GroundAliasPolicy.Ngspice){
            diagnostic=new("unsupported_ground_dialect_combination",catalog.Scopes[0].Range);return null;
        }
        var policy=requested==GroundAliasPolicy.Automatic?(requiresPspice?GroundAliasPolicy.Pspice:GroundAliasPolicy.Ngspice):requested;
        var protectedScopes=catalog.Symbols.Where(s=>s.BodyScopeId!=null&&
            (HeaderGround(s.Name)||s.HeaderArguments.Any(p=>HeaderGround(p.Text))))
            .Select(s=>s.BodyScopeId!.Value).ToHashSet();
        // A protected body's explicit global gnd has a different namespace
        // from root-normalized zero. Refuse that interaction until qualified.
        var unqualified=catalog.Cards.FirstOrDefault(c=>policy==GroundAliasPolicy.Pspice&&reachable.Contains(c.ScopeId)&&
            protectedScopes.Contains(c.ScopeId)&&c.GlobalNodes?.Any(n=>n.Text.Equals("gnd",StringComparison.OrdinalIgnoreCase))==true);
        if(unqualified!=null){diagnostic=new("unsupported_ground_global_context",unqualified?.Range);return null;}
        return new(catalog,policy,protectedScopes);
    }

    internal bool IsGround(string name,int scope)=>name=="0"||name.Equals("gnd",StringComparison.OrdinalIgnoreCase)&&
        Policy!=GroundAliasPolicy.Disabled&&!(Policy==GroundAliasPolicy.Pspice&&protectedScopes.Contains(scope));

    internal BridgeDiagnostic? CheckScope(int scope)=>Policy==GroundAliasPolicy.Pspice&&protectedScopes.Contains(scope)&&
        (catalog.Scopes[scope].ParentId!=0||catalog.Scopes.Any(s=>s.ParentId==scope))
        ?new("unsupported_ground_dialect_scope",catalog.Scopes[scope].Range):null;

    internal BridgeDiagnostic? CheckVector(XspiceVoltagePortSyntax vector,int scope)
    {
        foreach(var port in vector.Elements){
            if(port.IsVector){var failure=CheckVector(port,scope);if(failure!=null)return failure;continue;}
            foreach(var reference in new[]{port.P,port.N}.OfType<SourceSyntax>()){
                if(!reference.Text.Equals("gnd",StringComparison.OrdinalIgnoreCase)||!IsGround(reference.Text,scope))continue;
                // Native preprocessing recognizes whitespace, commas and
                // parentheses, but not adjacent square brackets. Ranges retain
                // that distinction without introducing another SPICE lexer.
                var offset=reference.Range.Start-vector.Original.Range.Start;
                var end=offset+reference.Range.Length;var text=vector.Original.Text;
                if(offset<=0||end>=text.Length||!(Space(text[offset-1])||text[offset-1] is '(' or ',')||
                    !(Space(text[end])||text[end] is ')' or ','))
                    return new("unsupported_ground_lexical_context",reference.Range);
            }
        }
        return null;
    }
    private static bool Space(char c)=>c is ' ' or '\t' or '\r' or '\n' or '\v' or '\f';
    // The native PSpice identifier-boundary rule applies to already parsed
    // header names/parameters, not just formal ports. Standard nonbacktracking
    // regex supplies bounded matching; ANTLR remains the SPICE parser.
    private static bool HeaderGround(string text)=>Regex.IsMatch(text,@"(?:^|[^A-Za-z0-9_])gnd(?:$|[^A-Za-z0-9_])",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.NonBacktracking);
}
