using Simulator.Client;
using Simulator.Syntax;

namespace Simulator.Frontend;

/// <summary>Lower the level-1, B=1 JFET square law and its two gate junctions.</summary>
internal static class JfetPreparation
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        { "LEVEL", "VTO", "BETA", "LAMBDA", "RD", "RS", "IS" };

    internal static bool Supports(BoundInstance call)
    {
        if (call.Status != BindingStatus.Bound || call.Target is not
            { ModelFamily: "njf" or "pjf", HasOpaqueModelTerms: false } model ||
            call.Ports.Length != 3 || !call.Header.Parameters.IsEmpty)
            return false;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return model.Parameters.All(p => p.Name is { } name && Allowed.Contains(name) && names.Add(name));
    }

    internal static bool TryPrepare(DeclarationCatalog catalog, ParameterEvaluation environment, BoundInstance call,
        out PreparedMos1? profile, out BridgeDiagnostic diagnostic)
    {
        profile = null;
        diagnostic = new("unsupported_jfet1_profile", call.Header.Range);
        if (!Supports(call)) return false;

        var model = call.Target!;
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in model.Parameters)
        {
            var result = ParameterEvaluator.EvaluateValue(catalog, environment, parameter.Value, model.ScopeId);
            if (result.Value is not { } value)
            {
                diagnostic = new(result.Diagnostics[0].Code, result.Diagnostics[0].Range);
                return false;
            }
            values.Add(parameter.Name!, value);
        }

        var beta = 2 * Get("BETA", 1e-4);
        var threshold = Get("VTO", -2);
        var lambda = Get("LAMBDA", 0);
        var rd = Get("RD", 0);
        var rs = Get("RS", 0);
        var saturation = Get("IS", 1e-14);
        if (Get("LEVEL", 1) != 1 || beta <= 0 || !double.IsFinite(beta) || lambda < 0 ||
            saturation <= 0 || !Resistance(rd) || !Resistance(rs))
        {
            diagnostic = new("invalid_jfet1_parameter", model.Range);
            return false;
        }

        // JFET BETA multiplies Vov². The shared square-law primitive uses
        // beta/2, and its junction reference terminal is wired to the gate.
        var p = model.ModelFamily == "pjf";
        const double thermalVolts = 1.38064852e-23 / 1.6021766208e-19 * 300.15;
        var junction = new SpiceDiodeModel(saturation, thermalVolts, 0, 1, .5, .5, 0);
        profile = new(new(p, p ? -threshold : threshold, beta, 0, 1, lambda),
            junction, junction, 0, 0, 0, rd, rs);
        return true;

        double Get(string name, double fallback) => values.GetValueOrDefault(name, fallback);
        static bool Resistance(double ohms) => ohms == 0 || ohms > 0 && double.IsFinite(1 / ohms);
    }
}
