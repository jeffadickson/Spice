using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;

namespace Simulator.Frontend;

// The SPICE3 forward Early factor multiplies transport but leaves the base junction currents unchanged.
internal sealed record ForwardEarlyEffect(string ForwardId, string ReverseId, int Base, int Collector, bool Pnp, double Volts);

internal sealed class SelectedDcBias(ImmutableArray<ForwardEarlyEffect> effects)
{
    private readonly double[] factors = Enumerable.Repeat(1d, effects.Length).ToArray();

    internal static BridgeResult Prepare(DeckSyntax syntax, string backend) =>
        OperatingPointSelection.Select(syntax, new BridgeLimits { AllowForwardEarlyEffect = true }, backend).Prepared;

    internal async Task<SimulationResponse?> SolveAsync(SimulationClient client, SimulationRequest request,
        CancellationToken cancellationToken)
    {
        if (effects.IsEmpty) return await client.RunAsync(request, cancellationToken);

        // Reuse only the last accepted factors within this operation. Each
        // inner solve still enforces KCL with fixed transport coefficients.
        var elements = request.Circuit.Elements;
        var trial = (double[])factors.Clone();
        var gains = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < effects.Length; i++)
        {
            gains.Add(effects[i].ForwardId, i);
            gains.Add(effects[i].ReverseId, i);
        }
        var analysis = new OperatingPointNewton(new NewtonOptions(MaxIterations: 200, RelativeTolerance: 1e-11,
            VoltageAbsoluteTolerance: 1e-11, CurrentAbsoluteTolerance: 1e-14,
            Continuation: new(InitialStep: 1)));
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var frozen = elements.Select(e => e is Cccs gain && gains.TryGetValue(gain.Id, out var index)
                ? gain with { Gain = gain.Gain * trial[index] } : e).ToArray();
            var response = await client.RunAsync(request with
            {
                Circuit = request.Circuit with { Elements = frozen }, Analysis = analysis
            }, cancellationToken);
            if (!response.Succeeded) return response;
            var result = response.ReadManifest();
            if (!result.Complete || result.Samples.Length != 1) return null;
            var sample = result.Samples[0].Values;
            double Voltage(int node)
            {
                if (node == 0) return 0;
                var index = Array.FindIndex(result.Signals, s => s.Id == $"v({request.Circuit.Nodes[node]})");
                return index >= 0 && index < sample.Length ? sample[index] : double.NaN;
            }
            var converged = true;
            for (var i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                var vbc = (effect.Pnp ? -1 : 1) * (Voltage(effect.Base) - Voltage(effect.Collector));
                var next = 1 - vbc / effect.Volts;
                if (!double.IsFinite(next) || next <= 0) return null;
                converged &= Math.Abs(next - trial[i]) <= 1e-12 * Math.Max(1, next);
                trial[i] = next;
            }
            if (converged)
            {
                trial.CopyTo(factors, 0);
                return response;
            }
        }
        return null;
    }
}
