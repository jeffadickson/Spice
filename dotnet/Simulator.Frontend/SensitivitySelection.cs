using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Simulator.Client;
using Simulator.Syntax;

namespace Simulator.Frontend;

/// <summary>Derivative of the selected DC voltage with respect to an original root scalar.</summary>
public sealed record DcSensitivity(string Parameter, string Unit, double Value);

/// <summary>Bounded primitive sensitivity. Model and secondary parameter columns are outside this profile.</summary>
public sealed record DcSensitivityResult(ImmutableArray<DcSensitivity> Derivatives, BridgeDiagnostic? Diagnostic)
{
    /// <summary>Whether every supported primitive parameter was measured.</summary>
    public bool Success => Diagnostic is null;

    /// <summary>Identifies exactly the parameter population represented by the derivatives.</summary>
    public string Profile => "root_resistance_and_independent_dc_source_values";
}

/// <summary>Selected .sens differential DC voltage versus root R, V and I scalar values.</summary>
public static partial class SensitivitySelection
{
    [GeneratedRegex(@"^\.sens\s+v\(\s*([^\s(),]+)\s*(?:,\s*([^\s(),]+)\s*)?\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Directive();

    /// <summary>Measures one root DC sensitivity directive using converged symmetric perturbations.</summary>
    public static async Task<DcSensitivityResult> ExecuteAsync(DeckSyntax syntax, SimulationClient client,
        string backend = "lu", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(client);
        if (syntax is not { Status: "accepted_structure", Declarations: { } catalog })
            return new([], new("syntax_not_accepted", null));
        var cards = catalog.Cards.Where(c => c.Name.Equals(".sens", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (cards is not [{ ScopeId: 0 }] || catalog.Cards.Any(c => c.Kind == "control"))
            return new([], new("unsupported_sens_program", null));
        var range = cards[0].Range;
        DcSensitivityResult Failure(string code) => new([], new(code, range));
        var statement = syntax.Statements.FirstOrDefault(s => s.Range.Start == range.Start);
        var match = Directive().Match(statement?.Text.Trim() ?? "");
        if (!match.Success) return Failure("unsupported_sens_directive");
        var prepared = SelectedDcBias.Prepare(syntax, backend);
        if (!prepared.Success) return new([], prepared.Diagnostics[0]);
        var request = prepared.Request!;
        if (request.Circuit.Elements.Any(e => e is PspiceSwitch))
            return Failure("unsupported_sens_switch_state");
        var nodes = request.Circuit.Nodes;
        string? Node(string name) => name == "0" ? "0" : prepared.RootNodes.GetValueOrDefault(name);
        var positive = Node(match.Groups[1].Value);
        var negative = match.Groups[2].Success ? Node(match.Groups[2].Value) : "0";
        if (positive is null || negative is null || positive == negative)
            return Failure("unsupported_sens_output");

        var elements = request.Circuit.Elements;
        var byId = elements.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var targets = prepared.Origins.Where(o => o.InstancePath.IsEmpty &&
            byId[o.WireId] is Resistor or Voltage or Current &&
            catalog.Primitives.Any(p => p.ScopeId == 0 && p.Range == o.Range && p.Family is "R" or "V" or "I"))
            .ToArray();
        if (targets.Length is < 1 or > 16) return Failure("unsupported_sens_parameter_budget");
        var bias = new SelectedDcBias(prepared.EarlyEffects);
        var derivatives = ImmutableArray.CreateBuilder<DcSensitivity>();
        foreach (var target in targets)
        {
            var element = byId[target.WireId];
            var (value, minimum, unit) = element switch
            {
                Resistor r => (r.Ohms, 0d, "V/ohm"),
                Voltage v => (v.Volts, 1e-4, "V/V"),
                Current i => (i.Amps, 1e-7, "V/A"),
                _ => throw new InvalidOperationException("Unbound sensitivity target.")
            };
            var step = Math.Max(Math.Abs(value) * 1e-5, minimum);
            var plus = await Solve(element, value + step);
            var minus = await Solve(element, value - step);
            var finePlus = await Solve(element, value + step / 2);
            var fineMinus = await Solve(element, value - step / 2);
            var coarse = (plus - minus) / (2 * step);
            var fine = (finePlus - fineMinus) / step;
            if (!double.IsFinite(coarse) || !double.IsFinite(fine))
                return Failure("sens_bias_or_signal_failure");
            if (Math.Abs(coarse - fine) > 1e-6 + 1e-3 * Math.Max(Math.Abs(coarse), Math.Abs(fine)))
                return Failure("sens_derivative_unstable");
            derivatives.Add(new(target.LocalName, unit, fine));
        }
        return new(derivatives.ToImmutable(), null);

        async Task<double> Solve(Element original, double value)
        {
            var perturbed = elements.Select(e => e.Id != original.Id ? e : e switch
            {
                Resistor r => r with { Ohms = value },
                Voltage v => v with { Volts = value },
                Current i => i with { Amps = value },
                _ => e
            }).ToArray();
            var response = await bias.SolveAsync(client, request with { Circuit = request.Circuit with { Elements = perturbed } },
                cancellationToken);
            if (response is not { Succeeded: true }) return double.NaN;
            var result = response.ReadManifest();
            if (!result.Complete || result.Samples.Length != 1) return double.NaN;
            var values = result.Samples[0].Values;
            double VoltageAt(string node)
            {
                if (node == "0") return 0;
                var index = Array.FindIndex(result.Signals, signal => signal.Id == $"v({node})");
                return index >= 0 && index < values.Length ? values[index] : double.NaN;
            }
            return VoltageAt(positive) - VoltageAt(negative);
        }
    }
}
