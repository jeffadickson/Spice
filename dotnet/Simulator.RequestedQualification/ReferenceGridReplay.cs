using Simulator.Client;
using Simulator.Frontend;
namespace Simulator.RequestedQualification;

/// <summary>Constructs a separately labelled linear physics replay, not a replacement
/// for the original free-running request or its adaptive qualification evidence.</summary>
public static class ReferenceGridReplay
{
    /// <summary>Executes a separately identified grid-controlled replay through the
    /// normal client and full observable comparator. It never replaces the
    /// original operation, result, or free-running qualification status.</summary>
    public static async Task<ReferenceGridResult> RunAsync(RequestedOperation operation, BridgeResult circuit,
        NativeRaw reference, SimulationClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(client);
        if (operation.Kind != "tran" || operation.Requests.Length != 1 || operation.UsesUic || operation.OutputStartS != 0
            || operation.InitializationRequest is not null || !operation.InitialConditions.IsEmpty)
            throw new NotSupportedException("reference_grid_requires_complete_non_uic_transient");
        var original = operation.Requests[0];
        var replay = Prepare(original, reference);
        var response = await client.RunAsync(replay, cancellationToken).ConfigureAwait(false);
        var comparison = HasExactGrid(replay,response)
            ? RequestedComparison.Compare(operation with { Requests = [replay] }, circuit, [response], reference)
            : new ComparisonResult("reference_grid_contract_gap",[],["reference_grid_contract_gap"]);
        return new(original, replay, response, comparison);
    }
    /// <summary>Requires a complete real response, the exact accepted sequence and
    /// explicit integration mode before invoking a comparator that could otherwise
    /// interpolate. Numerical/provider agreement remains a separate mandatory gate.</summary>
    public static bool HasExactGrid(SimulationRequest replay,SimulationResponse response)
    {
        ArgumentNullException.ThrowIfNull(replay);ArgumentNullException.ThrowIfNull(response);
        if(!response.Succeeded || replay.Analysis is not TransientConfigured { Options.AcceptedTimesS: {} times } || times.Length<2
            || response.Payload.ValueKind!=System.Text.Json.JsonValueKind.Object
            || !response.Payload.TryGetProperty("integration",out var integration) || integration.ValueKind!=System.Text.Json.JsonValueKind.Object
            || !integration.TryGetProperty("time_control",out var mode) || mode.ValueKind!=System.Text.Json.JsonValueKind.String
            || mode.GetString()!="accepted_time_grid")return false;
        try {
            var manifest=response.ReadManifest();
            return manifest.AcSamples.Length==0 && manifest.Samples.Length==times.Length
                && manifest.Samples.Select(s=>s.TimeS).SequenceEqual(times);
        }catch(Exception error)when(error is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException){return false;}
    }
    /// <summary>Uses every recorded native accepted time without interpolation,
    /// preserving circuit, source waveforms, integration method and step ceiling.
    /// Adaptive control is explicitly disabled in the returned copy. The original
    /// request must be retained and verified separately. UIC/windowed, nonlinear,
    /// malformed, incomplete and budget-exceeding grids are refused.</summary>
    public static SimulationRequest Prepare(SimulationRequest original, NativeRaw reference)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(reference);
        if (original.Analysis is not TransientConfigured transient || reference.Complex
            || reference.PlotName != "Transient Analysis" || reference.Variables.IsEmpty
            || reference.Variables[0].Dimension != "time" || reference.Variables[0].Name != "time")
            throw new InvalidDataException("reference_grid_analysis_identity");
        var options = transient.Options;
        if (options.InitialState is not null || options.InitialNewton is not null
            || original.Circuit.Elements.Any(e => e is not (Resistor or Capacitor or Inductor or Voltage or Current or Vccs or Cccs or Vcvs or Ccvs)))
            throw new NotSupportedException("reference_grid_requires_linear_default_initialization");
        if (!double.IsFinite(options.StopS) || options.StopS <= 0 || !double.IsFinite(options.MaxStepS) || options.MaxStepS <= 0
            || options.MaxSteps is < 1 or > 100000 || reference.Points.Length < 2 || reference.Points.Length > options.MaxSteps + 1
            || reference.Points.Any(p => p.Length != reference.Variables.Length || p.Any(v => !double.IsFinite(v.Real) || !double.IsFinite(v.Imag) || v.Imag != 0)))
            throw new InvalidDataException("reference_grid_shape_or_budget");
        var times = reference.Points.Select(p => p[0].Real).ToArray();
        // Endpoint subtraction loses a few ULPs at large absolute times; allow
        // only rounding of the original ceiling, never a larger physical step.
        var roundoff = 32 * 2.2204460492503131e-16 * options.StopS;
        if (times[0] != 0 || times[^1] != options.StopS
            || times.Zip(times.Skip(1)).Any(p => p.Second <= p.First || p.Second - p.First > options.MaxStepS + roundoff))
            throw new InvalidDataException("reference_grid_axis_or_step_ceiling");
        return original with { Analysis = new TransientConfigured(options with { Adaptive = null, AcceptedTimesS = times }) };
    }
}

/// <summary>Explicit physics-only replay evidence alongside the preserved original request.</summary>
public sealed record ReferenceGridResult(SimulationRequest Original, SimulationRequest Replay,
    SimulationResponse Response, ComparisonResult Comparison)
{
    /// <summary>Fixed scope marker: never interpret this result as adaptive-controller parity.</summary>
    public string Mode => "reference_grid_physics_replay";
}
