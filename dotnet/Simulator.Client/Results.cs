namespace Simulator.Client;
public sealed record SimulationError(string Code,string Message);
/// <summary>Host-owned materialized result; no borrowed native pointers or hidden waveform interpolation.</summary>
public sealed record ResultManifest(string SchemaVersion, string CaseId, string Status, bool Complete,
    SignalInfo[] Signals, RealSample[] Samples, AcSample[] AcSamples, MeasurementValue[] Measurements,
    AssertionValue[] Assertions, IntegrationInfo? Integration, BackendInfo Backend, string RequestSha256, PowerInfo? DcPower=null,
    NewtonInfo? Newton=null, ConsistencyInfo? Consistency=null, DiagnosticInfo[]? Diagnostics=null);
/// <summary>Accepted nonlinear convergence evidence; absent for linear analyses.</summary>
public sealed record NewtonInfo(int Iterations, double ResidualInf, int DampedSteps, double UpdateInf = 0,
    int LinearSolves = 0, ContinuationInfo? Continuation = null, double? GminSiemens = null,
    GminContinuationInfo? GminContinuation = null);
public sealed record GminContinuationInfo(GminContinuationStageInfo[] Stages, bool FinalUnregularized);
public sealed record GminContinuationStageInfo(double GminSiemens, int Iterations, double ResidualInf);
/// <summary>Aggregate evidence for an opt-in bounded DC source-stepping solve.</summary>
public sealed record ContinuationInfo(int AcceptedStages, int RejectedStages, ContinuationStageInfo[] Stages);
/// <summary>One accepted independent-source scale in a continuation solve.</summary>
public sealed record ContinuationStageInfo(double SourceScale, int Iterations, double ResidualInf,
    double UpdateInf, int DampedSteps);
public sealed record PowerInfo(DevicePower[] Devices,double SumAbsorbedW,double SumAbsoluteW);
public sealed record DevicePower(string Id,double AbsorbedW);
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


/// <summary>Independent physical checks with explicit unavailable evidence and SI bounds.</summary>
public sealed record ConsistencyInfo(ConsistencyCheck[] Checks);
public sealed record ConsistencyCheck(string Id, string Status, double? Discrepancy, double? Tolerance, string Unit, string? Reason);
/// <summary>Bounded preflight findings; warnings do not imply full topology classification.</summary>
public sealed record DiagnosticInfo(string Code, string Severity, string Subject, string Message);