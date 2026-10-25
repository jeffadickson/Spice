namespace Simulator.Client;
/// <summary>Host-owned materialized result; no borrowed native pointers or hidden waveform interpolation.</summary>
public sealed record ResultManifest(string SchemaVersion, string CaseId, string Status, bool Complete,
    SignalInfo[] Signals, RealSample[] Samples, AcSample[] AcSamples, MeasurementValue[] Measurements,
    AssertionValue[] Assertions, IntegrationInfo? Integration, BackendInfo Backend, string RequestSha256);
public sealed record SignalInfo(string Id, string Quantity, string Unit);
public sealed record RealSample(double TimeS, double[] Values);
public sealed record ComplexValue(double Real, double Imag);
public sealed record AcSample(double FrequencyHz, ComplexValue[] Values);
public sealed record MeasurementValue(string Id, string Signal, string Unit, string Status, double? Value, string? Reason);
public sealed record AssertionValue(string Id, string Measurement, string Outcome, string? Reason);
public sealed record IntegrationInfo(string Method, string Initialization, int AcceptedSteps, int RejectedSteps,
    int Attempts, double MaxAcceptedError, int LinearSolves, int PreparedFactors, int PreparedReuses);
public sealed record BackendInfo(string Id, string Placement, int MaxUnknowns, bool Sparse, bool Gpu,
    bool Complex, bool PreparedReal, string ProviderVersion);

