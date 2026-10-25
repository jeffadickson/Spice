// Usage: dotnet run --file scripts/Inventory-Ngspice.cs -- C:\dev\ngspice-ngspice
// Read-only inventory of the upstream corpus; it never writes into that checkout.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var root = args.Length == 1 ? args[0] : throw new ArgumentException("Expected ngspice repository root.");
var tests = Path.Combine(root, "tests");
if (!Directory.Exists(tests)) throw new DirectoryNotFoundException(tests);
var files = Directory.EnumerateFiles(tests, "*.*", SearchOption.AllDirectories)
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
        var supported = markers.All(marker => new[] { "R", "C", "L", "V", "I", "D", ".end", ".op", ".tran", ".ac", ".options", ".print", ".plot", ".probe" }.Contains(marker, StringComparer.OrdinalIgnoreCase));
        return new { path = Path.GetRelativePath(root, path).Replace('\\', '/'), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), markers, status = supported ? "candidate_basic" : "future_feature" };
    }).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new { root = Path.GetFullPath(root), files = files.Length, candidate_basic = files.Count(file => file.status == "candidate_basic"), future_feature = files.Count(file => file.status == "future_feature"), fixtures = files }, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
