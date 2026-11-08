using System.Text.Json;

namespace Simulator.RequestedQualification;

/// <summary>Per-inventory-row qualification expectation, independent of stage counts.</summary>
internal sealed record DeckValidationContract(string Mode, string? Diagnostic)
{
    internal static DeckValidationContract Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("invalid_validation_contract");

        string? mode = null;
        string? diagnostic = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
        {
            if (!names.Add(field.Name))
                throw new InvalidDataException("invalid_validation_contract");
            switch (field.Name, field.Value.ValueKind)
            {
                case ("mode", JsonValueKind.String):
                    mode = field.Value.GetString();
                    break;
                case ("diagnostic", JsonValueKind.String):
                    diagnostic = field.Value.GetString();
                    break;
                case ("diagnostic", JsonValueKind.Null):
                    break;
                default:
                    throw new InvalidDataException("invalid_validation_contract");
            }
        }

        return mode switch
        {
            "native_agreement" or "execution_only" when diagnostic is null => new(mode, null),
            "expected_preparation_refusal" when diagnostic is { Length: > 0 and <= 120 } &&
                diagnostic.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or ':' or '-') => new(mode, diagnostic),
            _ => throw new InvalidDataException("invalid_validation_contract")
        };
    }

    // A missing oracle or unrelated infrastructure failure is unavailable.
    // Only the declared preparation boundary can satisfy a refusal contract.
    internal bool? Evaluate(bool? executed, bool? nativeAgreed, string status, IReadOnlyCollection<string> diagnostics) =>
        Mode switch
        {
            "native_agreement" => executed == false ? false : nativeAgreed,
            "execution_only" => executed,
            "expected_preparation_refusal" when status == "requested_preparation_refused" =>
                diagnostics.Contains(Diagnostic!, StringComparer.Ordinal),
            "expected_preparation_refusal" when executed == true || status == "requested_execution_failed" => false,
            _ => null
        };
}
