using System.Text.Json.Serialization;
namespace Simulator.Client;

// Source-generated metadata keeps the public DTO path usable in .NET file apps and Native AOT.
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull,GenerationMode=JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SimulationRequest))]
[JsonSerializable(typeof(ResultManifest))]
[JsonSerializable(typeof(SimulationError))]
[JsonSerializable(typeof(ScalarSweepResult))]
internal partial class WireContext : JsonSerializerContext;
