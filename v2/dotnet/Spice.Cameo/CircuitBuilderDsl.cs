using Spice.Ir;
using Spice.Sdk;

namespace Spice.Cameo;

/// <summary>
/// Dimensional SI quantity constructors for programmatic circuit authoring and
/// Spice.Cameo host bindings (ARCH-DSL-001). Guarantees zero DSL evaluation inside
/// the native C++20 solver hot loop (ARCH-DSL-002).
/// </summary>
public static class SiUnits
{
    public static double Ohms(double v) => v;
    public static double KiloOhms(double v) => v * 1.0e3;
    public static double MegaOhms(double v) => v * 1.0e6;
    public static double Farads(double v) => v;
    public static double MicroFarads(double v) => v * 1.0e-6;
    public static double NanoFarads(double v) => v * 1.0e-9;
    public static double PicoFarads(double v) => v * 1.0e-12;
    public static double Henries(double v) => v;
    public static double MilliHenries(double v) => v * 1.0e-3;
    public static double MicroHenries(double v) => v * 1.0e-6;
    public static double Volts(double v) => v;
    public static double MilliVolts(double v) => v * 1.0e-3;
    public static double Amps(double v) => v;
    public static double MilliAmps(double v) => v * 1.0e-3;
    public static double Hertz(double v) => v;
    public static double KiloHertz(double v) => v * 1.0e3;
    public static double MegaHertz(double v) => v * 1.0e6;
    public static double Seconds(double v) => v;
    public static double MilliSeconds(double v) => v * 1.0e-3;
    public static double MicroSeconds(double v) => v * 1.0e-6;
    public static double NanoSeconds(double v) => v * 1.0e-9;
}

/// <summary>
/// Deterministic circuit authoring builder emitting validated, frontend-agnostic CircuitIr
/// (ARCH-DSL-001).
/// </summary>
public sealed class CircuitBuilder
{
    private readonly string _name;
    private readonly HashSet<string> _nodes = new(StringComparer.Ordinal) { "0" };
    private readonly List<DeviceInstanceIr> _devices = [];
    private readonly DeviceRegistry _registry;

    public CircuitBuilder(string name, DeviceRegistry? registry = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Circuit name must be non-empty.", nameof(name));
        }
        _name = name;
        _registry = registry ?? DeviceRegistry.Default;
    }

    public CircuitBuilder AddResistor(string id, string nodeP, string nodeN, double ohms) =>
        AddDevice(id, "spice.resistor",
            new Dictionary<string, string> { ["p"] = nodeP, ["n"] = nodeN },
            new Dictionary<string, double> { ["ohms"] = ohms });

    public CircuitBuilder AddCapacitor(string id, string nodeP, string nodeN, double farads, double? icVolts = null)
    {
        var p = new Dictionary<string, double> { ["farads"] = farads };
        if (icVolts.HasValue) p["ic_volts"] = icVolts.Value;
        return AddDevice(id, "spice.capacitor",
            new Dictionary<string, string> { ["p"] = nodeP, ["n"] = nodeN }, p);
    }

    public CircuitBuilder AddInductor(string id, string nodeP, string nodeN, double henries, double? icAmps = null)
    {
        var p = new Dictionary<string, double> { ["henries"] = henries };
        if (icAmps.HasValue) p["ic_amps"] = icAmps.Value;
        return AddDevice(id, "spice.inductor",
            new Dictionary<string, string> { ["p"] = nodeP, ["n"] = nodeN }, p);
    }

    public CircuitBuilder AddVoltageSource(
        string id, string nodeP, string nodeN, double dcVolts, double acMag = 0.0, double acPhaseDeg = 0.0) =>
        AddDevice(id, "spice.vsrc",
            new Dictionary<string, string> { ["p"] = nodeP, ["n"] = nodeN },
            new Dictionary<string, double>
            {
                ["dc_volts"] = dcVolts,
                ["ac_mag"] = acMag,
                ["ac_phase_deg"] = acPhaseDeg
            });

    public CircuitBuilder AddCurrentSource(
        string id, string nodeP, string nodeN, double dcAmps, double acMag = 0.0) =>
        AddDevice(id, "spice.isrc",
            new Dictionary<string, string> { ["p"] = nodeP, ["n"] = nodeN },
            new Dictionary<string, double> { ["dc_amps"] = dcAmps, ["ac_mag"] = acMag });

    public CircuitBuilder AddDiode(
        string id, string anode, string cathode,
        double isAmps = 1e-14, double nEmission = 1.0, double cjoFarads = 0.0, double ttSeconds = 0.0) =>
        AddDevice(id, "spice.diode",
            new Dictionary<string, string> { ["p"] = anode, ["n"] = cathode },
            new Dictionary<string, double>
            {
                ["is_amps"] = isAmps,
                ["n_emission"] = nEmission,
                ["cjo_farads"] = cjoFarads,
                ["tt_s"] = ttSeconds
            });

    public CircuitBuilder AddDevice(
        string id,
        string kindId,
        IReadOnlyDictionary<string, string> pins,
        IReadOnlyDictionary<string, double> parameters,
        string? modelName = null)
    {
        foreach (var net in pins.Values)
        {
            if (!string.IsNullOrWhiteSpace(net)) _nodes.Add(net);
        }
        _devices.Add(new DeviceInstanceIr(id, kindId, pins, parameters, modelName));
        return this;
    }

    public CircuitIr Build()
    {
        var orderedNodes = _nodes
            .OrderBy(n => n == "0" ? "" : n, StringComparer.Ordinal)
            .ToList();
        var circuit = new CircuitIr(
            _name,
            orderedNodes,
            _devices.ToList(),
            new Dictionary<string, string> { ["source"] = "Spice.Cameo" });

        var errors = _registry.ValidateCircuit(circuit);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid circuit built via Spice.Cameo: " +
                string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}")));
        }
        return circuit;
    }
}

/// <summary>
/// Deterministic bounded-concurrency parallel_map campaign runner backing
/// Cameo corner/sweep/Monte-Carlo plans (ARCH-DSL-001).
/// Preserves exact input order regardless of task completion order.
/// </summary>
public static class CampaignRunner
{
    public static async Task<IReadOnlyList<ResultManifestIr>> RunParallelMapAsync<TParam>(
        IReadOnlyList<TParam> points,
        Func<TParam, int, SimulationRequestIr> requestFactory,
        ISimulationBackend backend,
        int maxDegreeOfParallelism = 4,
        CancellationToken cancellationToken = default)
    {
        if (maxDegreeOfParallelism < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
        }

        var results = new ResultManifestIr[points.Count];
        using var gate = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);
        var tasks = new Task[points.Count];

        for (int idx = 0; idx < points.Count; ++idx)
        {
            int capturedIndex = idx;
            TParam capturedPoint = points[idx];
            tasks[idx] = Task.Run(async () =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var req = requestFactory(capturedPoint, capturedIndex);
                    results[capturedIndex] = await backend.RunAsync(req, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
