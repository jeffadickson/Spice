using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;

// An algorithm selector is not a reporting option. Eliminate its auxiliary
// zero-delay states only after checking the entire admitted physical profile.
internal static class RequestedExcessPhasePreparation
{
    internal sealed record Settings(ImmutableHashSet<int> Handled,ImmutableArray<OperatingPointOmission> Observations,BridgeDiagnostic? Error);
    internal static Settings Prepare(DeclarationCatalog catalog)
    {
        var cards=catalog.Cards.Where(c=>c.ZeroDelayExcessPhaseOption).ToArray();
        Settings Fail(string code,SourceRange range)=>new([],[],new(code,range));
        if(cards.Length==0)return new([],[],null);
        if(cards.Length!=1)return Fail("ambiguous_excess_phase_option",cards[1].Range);
        var card=cards[0];if(card.ScopeId!=0)return Fail("unsupported_excess_phase_scope",card.Range);
        // The admitted BJT contract has no authored PTF. TF alone is charge
        // storage, not excess phase: delay = TF*PTF*pi/180 is identically zero.
        // Refuse even an explicit PTF=0 until that model-field profile is added;
        // never silently discard a possibly parameterized nonzero phase.
        if(catalog.Symbols.Where(s=>s.ModelFamily is "npn" or "pnp")
            .Any(s=>s.HasOpaqueModelTerms||s.Parameters.Any(p=>p.Name?.Equals("ptf",StringComparison.OrdinalIgnoreCase)==true)))
            return Fail("unsupported_excess_phase_delay",card.Range);
        return new([card.Range.Start],[new(card.ScopeId,card.Kind,card.Name,"zero_delay_excess_phase_equivalence",card.Range)],null);
    }
}
