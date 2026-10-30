using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulator.Client;

namespace Simulator.Benchmarks;

internal static class Runner
{
    internal static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
    private static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static byte[] Input(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 1_048_576) throw new InvalidDataException("Missing/oversized benchmark input.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 1_048_576) throw new InvalidDataException("Input grew beyond budget.");
        return bytes;
    }

    internal static async Task<BenchmarkReport> Run(string root, string engine, string ngspice,
        SuiteDefinition suite, BenchmarkReport report, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        // Freeze all deck/request bytes before execution. Rechecking source and
        // executable hashes later invalidates a run edited during measurement.
        var frozen = suite.Cases.Select(c =>
        {
            var deck = Suites.SafeInput(root, c.Deck);
            var request = Suites.SafeInput(root, c.Request);
            var bytes = Input(deck);
            var requestBytes = Input(request);
            var node = JsonNode.Parse(requestBytes) as JsonObject ?? throw new InvalidDataException("Expected request object.");
            var kind = node["analysis"]?["kind"]?.GetValue<string>();
            if (!(c.Kind == "op" ? kind is "operating_point" or "operating_point_newton" : kind == (c.Kind == "ac" ? "ac" : "transient_configured")))
                throw new InvalidDataException("Deck/request analysis policies disagree.");
            report.Hashes[c.Deck] = HashBytes(bytes); report.Hashes[c.Request] = HashBytes(requestBytes);
            return (Definition: c, Deck: deck, Request: request, Bytes: bytes, Node: node);
        }).ToArray();
        async Task<ProcessResult> Child(string executable, string[] args, string? input, string directory)
        {
            cancellation.ThrowIfCancellationRequested();
            var remaining = report.BudgetSeconds - clock.Elapsed.TotalSeconds;
            if (remaining <= 0) throw new TimeoutException("Whole benchmark budget exhausted.");
            return await ProcessRunner.Run(executable, args, input, directory, TimeSpan.FromSeconds(Math.Min(30, remaining)), cancellation);
        }
        var version = await Child(ngspice, ["-n", "--version"], null, report.RawDirectory);
        if (version.ExitCode != 0) throw new IOException("ngspice version probe failed.");
        var versionText = (version.Output + version.Error).Trim();
        if (versionText.Length is < 1 or > 16_384) throw new InvalidDataException("Invalid version evidence.");
        report = report with { NgspiceVersion = versionText };
        foreach (var input in frozen)
        {
            var c = input.Definition;
            var directory = Path.Combine(report.RawDirectory, c.Id + "-case");
            Directory.CreateDirectory(directory);
            var executedDeck = Path.Combine(directory, "executed.cir");
            File.WriteAllBytes(executedDeck, input.Bytes);
            var measurements = new List<Measurement>(); var comparisons = new List<Comparison>();
            var result = new CaseResult(c.Id, c.Kind, c.Kind == "op" ? "op_index" : c.Kind == "ac" ? "Hz" : "s", c.Unit, c.Policy, report.Hashes[c.Deck],
                report.Hashes[c.Request], measurements, comparisons, [], false);
            report.Cases.Add(result);
            var allPassed = true;
            string[] tools = ["ngspice", "lu", "faer"];
            for (var iteration = -report.Warmups; iteration < report.Iterations; iteration++)
            {
                var outputs = new Dictionary<string, Point[]>();
                // Rotate order so each measured tool occupies every position in
                // the default three repetitions; no unmeasured speedup is inferred.
                for (var position = 0; position < tools.Length; position++)
                {
                    var tool = tools[(iteration + report.Warmups + position) % tools.Length];
                    var stem = Path.Combine(directory, iteration.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + tool);
                    var log = stem + ".log";
                    var node = (JsonObject)input.Node.DeepClone(); node["backend"] = tool;
                    var request = JsonSerializer.Serialize<JsonNode>(node, BenchmarkJson.Default.JsonNode);
                    var measured = tool == "ngspice"
                        ? await Child(ngspice, ["-n", "-b", "-o", log, executedDeck], null, directory)
                        : await Child(engine, [], request, directory);
                    File.WriteAllText(stem + ".stdout", measured.Output);
                    File.WriteAllText(stem + ".stderr", measured.Error);
                    if (measured.ExitCode != 0) throw new IOException($"{c.Id}/{tool} exited {measured.ExitCode}; raw log: {stem}");
                    int? newton = null, solves = null, accepted = null, rejected = null;
                    var providerId = "ngspice"; string? providerVersion = null;
                    if (tool == "ngspice")
                    {
                        if (!File.Exists(log) || new FileInfo(log).Length > 8_388_608) throw new InvalidDataException("Missing/oversized ngspice table.");
                        outputs[tool] = Tables.Parse(File.ReadAllText(log), c.Kind, c.Observable);
                    }
                    else
                    {
                        using var document = JsonDocument.Parse(measured.Output);
                        var value = document.RootElement;
                        var response = new SimulationResponse(value.GetProperty("schema_version").GetString()!, value.GetProperty("status").GetString()!, value.GetProperty("complete").GetBoolean(), value.Clone());
                        var manifest = response.ReadManifest();
                        // Request aliases differ from the provider's reported identity.
                        var expectedProvider = tool == "lu" ? "nalgebra_dense_lu_reference" : "faer_sparse_lu";
                        if (manifest.Backend.Id != expectedProvider || manifest.Backend.Placement != "cpu" || manifest.Backend.Gpu) throw new InvalidDataException("Engine did not execute selected CPU backend.");
                        providerId = manifest.Backend.Id; providerVersion = manifest.Backend.ProviderVersion;
                        var column = Array.FindIndex(manifest.Signals, s => s.Id.Equals(c.Observable, StringComparison.OrdinalIgnoreCase));
                        if (column < 0 || manifest.Signals[column].Unit != c.Unit) throw new InvalidDataException("Missing observable or wrong physical unit.");
                        outputs[tool] = c.Kind == "ac" ? manifest.AcSamples.Select(p => new Point(p.FrequencyHz, p.Values[column].Real, p.Values[column].Imag)).ToArray()
                            : manifest.Samples.Select(p => new Point(c.Kind == "op" ? 0 : p.TimeS, p.Values[column], 0)).ToArray();
                        Tables.Validate(outputs[tool]);
                        newton = Counter(value, "newton", "iterations");
                        solves = Counter(value, "integration", "linear_solves") ?? Counter(value, "newton", "linear_solves");
                        accepted = Counter(value, "integration", "accepted_steps"); rejected = Counter(value, "integration", "rejected_steps");
                    }
                    measurements.Add(new(tool, iteration, iteration < 0, measured.ElapsedMs, measured.PeakWorkingSetBytes,
                        measured.MemoryStatus, tool == "ngspice" ? report.Hashes[c.Deck] : HashBytes(Encoding.UTF8.GetBytes(request)), newton, solves, accepted, rejected, providerId, providerVersion));
                }
                foreach (var tool in tools.Skip(1)) foreach (var coordinate in c.Coordinates)
                {
                    var expected = Tables.At(outputs["ngspice"], coordinate, c.Kind == "transient");
                    var actual = Tables.At(outputs[tool], coordinate, c.Kind == "transient");
                    var passed = Tables.Within(expected, actual, c.AbsoluteTolerance, c.RelativeTolerance);
                    allPassed &= passed;
                    comparisons.Add(new(tool, iteration, iteration < 0, coordinate, expected, actual, c.AbsoluteTolerance, c.RelativeTolerance, passed));
                }
            }
            var summaries = tools.Select(tool =>
            {
                var times = measurements.Where(m => m.Tool == tool && !m.Warmup).Select(m => m.ElapsedMs).Order().ToArray();
                var median = times.Length % 2 == 1 ? times[times.Length / 2] : (times[times.Length / 2 - 1] + times[times.Length / 2]) / 2;
                return new TimingSummary(tool, allPassed, times.Length, allPassed ? median : null, allPassed ? times[0] : null, allPassed ? times[^1] : null);
            }).ToArray();
            if (Hash(executedDeck) != result.DeckSha256) throw new IOException("Executed deck snapshot changed during measurement.");
            report.Cases[^1] = result with { Timings = summaries, AccuracyPassed = allPassed };
        }
        foreach (var input in frozen)
            if (Hash(input.Deck) != report.Hashes[input.Definition.Deck] || Hash(input.Request) != report.Hashes[input.Definition.Request])
                throw new IOException("Benchmark source changed during execution.");
        if (Hash(engine) != report.Hashes["engine_executable"] || Hash(ngspice) != report.Hashes["ngspice_executable"])
            throw new IOException("Benchmark executable changed during execution.");
        if (clock.Elapsed.TotalSeconds > report.BudgetSeconds) throw new TimeoutException("Completed after benchmark budget.");
        return report with { Status = report.Cases.All(c => c.AccuracyPassed) ? "passed" : "failed" };
    }

    private static int? Counter(JsonElement root, string group, string name)
    {
        if (!root.TryGetProperty(group, out var block) || block.ValueKind == JsonValueKind.Null || !block.TryGetProperty(name, out var value)) return null;
        return value.TryGetInt32(out var count) && count >= 0 ? count : throw new InvalidDataException("Malformed engine work counter.");
    }
}
