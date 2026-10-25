namespace Simulator.Client;

/// <summary>Unit-aware scalar choice retained for an external variation result.</summary>
public sealed record ScalarSelection(string ElementId, string Kind, string Unit, double Value);

/// <summary>One variation point. Failures retain identity and error instead of vanishing.</summary>
public sealed record ScalarSweepPoint(int Index, ScalarSelection Selection, string Execution,
    ResultManifest? Result, SimulationError? Error);

/// <summary>Explicit accounting for completed scalar variation points.</summary>
public sealed record ScalarSweepSummary(int Total, int Succeeded, int Failed);

/// <summary>
/// Typed projection of the Rust in-memory scalar sweep envelope. The one-shot
/// v1 executable does not yet expose a batch transport; callers may use this
/// DTO when an application persists or receives that envelope directly.
/// </summary>
public sealed record ScalarSweepResult(string SchemaVersion, string BaseCaseId, string SweepSha256,
    ScalarSweepDefinition Sweep, ScalarSweepPoint[] Points, ScalarSweepSummary Summary);

/// <summary>Declared values, retained verbatim so duplicate variation values remain visible.</summary>
public sealed record ScalarSweepDefinition(string ElementId, double[] Values);
