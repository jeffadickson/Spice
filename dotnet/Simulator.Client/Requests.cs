using System.Text.Json;
using System.Text.Json.Serialization;
namespace Simulator.Client;

/// <summary>Experimental v1 request. Values are SI; optional values are omitted rather than null.</summary>
public sealed record SimulationRequest(string CaseId, Circuit Circuit, Analysis Analysis, string Backend = "lu", Experiment? Experiment = null)
{
    public string SchemaVersion => "1";
}
public sealed record Circuit(string[] Nodes, Element[] Elements);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Resistor), "resistor")]
[JsonDerivedType(typeof(Capacitor), "capacitor")]
[JsonDerivedType(typeof(Inductor), "inductor")]
[JsonDerivedType(typeof(Voltage), "voltage")]
[JsonDerivedType(typeof(Current), "current")]
[JsonDerivedType(typeof(Diode), "diode")]
public abstract record Element(string Id, int P, int N);
public sealed record Resistor(string Id, int P, int N, double Ohms) : Element(Id, P, N);
public sealed record Capacitor(string Id, int P, int N, double Farads) : Element(Id, P, N);
public sealed record Inductor(string Id, int P, int N, double Henries) : Element(Id, P, N);
public sealed record Voltage(string Id, int P, int N, double Volts) : Element(Id, P, N);
public sealed record Current(string Id, int P, int N, double Amps) : Element(Id, P, N);
/// <summary>Shockley diode parameters use SI amps and volts; current flows P to N.</summary>
public sealed record Diode(string Id, int P, int N, double SaturationAmps, double ThermalVolts) : Element(Id, P, N);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OperatingPoint), "operating_point")]
[JsonDerivedType(typeof(OperatingPointNewton), "operating_point_newton")]
[JsonDerivedType(typeof(Transient), "transient")]
[JsonDerivedType(typeof(Ac), "ac")]
[JsonDerivedType(typeof(TransientConfigured), "transient_configured")]
public abstract record Analysis;
public sealed record OperatingPoint : Analysis;
/// <summary>Explicit bounded nonlinear DC convergence policy.</summary>
public sealed record OperatingPointNewton(NewtonOptions Options) : Analysis;
/// <summary>Optional source continuation is explicit so ordinary operating-point semantics remain unchanged.</summary>
public sealed record NewtonOptions(int MaxIterations = 50, double AbsoluteTolerance = 1e-12,
    double RelativeTolerance = 1e-9, ContinuationOptions? Continuation = null, double? GminSiemens = null,
    double VoltageAbsoluteTolerance = 1e-9, double CurrentAbsoluteTolerance = 1e-12);
/// <summary>Bounded DC source-stepping policy; each accepted stage seeds the next stage.</summary>
public sealed record ContinuationOptions(double InitialStep = 0.125, double MinimumStep = 1e-4, int MaxStages = 64);
public sealed record Transient(LegacyTransientOptions Options) : Analysis;
public sealed record LegacyTransientOptions(double StopS, double StepS, int MaxSteps);
public sealed record Ac(double[] FrequenciesHz) : Analysis;
public sealed record TransientConfigured(TransientConfiguration Options) : Analysis;

/// <summary>Method is backward_euler or trapezoidal; explicit initial state follows MNA ordering.</summary>
public sealed record TransientConfiguration(double StopS, double MaxStepS, int MaxSteps = 10000,
    string Method = "backward_euler", double[]? InitialState = null, SourceOverride[]? Sources = null, AdaptiveOptions? Adaptive = null);
public sealed record AdaptiveOptions(double Relative, double VoltageAbsolute, double CurrentAbsolute, double MinStepS, int MaxAttempts);
public sealed record SourceOverride(string Id, Waveform Waveform);
public sealed record Point(double TimeS, double Value);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Constant), "constant")]
[JsonDerivedType(typeof(Pwl), "pwl")]
[JsonDerivedType(typeof(Sine), "sine")]
public abstract record Waveform;
public sealed record Constant(double Value) : Waveform;
public sealed record Pwl(Point[] Points) : Waveform;
public sealed record Sine(double Offset, double Amplitude, double FrequencyHz, double PhaseRad) : Waveform;
public sealed record Experiment(Measurement[] Measurements, Assertion[] Assertions);
public sealed record Measurement(string Id, string Signal, MeasurementOperation Operation);
public sealed record Assertion(string Id, string Measurement, string Unit, double Low, double High);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ValueAt), "value_at")]
[JsonDerivedType(typeof(Minimum), "minimum")]
[JsonDerivedType(typeof(Maximum), "maximum")]
[JsonDerivedType(typeof(Integral), "integral")]
[JsonDerivedType(typeof(Rms), "rms")]
public abstract record MeasurementOperation;
public sealed record ValueAt(double TimeS) : MeasurementOperation;
public sealed record Minimum(double StartS, double StopS) : MeasurementOperation;
public sealed record Maximum(double StartS, double StopS) : MeasurementOperation;
public sealed record Integral(double StartS, double StopS) : MeasurementOperation;
public sealed record Rms(double StartS, double StopS) : MeasurementOperation;

/// <summary>Shared serializer options keep typed C#/F# clients on the canonical wire schema.</summary>
public static class WireFormat
{
    public static JsonSerializerOptions Options { get; } = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver=WireContext.Default
    };
}

