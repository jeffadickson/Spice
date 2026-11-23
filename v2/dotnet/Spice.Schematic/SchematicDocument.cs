using Spice.Ir;

namespace Spice.Schematic;

/// <summary>
/// Kind of simulation model attached to a custom library component or embedded in a schematic.
/// </summary>
public enum AttachedModelKind
{
    SpiceModelCard,
    Subcircuit,
    Osdi
}

/// <summary>
/// Attached SPICE .MODEL card, .SUBCKT block, or OSDI compiled model reference bound
/// to a custom component or schematic document (ARCH-SCHEM-001, ARCH-SCHEM-005).
/// </summary>
public sealed record AttachedModelDefinition(
    AttachedModelKind Kind,
    string ModelName,
    string SpiceText,
    string? OsdiLibraryPath = null,
    string? Description = null);

/// <summary>
/// Ordered pin definition on a custom component symbol. Pin order is deterministic
/// netlist pin order (matching RSpice CDF/symbol semantics).
/// </summary>
public sealed record CustomPinDefinition(
    string Name,
    double LocalX,
    double LocalY,
    string Role = "passive");

/// <summary>
/// User-defined or foundation library component binding a symbol, ordered pins,
/// default parameters, and an attached simulation model (.MODEL, .SUBCKT, or .OSDI).
/// </summary>
public sealed record CustomComponentDefinition(
    string Id,
    string Name,
    string Category,
    string Description,
    string BaseDeviceKindId,
    string SpicePrefix,
    string SymbolStyle,
    IReadOnlyList<CustomPinDefinition> Pins,
    IReadOnlyDictionary<string, double> DefaultParameters,
    AttachedModelDefinition? AttachedModel = null,
    string? CustomSvgPath = null,
    bool IsBuiltIn = false,
    string SchemaVersion = "2.1",
    string? ContentSha256 = null);

/// <summary>
/// Free-standing schematic annotation or active SPICE directive card (.PARAM, .TEMP, .OPTIONS, .INCLUDE).
/// </summary>
public sealed record SheetAnnotation(
    string Id,
    double X,
    double Y,
    string Text,
    bool IsSpiceDirective = false);

/// <summary>
/// Hierarchical interface port pin placed on a schematic sheet so the sheet can be
/// compiled into a reusable .SUBCKT custom component symbol.
/// </summary>
public sealed record SheetPort(
    string Name,
    double X,
    double Y,
    string Direction = "inout");

/// <summary>
/// Provenance and integrity metadata persisted in versioned .spice.json save files.
/// </summary>
public sealed record SchematicMetadata(
    string? Author = null,
    string? Description = null,
    string? ModifiedUtc = null,
    string? ContentSha256 = null);

/// <summary>
/// Versioned, deterministic schematic document model bound to Unified Device
/// Descriptors (ARCH-SCHEM-001, ARCH-SCHEM-002, ARCH-SCHEM-005).
/// </summary>
public sealed record SchematicSheet(
    string SheetId,
    string Title,
    IReadOnlyList<PlacedComponent> Components,
    IReadOnlyList<WireNet> Nets,
    IReadOnlyList<SheetAnnotation>? Annotations = null,
    IReadOnlyList<SheetPort>? Ports = null,
    double GridPitch = 20.0,
    string GridStyle = "lines");

public sealed record PlacedComponent(
    string InstanceId,
    string DeviceKindId,
    double X,
    double Y,
    int RotationDegrees,
    IReadOnlyDictionary<string, double> Parameters,
    bool MirrorX = false,
    bool MirrorY = false,
    string? ModelName = null,
    string? LibraryPartId = null,
    string? DisplayValue = null);

public sealed record WireNet(
    string NetName,
    IReadOnlyList<PinRef> ConnectedPins,
    bool IsGlobalGround = false);

public sealed record PinRef(string InstanceId, string PinName);

public sealed record SchematicDocument(
    string SchemaVersion,
    string Name,
    SchematicSheet RootSheet,
    IReadOnlyList<SchematicSheet>? SubSheets = null,
    string FormatId = "spice.schematic.v2",
    SchematicMetadata? Metadata = null,
    IReadOnlyList<AttachedModelDefinition>? EmbeddedModels = null,
    IReadOnlyList<CustomComponentDefinition>? EmbeddedParts = null);
