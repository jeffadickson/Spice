using Spice.Ir;

namespace Spice.Sdk;

public sealed record DifferentialMetric(
    string SignalId,
    double MaxAbsError,
    double MaxRelError,
    bool Passed);

public sealed record DifferentialReport(
    bool Passed,
    IReadOnlyList<DifferentialMetric> Metrics,
    string? FailureReason = null);

/// <summary>
/// Compares two ISimulationBackend ResultManifestIr outputs (e.g., XyceBackend
/// vs. NativeCppBackend) during Strangler Fig parity qualification (ARCH-COMPAT-002).
/// </summary>
public static class DifferentialComparer
{
    public static DifferentialReport Compare(
        ResultManifestIr reference,
        ResultManifestIr candidate,
        double absTol = 1.0e-6,
        double relTol = 1.0e-4)
    {
        if (reference.Status != RunTerminalStatus.Succeeded || !reference.Complete)
        {
            return new DifferentialReport(false, [], $"Reference backend failed: {reference.ErrorMessage}");
        }
        if (candidate.Status != RunTerminalStatus.Succeeded || !candidate.Complete)
        {
            return new DifferentialReport(false, [], $"Candidate backend failed: {candidate.ErrorMessage}");
        }

        var metrics = new List<DifferentialMetric>();

        if (reference.RealData is not null && candidate.RealData is not null)
        {
            foreach (var (sig, refVals) in reference.RealData.SignalValues)
            {
                if (!candidate.RealData.SignalValues.TryGetValue(sig, out var candVals))
                {
                    return new DifferentialReport(false, metrics, $"Candidate missing signal '{sig}'.");
                }
                if (refVals.Count != candVals.Count)
                {
                    return new DifferentialReport(false, metrics, $"Sample count mismatch on '{sig}': {refVals.Count} vs {candVals.Count}.");
                }

                double maxAbs = 0.0;
                double maxRel = 0.0;
                bool ok = true;
                for (int i = 0; i < refVals.Count; ++i)
                {
                    var diff = Math.Abs(refVals[i] - candVals[i]);
                    var denom = Math.Max(Math.Abs(refVals[i]), Math.Abs(candVals[i]));
                    var rel = denom > 0.0 ? diff / denom : 0.0;
                    maxAbs = Math.Max(maxAbs, diff);
                    maxRel = Math.Max(maxRel, rel);
                    if (diff > absTol + relTol * denom)
                    {
                        ok = false;
                    }
                }
                metrics.Add(new DifferentialMetric(sig, maxAbs, maxRel, ok));
            }
            return new DifferentialReport(metrics.All(m => m.Passed), metrics);
        }

        if (reference.ComplexData is not null && candidate.ComplexData is not null)
        {
            foreach (var (sig, refVals) in reference.ComplexData.SignalValues)
            {
                if (!candidate.ComplexData.SignalValues.TryGetValue(sig, out var candVals))
                {
                    return new DifferentialReport(false, metrics, $"Candidate missing complex signal '{sig}'.");
                }
                if (refVals.Count != candVals.Count)
                {
                    return new DifferentialReport(false, metrics, $"Complex sample count mismatch on '{sig}'.");
                }

                double maxAbs = 0.0;
                double maxRel = 0.0;
                bool ok = true;
                for (int i = 0; i < refVals.Count; ++i)
                {
                    var dr = refVals[i].Real - candVals[i].Real;
                    var di = refVals[i].Imaginary - candVals[i].Imaginary;
                    var diff = Math.Sqrt(dr * dr + di * di);
                    var refMag = Math.Sqrt(refVals[i].Real * refVals[i].Real + refVals[i].Imaginary * refVals[i].Imaginary);
                    var candMag = Math.Sqrt(candVals[i].Real * candVals[i].Real + candVals[i].Imaginary * candVals[i].Imaginary);
                    var denom = Math.Max(refMag, candMag);
                    var rel = denom > 0.0 ? diff / denom : 0.0;
                    maxAbs = Math.Max(maxAbs, diff);
                    maxRel = Math.Max(maxRel, rel);
                    if (diff > absTol + relTol * denom)
                    {
                        ok = false;
                    }
                }
                metrics.Add(new DifferentialMetric(sig, maxAbs, maxRel, ok));
            }
            return new DifferentialReport(metrics.All(m => m.Passed), metrics);
        }

        return new DifferentialReport(false, [], "Backends returned incompatible or empty series payloads.");
    }
}
