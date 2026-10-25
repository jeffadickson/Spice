// Usage: dotnet run --file scripts/Validate-NgspiceFixtureManifest.cs -- compat/ngspice/selected-fixtures.json
// Validates the small, attributed fixture selection without touching the upstream checkout.
using System.Text.Json;

var path = args.Length == 1 ? args[0] : "compat/ngspice/selected-fixtures.json";
using var document = JsonDocument.Parse(File.ReadAllText(path));
var root = document.RootElement;

if (!root.TryGetProperty("source_checkout_revision", out var revision)
    || string.IsNullOrWhiteSpace(revision.GetString()))
    throw new InvalidDataException("source_checkout_revision must be nonempty.");
if (!root.TryGetProperty("policy", out var policy) || string.IsNullOrWhiteSpace(policy.GetString()))
    throw new InvalidDataException("policy must be nonempty.");
if (!root.TryGetProperty("fixtures", out var fixtures) || fixtures.ValueKind != JsonValueKind.Array)
    throw new InvalidDataException("fixtures must be an array.");

var paths = new HashSet<string>(StringComparer.Ordinal);
var allowedStates = new HashSet<string>(StringComparer.Ordinal) {
    "blocked_by_features", "oracle_passing", "analytic_oracle_passing", "import_only",
};
foreach (var fixture in fixtures.EnumerateArray()) {
    var fixturePath = fixture.GetProperty("path").GetString();
    var family = fixture.GetProperty("family").GetString();
    var state = fixture.GetProperty("state").GetString();
    var features = fixture.GetProperty("required_features");
    if (string.IsNullOrWhiteSpace(fixturePath) || !paths.Add(fixturePath)
        || string.IsNullOrWhiteSpace(family) || state is null || !allowedStates.Contains(state)
        || features.ValueKind != JsonValueKind.Array)
        throw new InvalidDataException("fixture must have unique path, family, known state and feature array.");
    var featureNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var feature in features.EnumerateArray()) {
        var name = feature.GetString();
        if (string.IsNullOrWhiteSpace(name) || !featureNames.Add(name))
            throw new InvalidDataException("required features must be nonempty and unique.");
    }
    if ((state == "oracle_passing" || state == "analytic_oracle_passing")
        && features.GetArrayLength() != 0)
        throw new InvalidDataException("oracle-passing fixture cannot list missing features.");
}

Console.WriteLine($"Validated {fixtures.GetArrayLength()} selected ngspice fixtures at {revision.GetString()}.");
