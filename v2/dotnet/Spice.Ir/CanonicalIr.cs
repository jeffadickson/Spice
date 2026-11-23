namespace Spice.Ir;

/// <summary>
/// Single fail-closed Unified Device Descriptor shared across schematic capture,
/// ERC, StarLark-cs authoring, Canonical IR emission, and native/Xyce execution
/// (ARCH-SCHEM-001).
/// </summary>
public sealed record DeviceDescriptor(
    string KindId,
    string DisplayName,
    IReadOnlyList<PinDescriptor> Pins,
    IReadOnlyList<ParameterDescriptor> Parameters,
    string? SymbolId = null);

public sealed record PinDescriptor(string Name, string Role);

public sealed record ParameterDescriptor(
    string Name,
    string SiUnit,
    double? DefaultValue = null,
    bool Required = false);

public sealed record DeviceInstanceIr(
    string Id,
    string KindId,
    IReadOnlyDictionary<string, string> PinConnections,
    IReadOnlyDictionary<string, double> Parameters,
    string? ModelName = null,
    IReadOnlyList<string>? PinOrder = null);

public sealed record ModelDefinitionIr(
    string Name,
    string ModelType,
    IReadOnlyDictionary<string, double> Parameters,
    string? OsdiLibraryPath = null,
    string? OsdiModelName = null,
    string? RawSpiceDefinition = null);

/// <summary>
/// Immutable frontend-agnostic circuit representation emitted by StarLark-cs,
/// Spice.Schematic, C#/F# builders, and SPICE parsers (ARCH-API-001).
/// </summary>
public sealed record CircuitIr(
    string Name,
    IReadOnlyList<string> Nodes,
    IReadOnlyList<DeviceInstanceIr> Devices,
    IReadOnlyDictionary<string, string>? Provenance = null,
    IReadOnlyList<ModelDefinitionIr>? Models = null,
    IReadOnlyList<string>? Directives = null);

public enum AnalysisKind
{
    OperatingPoint,
    DcSweep,
    Ac,
    Transient,
    Noise,
    HarmonicBalance,
    PeriodicSteadyState,
    SParameters
}

public sealed record SimulationRequestIr(
    string CaseId,
    CircuitIr Circuit,
    AnalysisKind Analysis,
    IReadOnlyDictionary<string, double>? Options = null,
    bool EnablePhysicalAudits = true,
    string? SweepSourceId = null);

public enum RunTerminalStatus
{
    Succeeded,
    Failed,
    Cancelled,
    ResourceExhausted
}

public sealed record SignalDescriptor(
    string Id,
    string Quantity,
    string SiUnit,
    bool IsComplex);

public sealed record RealSeriesIr(
    string AxisName,
    string AxisUnit,
    IReadOnlyList<double> AxisValues,
    IReadOnlyDictionary<string, IReadOnlyList<double>> SignalValues);

public sealed record ComplexSample(double Real, double Imaginary);

public sealed record ComplexSeriesIr(
    string AxisName,
    string AxisUnit,
    IReadOnlyList<double> AxisValues,
    IReadOnlyDictionary<string, IReadOnlyList<ComplexSample>> SignalValues);

public sealed record ResultManifestIr(
    string CaseId,
    string BackendId,
    RunTerminalStatus Status,
    bool Complete,
    IReadOnlyList<SignalDescriptor> Signals,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    RealSeriesIr? RealData = null,
    ComplexSeriesIr? ComplexData = null);
