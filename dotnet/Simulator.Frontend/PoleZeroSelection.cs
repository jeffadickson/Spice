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
    /// <summary>Validated DC circuit and original observable mapping used by this PZ analysis; AC overrides remain separate.</summary>
    public BridgeResult? CircuitPreparation { get; init; }
}

/// <summary>Exact real transfer roots for bounded RC and RL sections.</summary>
public static class PoleZeroSelection
{
    /// <summary>Evaluate one root .pz request for a supported grounded transfer topology.</summary>
    public static PoleZeroResult Analyze(DeckSyntax syntax)
    {
        var result=AnalyzeCore(syntax,out var circuit);
        return result.Success ? result with { CircuitPreparation=circuit } : result;
    }

    private static PoleZeroResult AnalyzeCore(DeckSyntax syntax,out BridgeResult? preparation)
    {
        preparation=null;
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
            tokens[5].ToLowerInvariant() is not ("vol" or "cur") ||
            tokens[6].ToLowerInvariant() is not ("pol" or "zer" or "pz"))
            return Fail("unsupported_pz_directive", card.Range);
        if (catalog.Cards.Any(c => c.Kind is "control" or "ac" or "dc" or "tran" ||
            c.Range.Start != card.Range.Start && OperatingPointSelection.OmissionReason(c) == "analysis_not_selected"))
            return Fail("unsupported_pz_program", card.Range);

        // PZ is an explicitly requested small-signal analysis. Admit the same
        // validated passive variants as requested AC, retaining the DC values
        // in preparation and replacing them only in this local transfer view.
        var circuit = OperatingPointSelection.SelectRequested(syntax,null,"lu",[]).Prepared;
        if (!circuit.Success) return new([], [], circuit.Diagnostics[0]);
        preparation=circuit;
        return AnalyzePrepared(circuit,tokens,card.Range);
    }

    internal static PoleZeroResult AnalyzeRequested(BridgeResult circuit,AnalysisSyntax directive)
    {
        if(directive.Kind!="pz"||directive.Names.Length!=6||
            directive.Names[4].ToLowerInvariant() is not ("vol" or "cur")||
            directive.Names[5].ToLowerInvariant() is not ("pol" or "zer" or "pz"))
            return new([],[],new("unsupported_pz_directive",directive.Range));
        var result=AnalyzePrepared(circuit,new[]{"pz"}.Concat(directive.Names).ToArray(),directive.Range);
        return result.Success?result with{CircuitPreparation=circuit}:result;
    }

    private static PoleZeroResult AnalyzePrepared(BridgeResult circuit,string[] tokens,SourceRange range)
    {
        PoleZeroResult Fail(string code,SourceRange? location=null)=>new([],[],new(code,location??range));
        var request = circuit.Request!;
        var nodes = request.Circuit.Nodes;
        int Node(string name) => name == "0" ? 0 :
            circuit.RootNodes.TryGetValue(name, out var wire) ? Array.IndexOf(nodes, wire) : -1;
        var input = Node(tokens[1]);
        var output = Node(tokens[3]);
        if (input <= 0 || output <= 0 || input == output || Node(tokens[2]) != 0 || Node(tokens[4]) != 0)
            return Fail("unsupported_pz_ports", range);

        var elements = request.Circuit.Elements.Select(e =>
            circuit.AcResistances.TryGetValue(e.Id,out var ohms)
                ? new Resistor(e.Id,e.P,e.N,ohms) : e).ToArray();
        var currentDriven = tokens[5].Equals("cur", StringComparison.OrdinalIgnoreCase);
        var requested = tokens[6].ToLowerInvariant();
        if ((!currentDriven || requested == "pol") &&
            TryCascade(elements, input, output, currentDriven) is { IsDefault: false } cascade)
            return new(tokens[6].Equals("zer", StringComparison.OrdinalIgnoreCase) ? [] : cascade, [], null);
        if (currentDriven)
        {
            // With current injected into the first shunt and no output load
            // beyond the second shunt, Z21 = s*C*Rin*Rout/(1+s*C*(Rin+Rout)).
            if (nodes.Length != 3 || elements.Length != 3 ||
                elements.OfType<Resistor>().FirstOrDefault(e => Between(e, input, 0)) is not { } inputShunt ||
                elements.OfType<Resistor>().FirstOrDefault(e => Between(e, output, 0)) is not { } outputShunt ||
                elements.OfType<Capacitor>().FirstOrDefault(e => Between(e, input, output)) is not { } coupling)
                return Fail("unsupported_pz_topology", range);
            var timeConstant = inputShunt.Ohms * coupling.Farads + outputShunt.Ohms * coupling.Farads;
            var highPassPole = -1 / timeConstant;
            if (!double.IsFinite(highPassPole) || highPassPole >= 0)
                return Fail("invalid_pz_time_constant", range);
            return new(requested == "zer" ? [] : [highPassPole], requested == "pol" ? [] : [0], null);
        }

        var resistors = elements.OfType<Resistor>().ToArray();
        var capacitors = elements.OfType<Capacitor>().ToArray();
        var sources = elements.OfType<Voltage>().ToArray();
        if (nodes.Length == 4 && elements.Length == 5 && resistors.Length == 2 &&
            capacitors.Length == 2 && sources.Length == 1 && Between(sources[0], input, 0))
        {
            var mid = Enumerable.Range(1, 3).Single(n => n != input && n != output);
            var rGround = resistors.FirstOrDefault(r => Between(r, mid, 0));
            var rBridge = resistors.FirstOrDefault(r => Between(r, input, output));
            var cInput = capacitors.FirstOrDefault(c => Between(c, input, mid));
            var cOutput = capacitors.FirstOrDefault(c => Between(c, mid, output));
            if (rGround is null || rBridge is null || cInput is null || cOutput is null)
                return Fail("unsupported_pz_topology", range);

            // The two-node determinant and Cramer's numerator have the same
            // constant and quadratic terms. The extra denominator term is
            // gGround*s*cOutput. Divide both polynomials by cInput*cOutput.
            var gGround = 1 / rGround.Ohms;
            var gBridge = 1 / rBridge.Ohms;
            var zeroLinear = gBridge / cInput.Farads + gBridge / cOutput.Farads;
            var groundRate = gGround / cInput.Farads;
            var bridgeRate = gBridge / cOutput.Farads;
            var poleLinear = zeroLinear + groundRate;
            var mode = tokens[6].ToLowerInvariant();
            var poles = mode == "zer" ? [] : RealRoots(poleLinear, groundRate, bridgeRate);
            var zeros = mode == "pol" ? [] : RealRoots(zeroLinear, groundRate, bridgeRate);
            return poles.IsDefault || zeros.IsDefault
                ? Fail("unsupported_pz_root_domain", range)
                : new(poles, zeros, null);
        }
        var series = resistors.Where(r => Between(r,input,output)).ToArray();
        var shunts = resistors.Where(r => Between(r,output,0)).ToArray();
        if (nodes.Length != 3 || elements.Length is not (3 or 4) ||
            elements.Length != resistors.Length+capacitors.Length+sources.Length ||
            resistors.Length is not (1 or 2) || series.Length != 1 ||
            shunts.Length != resistors.Length-1 ||
            capacitors.Length != 1 || sources.Length != 1 ||
            !Between(capacitors[0], output, 0) ||
            !Between(sources[0], input, 0))
            return Fail("unsupported_pz_topology", range);

        // KCL gives H(s)=Gseries/(Gseries+Gshunt+sC). The shunt
        // changes the DC gain and pole, but introduces no finite zero.
        var conductance = 1/series[0].Ohms + (shunts.Length==0 ? 0 : 1/shunts[0].Ohms);
        var pole = -conductance/capacitors[0].Farads;
        if (!double.IsFinite(pole) || pole >= 0) return Fail("invalid_pz_time_constant", range);
        return new(tokens[6].Equals("zer", StringComparison.OrdinalIgnoreCase) ? [] : [pole], [], null);
    }

    // Consume every element of an isolated cascade. Any extra load, feedback
    // path, reused signal node or ambiguous parallel part invalidates the factorization.
    private static ImmutableArray<double> TryCascade(Element[] elements, int input, int output, bool currentDriven)
    {
        var remaining = elements.ToList();
        var visited = new HashSet<int> { input };
        var poles = ImmutableArray.CreateBuilder<double>();
        if (currentDriven ? Take<Current>(e => Between(e, input, 0)) is null :
            Take<Voltage>(e => Between(e, input, 0)) is null)
            return default;
        var node = input;
        while (poles.Count < 4)
        {
            double pole;
            if (currentDriven)
            {
                var resistor = Take<Resistor>(e => Between(e, node, 0));
                var inductor = Take<Inductor>(e => Between(e, node, 0));
                if (resistor is null || inductor is null) return default;
                // Z(R || L) = sL / (1 + sL/R).
                pole = -resistor.Ohms / inductor.Henries;
            }
            else
            {
                var resistor = Take<Resistor>(e => e.P == node || e.N == node);
                if (resistor is null) return default;
                node = resistor.P == node ? resistor.N : resistor.P;
                if (node == 0 || !visited.Add(node)) return default;
                var capacitor = Take<Capacitor>(e => Between(e, node, 0));
                if (capacitor is null) return default;
                // An ideal voltage buffer presents no load to the preceding RC.
                pole = -1 / (resistor.Ohms * capacitor.Farads);
            }
            if (!double.IsFinite(pole) || pole >= 0) return default;
            poles.Add(pole);
            if (node == output)
                return remaining.Count == 0 && poles.Count >= 2 ? poles.ToImmutable() : default;

            Element? coupling = currentDriven
                ? Take<Vccs>(e => e.Cp == node && e.Cn == 0 && e.N == 0 && e.Siemens != 0)
                : Take<Vcvs>(e => e.Cp == node && e.Cn == 0 && e.N == 0 && e.Gain != 0);
            if (coupling is null || coupling.P == 0 || !visited.Add(coupling.P)) return default;
            node = coupling.P;
        }
        return default;

        T? Take<T>(Func<T, bool> matches) where T : Element
        {
            var found = remaining.OfType<T>().Where(matches).Take(2).ToArray();
            if (found.Length != 1) return null;
            remaining.Remove(found[0]);
            return found[0];
        }
    }

    // Scale before squaring and recover the smaller root from the product
    // to avoid cancellation for well-separated real roots.
    private static ImmutableArray<double> RealRoots(double linear, double factorA, double factorB)
    {
        var scale = Math.Max(linear, Math.Max(factorA, factorB));
        if (!double.IsFinite(scale) || scale <= 0) return default;
        var b = linear / scale;
        var c = (factorA / scale) * (factorB / scale);
        var discriminant = b * b - 4 * c;
        if (c <= 0 || discriminant < 0) return default;
        var large = -.5 * (b + Math.Sqrt(discriminant));
        var first = scale * large;
        var second = scale * (c / large);
        return double.IsFinite(first) && double.IsFinite(second) && first < 0 && second < 0
            ? [first, second] : default;
    }

    private static bool Between(Element e, int a, int b) =>
        (e.P == a && e.N == b) || (e.P == b && e.N == a);
}
