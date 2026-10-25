using Simulator.Client;
using System.Text.RegularExpressions;

namespace SForge.Language;

/// <summary>Lowers the validated v0.1 AST into the simulator's stable typed request contract.</summary>
public static class SForgeLowering
{
    public static SimulationRequest Lower(Document document, string runName, string caseId = "sforge")
    {
        var errors = SForgeSemanticValidator.Validate(document);
        if (errors.Count != 0) throw new InvalidDataException(string.Join("; ", errors.Select(error => error.Message)));
        var run = document.Declarations.OfType<Run>().SingleOrDefault(candidate => candidate.Name == runName)
            ?? throw new InvalidDataException($"run '{runName}' was not declared");
        var circuit = document.Declarations.OfType<Circuit>().Single(candidate => candidate.Name == run.Circuit);
        var positions = circuit.Nodes.Select((node, index) => (node, index)).ToDictionary(pair => pair.node, pair => pair.index, StringComparer.Ordinal);
        var elements = circuit.Devices.Select(device => LowerDevice(device, positions)).ToArray();
        if (run.Analysis is not Transient transient) throw new InvalidDataException("v0.1 only lowers transient analyses");
        var stop = ToSi(transient.Stop, new Dictionary<string, double> { ["s"] = 1, ["ms"] = 1e-3, ["us"] = 1e-6 });
        var step = ToSi(transient.MaxStep, new Dictionary<string, double> { ["s"] = 1, ["ms"] = 1e-3, ["us"] = 1e-6 });
        var simulatorNodes = circuit.Nodes.Select(node => node == "gnd" ? "0" : node).ToArray();
        var experiment = LowerExperiment(run, stop);
        return new SimulationRequest(caseId, new Simulator.Client.Circuit(simulatorNodes, elements), new TransientConfigured(new TransientConfiguration(stop, step, 100_000)), Experiment: experiment);
    }

    private static Element LowerDevice(Device device, IReadOnlyDictionary<string, int> positions) => device switch
    {
        Resistor resistor => new Simulator.Client.Resistor(resistor.Name, positions[resistor.Positive], positions[resistor.Negative], ToSi(resistor.Resistance, new Dictionary<string, double> { ["Ohm"] = 1, ["kOhm"] = 1e3, ["MOhm"] = 1e6 })),
        Capacitor capacitor => new Simulator.Client.Capacitor(capacitor.Name, positions[capacitor.Positive], positions[capacitor.Negative], ToSi(capacitor.Capacitance, new Dictionary<string, double> { ["F"] = 1, ["uF"] = 1e-6, ["nF"] = 1e-9, ["pF"] = 1e-12 })),
        VoltageSource source => new Voltage(source.Name, positions[source.Positive], positions[source.Negative], ToSi(source.Dc, new Dictionary<string, double> { ["V"] = 1, ["mV"] = 1e-3 })),
        Diode diode => new Simulator.Client.Diode(diode.Name, positions[diode.Positive], positions[diode.Negative], 1e-12, 0.02585),
        _ => throw new InvalidDataException($"unsupported SForge device {device.GetType().Name}")
    };

    private static double ToSi(Quantity quantity, IReadOnlyDictionary<string, double> units) =>
        units.TryGetValue(quantity.Unit, out var scale) ? quantity.Value * scale : throw new InvalidDataException($"unsupported unit '{quantity.Unit}'");

    /// <summary>Lower observable snapshots and bounded max assertions without evaluating source expressions in the host process.</summary>
    private static Experiment? LowerExperiment(Run run, double stop)
    {
        var measurements = new List<Measurement>();
        foreach (var observable in run.Observables)
            measurements.Add(new($"observe_{observable.Kind}_{observable.Target}", Signal(observable), new ValueAt(stop)));
        var assertions = new List<Assertion>();
        foreach (var requirement in run.Requirements)
        {
            var match = Regex.Match(requirement.Expression, @"^max\((?<kind>[vi])\((?<target>[$_A-Za-z][$_A-Za-z0-9-]*)\)\)\s*<\s*(?<value>[0-9.eE+-]+)(?<unit>[A-Za-z]+)$", RegexOptions.CultureInvariant);
            if (!match.Success) throw new InvalidDataException($"unsupported require expression '{requirement.Expression}'");
            var signal = $"{match.Groups["kind"].Value}({match.Groups["target"].Value})";
            var unit = match.Groups["unit"].Value;
            var value = double.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture) * UnitScale(unit);
            var id = $"require_max_{match.Groups["kind"].Value}_{match.Groups["target"].Value}";
            measurements.Add(new(id, signal, new Maximum(0, stop)));
            assertions.Add(new($"assert_{id}", id, unit is "V" or "mV" ? "V" : "A", -1e300, value));
        }
        return measurements.Count == 0 ? null : new Experiment(measurements.ToArray(), assertions.ToArray());
    }
    private static string Signal(Observable observable) => observable.Kind switch
    {
        "v" => $"v({observable.Target})", "i" => $"i({observable.Target})", _ => throw new InvalidDataException($"unsupported observable '{observable.Kind}'")
    };
    private static double UnitScale(string unit) => unit switch { "V" or "A" => 1, "mV" => 1e-3, "mA" => 1e-3, _ => throw new InvalidDataException($"unsupported assertion unit '{unit}'") };
}
