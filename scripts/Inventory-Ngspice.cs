// Usage: dotnet run --file scripts/Inventory-Ngspice.cs -- C:\dev\ngspice-ngspice
// Read-only inventory of the upstream corpus; it never writes into that checkout.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var root = args.Length == 1 ? args[0] : throw new ArgumentException("Expected ngspice repository root.");
if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
    .Where(path => new[] { ".cir", ".sp", ".ckt", ".net" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .Select(path =>
    {
        var text = File.ReadAllText(path);
        var markers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n').Select(line => line.TrimStart()))
        {
            if (line.Length == 0 || line.StartsWith('*')) continue;
            var head = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var marker = head.StartsWith('.') ? head.ToLowerInvariant() : head[..Math.Min(1, head.Length)].ToUpperInvariant();
            markers.Add(marker);
        }
        var supported = markers.All(marker => new[] { "R", "C", "L", "V", "I", "D", "E", "F", "G", "H", ".end", ".op", ".tran", ".ac", ".options", ".print", ".plot", ".probe" }.Contains(marker, StringComparer.OrdinalIgnoreCase));
        var family = markers.Any(marker => marker is "A" or "U" or ".control" or ".endc") ? "control_or_xspice"
            : markers.Any(marker => marker is "M" or "Q" or "J" or "Z" or ".model") ? "compact_models"
            : markers.Any(marker => marker is "X" or ".subckt" or ".ends" or ".include" or ".lib") ? "hierarchy_or_library"
            : markers.Any(marker => marker is "E" or "F" or "G" or "H") ? "controlled_sources"
            : markers.Any(marker => marker is "T" or "W" or "K") ? "transmission_or_magnetics"
            : markers.Any(marker => marker is ".noise" or ".pz" or ".sens" or ".disto" or ".tf") ? "special_analyses"
            : markers.Any(marker => marker is ".tran" or ".ac") ? "linear_or_waveform"
            : "basic_linear";
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var scope = relative.Split('/')[0].ToLowerInvariant() switch {
            "tests" => "tests",
            "examples" => "examples",
            _ => "other"
        };
        return new { path = relative, source_scope = scope, feature_family = family, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), markers, status = supported ? "candidate_basic" : "future_feature" };
    }).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new {
    root = Path.GetFullPath(root), files = files.Length,
    source_scopes = files.GroupBy(file => file.source_scope).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count()),
    feature_families = files.GroupBy(file => file.feature_family).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count()),
    candidate_basic = files.Count(file => file.status == "candidate_basic"),
    future_feature = files.Count(file => file.status == "future_feature"),
    fixtures = files
}, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
