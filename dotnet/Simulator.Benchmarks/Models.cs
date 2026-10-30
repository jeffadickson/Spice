using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Simulator.Benchmarks;

// Declarative paired workloads keep tolerances, coordinate units and integration
// policy beside the source inputs. None of these records claim universal parity.
internal sealed record CaseDefinition(string Id, string Kind, string Deck, string Request, string Observable,
    string Unit, double[] Coordinates, double AbsoluteTolerance, double RelativeTolerance, string Policy);
internal sealed record SuiteDefinition(string SchemaVersion, CaseDefinition[] Cases);
internal sealed record Measurement(string Tool, int Iteration, bool Warmup, double ElapsedMs,
    long? PeakWorkingSetBytes, string MemoryStatus, string ExecutedInputSha256,
    int? FinalNewtonIterations, int? LinearSolves, int? AcceptedSteps, int? RejectedSteps,
    string ProviderId, string? ProviderVersion);
internal sealed record Comparison(string Tool, int Iteration, bool Warmup, double Coordinate,
    Point Expected, Point Actual, double AbsoluteTolerance, double RelativeTolerance, bool Passed);
internal sealed record TimingSummary(string Tool, bool Eligible, int Repetitions, double? MedianMs, double? MinimumMs, double? MaximumMs);
internal sealed record CaseResult(string Id, string Kind, string CoordinateUnit, string Unit, string Policy, string DeckSha256,
    string RequestSha256, List<Measurement> Measurements, List<Comparison> Comparisons,
    TimingSummary[] Timings, bool AccuracyPassed);
internal sealed record BenchmarkReport(string SchemaVersion, string Status, string? Failure, string SourceCommit,
    bool SourceDirty, string Platform, string HelperMode, int Warmups, int Iterations, int BudgetSeconds,
    long StopwatchFrequency, bool HighResolution, string TimingScope, string MemoryScope,
    Dictionary<string, string> Hashes, string NgspiceVersion, List<CaseResult> Cases, string[] Limitations, string RawDirectory);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SuiteDefinition))]
[JsonSerializable(typeof(BenchmarkReport))]
[JsonSerializable(typeof(JsonNode))]
internal sealed partial class BenchmarkJson : JsonSerializerContext;
