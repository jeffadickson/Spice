using Spice.Ir;

namespace Spice.Schematic;

/// <summary>
/// Versioned, deterministic schematic document model bound to Unified Device
/// Descriptors (ARCH-SCHEM-001, ARCH-SCHEM-002, ARCH-SCHEM-005).
/// </summary>
public sealed record SchematicSheet(
    string SheetId,
    string Title,
    IReadOnlyList<PlacedComponent> Components,
    IReadOnlyList<WireNet> Nets);

public sealed record PlacedComponent(
    string InstanceId,
    string DeviceKindId,
    double X,
    double Y,
    int RotationDegrees,
    IReadOnlyDictionary<string, double> Parameters);

public sealed record WireNet(
    string NetName,
    IReadOnlyList<PinRef> ConnectedPins,
    bool IsGlobalGround = false);

public sealed record PinRef(string InstanceId, string PinName);

public sealed record SchematicDocument(
    string SchemaVersion,
    string Name,
    SchematicSheet RootSheet,
    IReadOnlyList<SchematicSheet>? SubSheets = null);
