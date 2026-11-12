namespace Simulator.Client;
/// <summary>Stable machine error class and human-readable native diagnostic; neither implies a successful result.</summary>
public sealed record SimulationError(string Code,string Message);
/// <summary>Host-owned materialized result; no borrowed native pointers or hidden waveform interpolation.</summary>
public sealed record ResultManifest(string SchemaVersion, string CaseId, string Status, bool Complete,
    SignalInfo[] Signals, RealSample[] Samples, AcSample[] AcSamples, MeasurementValue[] Measurements,
    AssertionValue[] Assertions, IntegrationInfo? Integration, BackendInfo Backend, string RequestSha256, PowerInfo? DcPower=null,
    NewtonInfo? Newton=null, ConsistencyInfo? Consistency=null, DiagnosticInfo[]? Diagnostics=null);
/// <summary>Accepted nonlinear convergence evidence; absent for linear analyses.
/// Iterations and DampedSteps describe the final stage. LinearSolves includes
/// all real correction calls across accepted/rejected recovery stages.</summary>
public sealed record NewtonInfo(int Iterations, double ResidualInf, int DampedSteps, double UpdateInf = 0,
    int LinearSolves = 0, ContinuationInfo? Continuation = null, double? GminSiemens = null,
    GminContinuationInfo? GminContinuation = null);
/// <summary>Ordered regularized warm starts and explicit evidence that the returned state removed Gmin.</summary>
public sealed record GminContinuationInfo(GminContinuationStageInfo[] Stages, bool FinalUnregularized);
/// <summary>One accepted shunt conductance in siemens, with its iteration count and residual infinity norm.</summary>
public sealed record GminContinuationStageInfo(double GminSiemens, int Iterations, double ResidualInf);
/// <summary>Aggregate evidence for an opt-in bounded DC source-stepping solve.</summary>
public sealed record ContinuationInfo(int AcceptedStages, int RejectedStages, ContinuationStageInfo[] Stages);
/// <summary>One accepted independent-source scale in a continuation solve.</summary>
public sealed record ContinuationStageInfo(double SourceScale, int Iterations, double ResidualInf,
    double UpdateInf, int DampedSteps);
/// <summary>Signed absorbed device power and independent balance/absolute-scale sums, all in watts.</summary>
public sealed record PowerInfo(DevicePower[] Devices,double SumAbsorbedW,double SumAbsoluteW);
/// <summary>Device identity and absorbed watts; a source delivering energy has negative absorbed power.</summary>
public sealed record DevicePower(string Id,double AbsorbedW);
/// <summary>Column identity, physical quantity and SI unit shared by all ordered sample vectors.</summary>
public sealed record SignalInfo(string Id, string Quantity, string Unit);
/// <summary>Recorded time in seconds and a value for each signal descriptor, without hidden interpolation.</summary>
public sealed record RealSample(double TimeS, double[] Values);
/// <summary>Real and imaginary parts of one complex phasor in its signal's declared unit.</summary>
public sealed record ComplexValue(double Real, double Imag);
/// <summary>Frequency in hertz and ordered complex signal values around the declared bias.</summary>
public sealed record AcSample(double FrequencyHz, ComplexValue[] Values);
/// <summary>Scalar measurement outcome; unavailable values retain a reason and are never silently zero.</summary>
public sealed record MeasurementValue(string Id, string Signal, string Unit, string Status, double? Value, string? Reason);
/// <summary>Bound-check outcome linked to a measurement, with an explicit failure/unavailable reason.</summary>
public sealed record AssertionValue(string Id, string Measurement, string Outcome, string? Reason);
/// <summary>Accepted/rejected integration work and factor reuse; MaxAcceptedError is the controller's scaled error. Optional InitialNewton retains actual nonlinear time-zero recovery evidence separately from transient step work.</summary>
public sealed record IntegrationInfo(string Method, string Initialization, int AcceptedSteps, int RejectedSteps,
    int Attempts, double MaxAcceptedError, int LinearSolves, int PreparedFactors, int PreparedReuses, NewtonInfo? InitialNewton = null,
    double[]? BackwardEulerRestarts = null, double? EffectiveCurrentAbsoluteTolerance = null,
    int? RetainedMidpoints = null, int? FailedNonlinearTrials = null);
/// <summary>Selected provider capabilities and version, without implying full numerical qualification.</summary>
public sealed record BackendInfo(string Id, string Placement, int MaxUnknowns, bool Sparse, bool Gpu,
    bool Complex, bool PreparedReal, string ProviderVersion);


/// <summary>Independent physical checks with explicit unavailable evidence and SI bounds.</summary>
public sealed record ConsistencyInfo(ConsistencyCheck[] Checks);
/// <summary>One dimensional physical discrepancy and bound; absent values preserve a not-checked reason.</summary>
public sealed record ConsistencyCheck(string Id, string Status, double? Discrepancy, double? Tolerance, string Unit, string? Reason);
/// <summary>Bounded preflight findings; warnings do not imply full topology classification.</summary>
public sealed record DiagnosticInfo(string Code, string Severity, string Subject, string Message);
