using Cameo.Netlist;
using Cameo.Netlist.Scripting;
using Cameo.Runtime.Interop;
using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;

namespace Spice.Cameo;

/// <summary>
/// Structured verification assertion recorded by a .cameo simulation script via
/// <c>sim.assert_near(...)</c> or <c>sim.assert_true(...)</c>.
/// </summary>
public sealed record CameoAssertionResult(
    string Label,
    bool Passed,
    double? Actual = null,
    double? Expected = null,
    string? Detail = null);

/// <summary>
/// Full result of executing a .cameo netlist + simulation control script against an
/// <see cref="ISimulationBackend"/>.
/// </summary>
public sealed record CameoSimulationSessionResult(
    bool Succeeded,
    FlagshipNetlistDocument? Document,
    string? FormattedSpicenet,
    IReadOnlyList<ResultManifestIr> Runs,
    IReadOnlyList<CameoAssertionResult> Assertions,
    IReadOnlyList<FlagshipDiagnostic> Diagnostics,
    string Stdout);

/// <summary>
/// First-class Cameo (.cameo) scripting host for Spice V2 (ARCH-DSL-001, ARCH-DSL-002).
/// Combines <see cref="CameoNetlistScriptHost"/> (<c>net.*</c> + ISO 80000 <c>units.*</c> +
/// <c>safenum.*</c>) with a typed <c>sim.*</c> simulation controller over <see cref="ISimulationBackend"/>.
/// Cameo scripts orchestrate netlist generation, analyses, sweeps, and assertions strictly
/// outside the native C++20 Newton/device hot loop.
/// </summary>
public static class CameoSimulationHost
{
    public static CameoSimulationSessionResult RunScript(
        string scriptSource,
        ISimulationBackend backend,
        ComponentLibraryStore? library = null,
        CameoNetlistScriptOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        var opts = (options ?? new CameoNetlistScriptOptions()) with
        {
            LibraryResolver = options?.LibraryResolver ?? CameoNetlistBridge.CreateLibraryResolver(library)
        };

        var runs = new List<ResultManifestIr>();
        var assertions = new List<CameoAssertionResult>();

        var eval = CameoNetlistScriptHost.Evaluate(
            scriptSource,
            opts,
            configureEngine: (engine, netBuilder) =>
            {
                var controller = new SpiceSimulationController(netBuilder, backend, library, runs, assertions);
                engine.Expose("sim", controller);
            });

        var allDiags = new List<FlagshipDiagnostic>(eval.Diagnostics);
        foreach (var a in assertions.Where(x => !x.Passed))
        {
            allDiags.Add(new FlagshipDiagnostic(
                "error",
                "cameo_assertion_failed",
                $"Assertion '{a.Label}' failed: {a.Detail ?? $"actual={a.Actual}, expected={a.Expected}"}"));
        }

        // Allow simulation-only scripts that invoked sim.run_spice_deck(...) without calling net.*
        bool ok = (eval.Succeeded || (runs.Count > 0 && eval.Diagnostics.All(d => d.Code == "empty_circuit"))) &&
                  assertions.All(a => a.Passed);

        if (ok && !eval.Succeeded)
        {
            allDiags.RemoveAll(d => d.Code == "empty_circuit");
        }

        return new CameoSimulationSessionResult(
            Succeeded: ok,
            Document: eval.Document,
            FormattedSpicenet: eval.FormattedSpicenet,
            Runs: runs,
            Assertions: assertions,
            Diagnostics: allDiags,
            Stdout: eval.Stdout);
    }
}

/// <summary>
/// Typed Spice simulation controller exposed as the global <c>sim</c> object in .cameo scripts.
/// Executes compiled immutable IR / canonical SPICE decks on <see cref="ISimulationBackend"/>
/// so zero Cameo bytecode ever runs inside the C++20 numerical hot loop (ARCH-DSL-002).
/// </summary>
public sealed class SpiceSimulationController
{
    private readonly NetlistScriptBuilder _netBuilder;
    private readonly ISimulationBackend _backend;
    private readonly ComponentLibraryStore? _library;
    private readonly List<ResultManifestIr> _runs;
    private readonly List<CameoAssertionResult> _assertions;
    private ResultManifestIr? _lastRun;

    internal SpiceSimulationController(
        NetlistScriptBuilder netBuilder,
        ISimulationBackend backend,
        ComponentLibraryStore? library,
        List<ResultManifestIr> runs,
        List<CameoAssertionResult> assertions)
    {
        _netBuilder = netBuilder;
        _backend = backend;
        _library = library;
        _runs = runs;
        _assertions = assertions;
    }

    // -------------------- Analysis Execution --------------------

    [Expose("op")]
    public bool Op() => RunCurrentCircuit(AnalysisKind.OperatingPoint);

    [Expose("dc")]
    public bool Dc(string sourceId, double start, double stop, double step) =>
        RunCurrentCircuit(
            AnalysisKind.DcSweep,
            sweepSourceId: sourceId,
            options: new Dictionary<string, double>
            {
                ["start"] = start,
                ["stop"] = stop,
                ["step"] = step
            });

    [Expose("ac")]
    public bool Ac(double fStartHz, double fStopHz, int pointsPerDecade = 10) =>
        RunCurrentCircuit(
            AnalysisKind.Ac,
            options: new Dictionary<string, double>
            {
                ["f_start_hz"] = fStartHz,
                ["f_stop_hz"] = fStopHz,
                ["points_per_decade"] = Math.Max(1, pointsPerDecade)
            });

    [Expose("tran")]
    public bool Tran(double stepSeconds, double stopSeconds, bool uic = false) =>
        RunCurrentCircuit(
            AnalysisKind.Transient,
            options: new Dictionary<string, double>
            {
                ["step_s"] = stepSeconds,
                ["stop_s"] = stopSeconds,
                ["uic"] = uic ? 1.0 : 0.0
            });

    [Expose("run_spice_deck")]
    public bool RunSpiceDeck(string name, string deckText, string analysis = "op")
    {
        if (_backend is not NativeCppBackend native)
        {
            throw new InvalidOperationException("sim.run_spice_deck requires NativeCppBackend.");
        }
        var kind = ParseAnalysis(analysis);
        var res = native.RunSpiceDeckAsync(name, deckText, kind).GetAwaiter().GetResult();
        _lastRun = res;
        _runs.Add(res);
        return res.Status == RunTerminalStatus.Succeeded;
    }

    /// <summary>
    /// Runs a deterministic bounded-concurrency parallel operating-point parameter sweep
    /// across <paramref name="values"/> for a part in the current elaborated circuit,
    /// preserving exact input order.
    /// </summary>
    [Expose("sweep_op")]
    public double[] SweepOp(string partId, string paramName, double[] values, string probeNode, int maxParallel = 4)
    {
        var doc = RequireSnapshot();
        var compiled = CameoNetlistBridge.CompileToIr(doc, library: _library);
        if (!compiled.Succeeded || compiled.Circuit is null)
        {
            throw new InvalidOperationException("Cannot run sim.sweep_op: " + string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        }

        var baseCircuit = compiled.Circuit;
        var results = CampaignRunner.RunParallelMapAsync(
            values,
            (val, idx) =>
            {
                bool matched = false;
                var updatedDevices = new List<DeviceInstanceIr>(baseCircuit.Devices.Count);
                foreach (var dev in baseCircuit.Devices)
                {
                    if (string.Equals(dev.Id, partId, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        var p = new Dictionary<string, double>(dev.Parameters, StringComparer.Ordinal)
                        {
                            [paramName] = val
                        };
                        updatedDevices.Add(dev with { Parameters = p });
                    }
                    else
                    {
                        updatedDevices.Add(dev);
                    }
                }
                if (!matched)
                {
                    throw new InvalidOperationException($"Part '{partId}' not found in elaborated circuit for sim.sweep_op.");
                }
                var mutated = baseCircuit with { Name = $"{baseCircuit.Name}_sweep_{idx}", Devices = updatedDevices };
                return new SimulationRequestIr($"{doc.Name}_sweep_{idx}", mutated, AnalysisKind.OperatingPoint);
            },
            _backend,
            Math.Clamp(maxParallel, 1, 16)).GetAwaiter().GetResult();

        var output = new double[results.Count];
        for (int i = 0; i < results.Count; ++i)
        {
            _runs.Add(results[i]);
            _lastRun = results[i];
            if (results[i].Status != RunTerminalStatus.Succeeded)
            {
                throw new InvalidOperationException($"sim.sweep_op corner {i} failed: {results[i].ErrorMessage}");
            }
            output[i] = ExtractScalarFromManifest(results[i], probeNode);
        }
        return output;
    }

    // -------------------- Waveform & Scalar Probing --------------------

    [Expose("status")]
    public string Status() => _lastRun?.Status.ToString() ?? "None";

    [Expose("run_count")]
    public int RunCount() => _runs.Count;

    [Expose("signals")]
    public string[] Signals() => _lastRun?.Signals.Select(s => s.Id).ToArray() ?? [];

    [Expose("v")]
    public double Voltage(string nodeName)
    {
        var run = RequireLastRun();
        var key = nodeName.StartsWith("v(", StringComparison.OrdinalIgnoreCase) ? nodeName : $"v({nodeName})";
        return ExtractScalarFromManifest(run, key);
    }

    [Expose("i")]
    public double Current(string sourceId)
    {
        var run = RequireLastRun();
        var key = sourceId.StartsWith("i(", StringComparison.OrdinalIgnoreCase) ? sourceId : $"i({sourceId})";
        return ExtractScalarFromManifest(run, key);
    }

    [Expose("axis")]
    public double[] Axis()
    {
        var run = RequireLastRun();
        if (run.RealData is not null) return run.RealData.AxisValues.ToArray();
        if (run.ComplexData is not null) return run.ComplexData.AxisValues.ToArray();
        return [];
    }

    [Expose("times")]
    public double[] Times() => Axis();

    [Expose("freqs")]
    public double[] Freqs() => Axis();

    [Expose("series")]
    public double[] Series(string signalId)
    {
        var run = RequireLastRun();
        if (run.RealData is null) throw new InvalidOperationException("Latest run has no real-valued series data.");
        return LookupSignal(run.RealData.SignalValues, signalId).ToArray();
    }

    [Expose("max")]
    public double Max(string signalId) => Series(signalId).Max();

    [Expose("min")]
    public double Min(string signalId) => Series(signalId).Min();

    [Expose("final")]
    public double Final(string signalId)
    {
        var s = Series(signalId);
        return s.Length > 0 ? s[^1] : double.NaN;
    }

    [Expose("mag")]
    public double[] Mag(string signalId)
    {
        var run = RequireLastRun();
        if (run.ComplexData is null) throw new InvalidOperationException("Latest run has no complex AC series data.");
        return LookupSignal(run.ComplexData.SignalValues, signalId)
            .Select(c => Math.Sqrt(c.Real * c.Real + c.Imaginary * c.Imaginary))
            .ToArray();
    }

    [Expose("db")]
    public double[] Db(string signalId) =>
        Mag(signalId).Select(m => 20.0 * Math.Log10(Math.Max(m, 1e-30))).ToArray();

    [Expose("phase_deg")]
    public double[] PhaseDeg(string signalId)
    {
        var run = RequireLastRun();
        if (run.ComplexData is null) throw new InvalidOperationException("Latest run has no complex AC series data.");
        return LookupSignal(run.ComplexData.SignalValues, signalId)
            .Select(c => Math.Atan2(c.Imaginary, c.Real) * (180.0 / Math.PI))
            .ToArray();
    }

    /// <summary>
    /// Computes log-frequency interpolated bandwidth where AC magnitude drops by
    /// <paramref name="dropDb"/> (default -3.0 dB) relative to the first frequency bin.
    /// </summary>
    [Expose("cutoff_hz")]
    public double CutoffHz(string signalId, double dropDb = -3.0)
    {
        var freqs = Freqs();
        var dbs = Db(signalId);
        if (freqs.Length < 2 || freqs.Length != dbs.Length)
        {
            throw new InvalidOperationException("sim.cutoff_hz requires an AC sweep with at least 2 frequency points.");
        }
        double target = dbs[0] + dropDb;
        for (int i = 1; i < dbs.Length; ++i)
        {
            if (dbs[i] <= target && dbs[i - 1] >= target)
            {
                double frac = Math.Abs(dbs[i - 1] - dbs[i]) > 1e-12
                    ? (dbs[i - 1] - target) / (dbs[i - 1] - dbs[i])
                    : 0.0;
                double logF = Math.Log10(freqs[i - 1]) + frac * (Math.Log10(freqs[i]) - Math.Log10(freqs[i - 1]));
                return Math.Pow(10.0, logF);
            }
        }
        return double.NaN;
    }

    // -------------------- Verification Assertions & Artifact Export --------------------

    [Expose("assert_near")]
    public bool AssertNear(string label, double actual, double expected, double absTol = 1e-6, double relTol = 1e-3)
    {
        double diff = Math.Abs(actual - expected);
        double limit = absTol + relTol * Math.Max(Math.Abs(actual), Math.Abs(expected));
        bool passed = double.IsFinite(actual) && double.IsFinite(expected) && diff <= limit;
        _assertions.Add(new CameoAssertionResult(
            label,
            passed,
            actual,
            expected,
            passed ? $"ok (|err|={diff:G4} <= {limit:G4})" : $"|actual({actual:G8}) - expected({expected:G8})| = {diff:G6} > {limit:G6}"));
        return passed;
    }

    [Expose("assert_true")]
    public bool AssertTrue(string label, bool condition, string detail = "")
    {
        _assertions.Add(new CameoAssertionResult(label, condition, Detail: string.IsNullOrWhiteSpace(detail) ? (condition ? "ok" : "condition was false") : detail));
        return condition;
    }

    [Expose("spicenet")]
    public string ExportSpicenet() => FlagshipNetlist.Format(RequireSnapshot());

    [Expose("velosim")]
    public string ExportVeloSim() => CameoNetlistBridge.ExportVeloSimNetlistJson(RequireSnapshot(), _library);

    [Expose("circuit_json")]
    public string ExportCircuitJson() => CameoNetlistBridge.ExportCircuitJson(RequireSnapshot(), _library);

    [Expose("spice_deck")]
    public string ExportSpiceDeck(string analysis = "op")
    {
        var schem = CameoNetlistBridge.ToSchematicDocument(RequireSnapshot(), _library);
        return SchematicExporter.ExportNetlist(schem, new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, ParseAnalysis(analysis)), library: _library);
    }

    // -------------------- Internal Helpers --------------------

    private bool RunCurrentCircuit(
        AnalysisKind analysis,
        string? sweepSourceId = null,
        IReadOnlyDictionary<string, double>? options = null)
    {
        var doc = RequireSnapshot();
        var schem = CameoNetlistBridge.ToSchematicDocument(doc, _library);
        var compiled = SchematicCompiler.CompileToIr(schem, library: _library);
        if (!compiled.Succeeded || compiled.Circuit is null)
        {
            throw new InvalidOperationException("Circuit ERC failed: " + string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        }

        bool usesFullDeck = (compiled.Circuit.Models is { Count: > 0 }) ||
            compiled.Circuit.Devices.Any(d => d.KindId is "spice.mosfet" or "spice.vdmos" or "spice.bjt" or "spice.jfet" or "spice.mesfet" or "spice.switch" or "spice.cswitch" or "spice.core" or "spice.subckt" or "spice.osdi");

        ResultManifestIr res;
        if (usesFullDeck && _backend is NativeCppBackend native)
        {
            var deck = SchematicExporter.ExportNetlist(schem, new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, analysis), library: _library);
            res = native.RunSpiceDeckAsync(doc.Name, deck, analysis, new SpiceDeckExecutionOptions(
                SweepSourceId: sweepSourceId,
                NumericOptions: options)).GetAwaiter().GetResult();
        }
        else
        {
            var req = new SimulationRequestIr(doc.Name, compiled.Circuit, analysis, options, SweepSourceId: sweepSourceId);
            res = _backend.RunAsync(req).GetAwaiter().GetResult();
        }

        _lastRun = res;
        _runs.Add(res);
        return res.Status == RunTerminalStatus.Succeeded;
    }

    private FlagshipNetlistDocument RequireSnapshot() =>
        _netBuilder.BuildSnapshot() ?? throw new InvalidOperationException("Current Cameo netlist is empty or has an unclosed module/rtl block.");

    private ResultManifestIr RequireLastRun() =>
        _lastRun ?? throw new InvalidOperationException("No simulation has been run yet; call sim.op(), sim.dc(), sim.ac(), or sim.tran() first.");

    private static double ExtractScalarFromManifest(ResultManifestIr run, string probeKey)
    {
        var key = probeKey.Contains('(') ? probeKey : $"v({probeKey})";
        if (run.RealData is not null)
        {
            var vals = LookupSignal(run.RealData.SignalValues, key);
            return vals[^1];
        }
        throw new InvalidOperationException($"Run '{run.CaseId}' has no real-valued series data for '{key}'.");
    }

    private static IReadOnlyList<T> LookupSignal<T>(IReadOnlyDictionary<string, IReadOnlyList<T>> map, string key)
    {
        if (map.TryGetValue(key, out var exact)) return exact;
        foreach (var kv in map)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }
        throw new KeyNotFoundException($"Signal '{key}' not found in simulation result. Available: {string.Join(", ", map.Keys)}");
    }

    private static AnalysisKind ParseAnalysis(string s) => s.Trim().ToLowerInvariant() switch
    {
        "op" => AnalysisKind.OperatingPoint,
        "dc" => AnalysisKind.DcSweep,
        "ac" => AnalysisKind.Ac,
        "tran" => AnalysisKind.Transient,
        "noise" => AnalysisKind.Noise,
        "hb" => AnalysisKind.HarmonicBalance,
        "pss" => AnalysisKind.PeriodicSteadyState,
        "sp" => AnalysisKind.SParameters,
        _ => throw new ArgumentException($"Unsupported analysis '{s}'.")
    };
}
