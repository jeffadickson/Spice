using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Simulator.Client;
using Simulator.Syntax;

namespace Simulator.Frontend;

/// <summary>A source-located voltage transfer about the original DC bias.</summary>
public sealed record VoltageTransfer(string InputSource, SourceRange Range)
{
    internal SimulationRequest Request { get; init; } = null!;
    internal Voltage Input { get; init; } = null!;
    internal int Positive { get; init; }
    internal int Negative { get; init; }
}

/// <summary>Ordered selected transfers, published only when every transfer binds.</summary>
public sealed record TransferFunctionPlan(ImmutableArray<VoltageTransfer> Operations, BridgeDiagnostic? Diagnostic)
{
    /// <summary>Whether all selected transfers were bound.</summary>
    public bool Success => Diagnostic is null;
}

/// <summary>DC voltage gain and port impedances in ohms. Null input impedance on success represents zero incremental input current.</summary>
public sealed record TransferFunctionResult(double? Gain, double? InputOhms, double? OutputOhms, BridgeDiagnostic? Diagnostic)
{
    /// <summary>Whether both perturbation scales produced consistent finite derivatives.</summary>
    public bool Success => Diagnostic is null;
}

/// <summary>Selected .tf voltage transfers using converged, independently perturbed DC operating points.</summary>
public static partial class TransferFunctionSelection
{
    [GeneratedRegex(@"^\.tf\s+v\(\s*([^\s(),]+)\s*(?:,\s*([^\s(),]+)\s*)?\)\s+([^\s(),]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Directive();

    /// <summary>Binds up to sixteen original root voltage transfers in source order. Other analyses are outside this selection.</summary>
    public static TransferFunctionPlan Prepare(DeckSyntax syntax, string backend = "lu")
    {
        ArgumentNullException.ThrowIfNull(syntax);
        if (syntax is not { Status: "accepted_structure", Declarations: { } catalog })
            return new([], new("syntax_not_accepted", null));
        var cards = catalog.Cards.Where(c => c.Name.Equals(".tf", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (cards.Length is < 1 or > 16 || cards.Any(c => c.ScopeId != 0) || catalog.Cards.Any(c => c.Kind == "control"))
            return new([], new("unsupported_tf_program", null));
        var prepared = OperatingPointSelection.Select(syntax, backend: backend).Prepared;
        if (!prepared.Success)
            return new([], prepared.Diagnostics[0]);
        var request = prepared.Request!;
        if (request.Circuit.Elements.Any(e => e is PspiceSwitch))
            return new([], new("unsupported_tf_switch_state", cards[0].Range));

        var operations = ImmutableArray.CreateBuilder<VoltageTransfer>();
        foreach (var card in cards)
        {
            var statement = syntax.Statements.FirstOrDefault(s => s.Range.Start == card.Range.Start);
            var match = Directive().Match(statement?.Text.Trim() ?? "");
            if (!match.Success)
                return new([], new("unsupported_tf_directive", card.Range));
            var name = match.Groups[3].Value;
            var positive = Node(match.Groups[1].Value);
            var negative = match.Groups[2].Success ? Node(match.Groups[2].Value) : 0;
            if (positive < 0 || negative < 0 || positive == negative)
                return new([], new("unsupported_tf_output", card.Range));
            var (element, _) = RequestedDcSourceBinding.Resolve(catalog, prepared, name);
            if (element is not Voltage input || input.P == input.N)
                return new([], new("unsupported_tf_voltage_input", card.Range));
            operations.Add(new(name, card.Range)
            {
                Request = request, Input = input, Positive = positive, Negative = negative
            });
        }
        return new(operations.ToImmutable(), null);

        int Node(string name) => name == "0" ? 0 : prepared.RootNodes.TryGetValue(name, out var wire)
            ? Array.IndexOf(request.Circuit.Nodes, wire) : -1;
    }

    /// <summary>Runs symmetric voltage and current perturbations at two amplitudes. Cancellation propagates to the caller.</summary>
    public static async Task<TransferFunctionResult> ExecuteAsync(VoltageTransfer operation, SimulationClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(client);
        var request = operation.Request;
        if (request is null)
            return new(null, null, null, new("unbound_tf_operation", operation.Range));
        var nodes = request.Circuit.Nodes;
        var elements = request.Circuit.Elements;
        const string probeId = "tf_output_current";
        var coarse = await Estimate(1e-4, 1e-7);
        var fine = await Estimate(5e-5, 5e-8);
        if (coarse is not { } a || fine is not { } b)
            return Failure("tf_bias_or_signal_failure");
        if (!Stable(a.Gain, b.Gain, 1e-6) || !Stable(a.Input, b.Input, 1e-13) ||
            !Stable(a.Output, b.Output, 1e-4) || (a.Input == 0) != (b.Input == 0))
            return Failure("tf_derivative_unstable");
        var inputOhms = b.Input == 0 ? (double?)null : 1 / b.Input;
        return inputOhms is { } z && !double.IsFinite(z)
            ? Failure("tf_input_impedance_overflow") : new(b.Gain, inputOhms, b.Output, null);

        TransferFunctionResult Failure(string code) => new(null, null, null, new(code, operation.Range));

        async Task<(double Gain, double Input, double Output)?> Estimate(double voltage, double current)
        {
            var plus = await Solve(ChangeInput(voltage));
            var minus = await Solve(ChangeInput(-voltage));
            // SPICE branch current enters the positive terminal of the source;
            // the external circuit draws its negative. Inject positive test
            // current into the positive output terminal to measure impedance.
            var outputPlus = await Solve([.. elements, new Current(probeId, operation.Negative, operation.Positive, current)]);
            var outputMinus = await Solve([.. elements, new Current(probeId, operation.Negative, operation.Positive, -current)]);
            if (plus is not { } p || minus is not { } m || outputPlus is not { } op || outputMinus is not { } om)
                return null;
            var delta = (operation.Input.Volts + voltage) - (operation.Input.Volts - voltage);
            return ((p.Output - m.Output) / delta, (m.InputCurrent - p.InputCurrent) / delta,
                (op.Output - om.Output) / (2 * current));
        }

        Element[] ChangeInput(double delta) => elements.Select(e => e.Id == operation.Input.Id
            ? operation.Input with { Volts = operation.Input.Volts + delta } : e).ToArray();

        async Task<(double Output, double InputCurrent)?> Solve(Element[] perturbed)
        {
            var response = await client.RunAsync(request with { Circuit = request.Circuit with { Elements = perturbed } },
                cancellationToken);
            if (!response.Succeeded) return null;
            var manifest = response.ReadManifest();
            if (!manifest.Complete || manifest.Samples.Length != 1) return null;
            var values = manifest.Samples[0].Values;
            double Value(string id)
            {
                var index = Array.FindIndex(manifest.Signals, signal => signal.Id == id);
                return index >= 0 && index < values.Length ? values[index] : double.NaN;
            }
            double VoltageAt(int node) => node == 0 ? 0 : Value($"v({nodes[node]})");
            var output = VoltageAt(operation.Positive) - VoltageAt(operation.Negative);
            var branch = Value($"i({operation.Input.Id})");
            return double.IsFinite(output) && double.IsFinite(branch) ? (output, branch) : null;
        }
    }

    private static bool Stable(double coarse, double fine, double absolute) =>
        double.IsFinite(coarse) && double.IsFinite(fine) &&
        Math.Abs(coarse - fine) <= absolute + 1e-3 * Math.Max(Math.Abs(coarse), Math.Abs(fine));
}
