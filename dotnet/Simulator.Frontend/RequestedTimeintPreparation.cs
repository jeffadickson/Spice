using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;

// Generated whole-card metadata is evaluated once in the root parameter frame.
// This adapter owns numerical policy only; it does not scan netlist text.
internal static class RequestedTimeintPreparation
{
    internal sealed record Settings(double? Amps,double? Relative,double? MaxStep,bool Fixed,
        ImmutableHashSet<int> Handled,BridgeDiagnostic? Error);
    internal static Settings Prepare(DeclarationCatalog catalog,bool transientOnly,bool hasLegacyAbstol,double temperatureCelsius)
    {
        var cards=catalog.Cards.Where(c=>!c.TimeintOptions.IsEmpty).ToArray();
        Settings Fail(string code,SourceRange range)=>new(null,null,null,false,[],new(code,range));
        if(cards.Length==0)return new(null,null,null,false,[],null);
        if(!transientOnly||cards.Any(c=>c.ScopeId!=0))return Fail("unsupported_timeint_scope",cards[0].Range);
        var environment=ParameterEvaluator.EvaluateAtTemperature(catalog,temperatureCelsius);
        var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var card in cards)foreach(var item in card.TimeintOptions)
        {
            var name=item.Name!;
            if(values.ContainsKey(name)||hasLegacyAbstol&&name.Equals("abstol",StringComparison.OrdinalIgnoreCase))
                return Fail("ambiguous_timeint_option",item.Range);
            var evaluated=ParameterEvaluator.EvaluateValue(catalog,environment,item.Value);
            if(evaluated.Value is not {} value||!double.IsFinite(value)||value<=0
                ||name.Equals("conststep",StringComparison.OrdinalIgnoreCase)&&value!=1)
                return Fail("invalid_timeint_option",item.Range);
            values.Add(name,value);
        }
        double? Get(string key)=>values.TryGetValue(key,out var value)?value:null;
        return new(Get("abstol"),Get("reltol"),Get("delmax"),values.ContainsKey("conststep"),
            cards.Select(c=>c.Range.Start).ToImmutableHashSet(),null);
    }
}
