using System.Security.Cryptography;
using System.Text;
using Simulator.Syntax;

namespace Simulator.Frontend;

/// <summary>Identifies the exact external Berkeley FD card and supported instance geometry.</summary>
internal static class BsimSoiFdPreparation
{
    // Sorted lowercase name=value spellings, joined by LF without a trailing LF.
    // Fingerprinting the card avoids silently discarding terms in the fixed provider.
    private const string ReferenceCard =
        "705B663A7D8F888C762ACDB65D9FCCA46961A033895B939893E5F036CE94D7F3";

    internal static bool IsFdSelector(BoundInstance instance) =>
        instance.Target is { ModelFamily: "nmos" or "pmos" } model &&
        model.Parameters.Any(p => string.Equals(p.Name, "level", StringComparison.OrdinalIgnoreCase)
            && p.Value.Text.Trim() == "55");

    internal static bool IsReference(DeclarationCatalog catalog, ParameterEvaluation environment,
        BoundInstance instance)
    {
        if (instance.Status != BindingStatus.Bound || instance.Ports.Length != 4 ||
            instance.Target is not { ModelFamily: "nmos", HasOpaqueModelTerms: false } model ||
            model.Parameters.Length != 113 || model.Parameters.Any(p => p.Name is null) ||
            instance.Header.Parameters.Length != 2)
            return false;

        var normalized = string.Join('\n', model.Parameters
            .Select(p => p.Name!.ToLowerInvariant() + "=" + p.Value.Text.Trim().ToLowerInvariant())
            .Order(StringComparer.Ordinal));
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))) != ReferenceCard)
            return false;

        var geometry = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in instance.Header.Parameters)
        {
            if (assignment.Name is not {} name ||
                ParameterEvaluator.EvaluateValue(catalog, environment, assignment.Value,
                    instance.Header.ScopeId).Value is not double value ||
                !geometry.TryAdd(name, value))
                return false;
        }
        return geometry.TryGetValue("W", out var width) && Math.Abs(width - 10e-6) <= 2e-21 &&
               geometry.TryGetValue("L", out var length) && Math.Abs(length - 0.25e-6) <= 5e-23;
    }
}
