using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Requested-only literal operating temperature; numerical model qualification remains explicit.</summary>
internal static class RequestedTemperaturePreparation
{
    internal sealed record Selection(BridgeLimits Limits,ImmutableHashSet<int> Handled,BridgeDiagnostic? Diagnostic);
    internal static Selection Prepare(DeclarationCatalog catalog,BridgeLimits? requestedLimits)
    {
        var limits=requestedLimits??new BridgeLimits();
        Selection Fail(string code,SourceRange range)=>new(limits,[],new(code,range));
        var cards=catalog.Cards.Where(c=>c.Name.Equals(".temp",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(cards.Length==0)return new(limits,[],null);
        if(cards.Length!=1)return Fail("ambiguous_operating_temperature",cards[1].Range);
        var card=cards[0];
        if(card.ScopeId!=0||card.OperatingTemperature is not {} syntax)
            return Fail("unsupported_operating_temperature_directive",card.Range);
        var value=ParameterEvaluator.EvaluateValue(catalog,ParameterEvaluator.EvaluateAtTemperature(catalog,27),syntax);
        if(value.Value is not {} celsius||!double.IsFinite(celsius)||celsius<=-273.15)
            return Fail("invalid_operating_temperature",syntax.Range);
        // Control exports and option precedence are separate contracts; do not
        // silently evaluate those under the former default Celsius environment.
        if(celsius!=27&&catalog.Cards.Any(c=>!c.ControlConstants.IsEmpty||c.DefaultTemperature&&c.Name!=card.Name))
            return Fail("conflicting_operating_temperature_profile",card.Range);
        return new(limits with{TemperatureCelsius=celsius},[card.Range.Start],null);
    }

    // Every admitted element is a temperature-independent typed linear stamp;
    // its already qualified producer resolved any temperature dependence first.
    internal static bool Supports(BridgeResult circuit)=>circuit.Request!.Circuit.Elements.All(e=>
        e is Resistor or Voltage or Current or Capacitor or Inductor or Vcvs or Vccs or Cccs or Ccvs);
}
