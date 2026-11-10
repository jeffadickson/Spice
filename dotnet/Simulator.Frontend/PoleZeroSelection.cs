using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;

namespace Simulator.Frontend;

/// <summary>Finite transfer roots in inverse seconds for a bounded pole-zero analysis.</summary>
public sealed record PoleZeroResult(ImmutableArray<double> Poles, ImmutableArray<double> Zeros,
    BridgeDiagnostic? Diagnostic)
{
    /// <summary>Whether the whole circuit fits the supported transfer function.</summary>
    public bool Success => Diagnostic is null;
}

/// <summary>Exact transfer roots for a voltage-driven first-order RC low-pass section.</summary>
public static class PoleZeroSelection
{
    /// <summary>Evaluate one root .pz voltage-transfer request in poles, zeros, or both mode.</summary>
    public static PoleZeroResult Analyze(DeckSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        PoleZeroResult Fail(string code, SourceRange? range = null) => new([], [], new(code, range));
        if (syntax is not { Status: "accepted_structure", Declarations: { } catalog })
            return Fail("syntax_not_accepted");

        var cards = catalog.Cards.Where(c => c.Name.Equals(".pz", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (cards.Length != 1 || cards[0].ScopeId != 0) return Fail("expected_one_root_pz");
        var card = cards[0];
        var statement = syntax.Statements.FirstOrDefault(s => s.Range.Start == card.Range.Start);
        var tokens = statement?.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens is not { Length: 7 } ||
            !tokens[5].Equals("vol", StringComparison.OrdinalIgnoreCase) ||
            tokens[6].ToLowerInvariant() is not ("pol" or "zer" or "pz"))
            return Fail("unsupported_pz_directive", card.Range);
        if (catalog.Cards.Any(c => c.Kind is "control" or "op" or "ac" or "dc" or "tran" ||
            c.Range.Start != card.Range.Start && OperatingPointSelection.OmissionReason(c) == "analysis_not_selected"))
            return Fail("unsupported_pz_program", card.Range);

        var circuit = OperatingPointSelection.Select(syntax).Prepared;
        if (!circuit.Success) return new([], [], circuit.Diagnostics[0]);
        if (!circuit.AcResistances.IsEmpty) return Fail("unsupported_pz_ac_resistance", card.Range);
        var request = circuit.Request!;
        var nodes = request.Circuit.Nodes;
        int Node(string name) => name == "0" ? 0 :
            circuit.RootNodes.TryGetValue(name, out var wire) ? Array.IndexOf(nodes, wire) : -1;
        var input = Node(tokens[1]);
        var output = Node(tokens[3]);
        if (input <= 0 || output <= 0 || input == output || Node(tokens[2]) != 0 || Node(tokens[4]) != 0)
            return Fail("unsupported_pz_ports", card.Range);

        var elements = request.Circuit.Elements;
        var resistors = elements.OfType<Resistor>().ToArray();
        var capacitors = elements.OfType<Capacitor>().ToArray();
        var sources = elements.OfType<Voltage>().ToArray();
        if (nodes.Length != 3 || elements.Length != 3 || resistors.Length != 1 ||
            capacitors.Length != 1 || sources.Length != 1 ||
            !Between(resistors[0], input, output) || !Between(capacitors[0], output, 0) ||
            !Between(sources[0], input, 0))
            return Fail("unsupported_pz_topology", card.Range);

        // H(s) = 1/(1+sRC). Its finite zero set is empty.
        var pole = -1 / (resistors[0].Ohms * capacitors[0].Farads);
        if (!double.IsFinite(pole) || pole >= 0) return Fail("invalid_pz_time_constant", card.Range);
        return new(tokens[6].Equals("zer", StringComparison.OrdinalIgnoreCase) ? [] : [pole], [], null);
    }

    private static bool Between(Element e, int a, int b) =>
        (e.P == a && e.N == b) || (e.P == b && e.N == a);
}
