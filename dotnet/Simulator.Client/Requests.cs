using System.Text.Json;
using System.Text.Json.Serialization;
namespace Simulator.Client;

/// <summary>Experimental v1 request. Values are SI; optional values are omitted rather than null.</summary>
public sealed record SimulationRequest(string CaseId, Circuit Circuit, Analysis Analysis, string Backend = "lu", Experiment? Experiment = null)
{
    /// <summary>Experimental JSON wire version supported by this authoring contract.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Source-generated wire serialization requires this instance property.")]
    public string SchemaVersion => "1";
}
/// <summary>Ordered node names and element instances; node zero is the reference and indices follow Nodes.</summary>
public sealed record Circuit(string[] Nodes, Element[] Elements);

/// <summary>Two-terminal instance with unique identity; P-to-N defines signed current and voltage.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Resistor), "resistor")]
[JsonDerivedType(typeof(Capacitor), "capacitor")]
[JsonDerivedType(typeof(Inductor), "inductor")]
[JsonDerivedType(typeof(Voltage), "voltage")]
[JsonDerivedType(typeof(Current), "current")]
[JsonDerivedType(typeof(Vcvs), "vcvs")]
[JsonDerivedType(typeof(BehavioralVoltage), "behavioral_voltage")]
[JsonDerivedType(typeof(BehavioralResistor), "behavioral_resistor")]
[JsonDerivedType(typeof(PspiceSwitch), "pspice_switch")]
[JsonDerivedType(typeof(Vccs), "vccs")]
[JsonDerivedType(typeof(Cccs), "cccs")]
[JsonDerivedType(typeof(VoltageScaledCccs), "voltage_scaled_cccs")]
[JsonDerivedType(typeof(Ccvs), "ccvs")]
[JsonDerivedType(typeof(Diode), "diode")]
[JsonDerivedType(typeof(DiodeWithCapacitance), "diode_with_capacitance")]
[JsonDerivedType(typeof(SpiceDiode), "spice_diode")]
[JsonDerivedType(typeof(JunctionStorage), "junction_storage")]
[JsonDerivedType(typeof(Bsim330), "bsim330")]
[JsonDerivedType(typeof(BsimSoiFdReference), "bsim_soi_fd_reference")]
[JsonDerivedType(typeof(Mos1Channel), "mos1_channel")]
public abstract record Element(string Id, int P, int N);
/// <summary>Positive resistance in ohms; validated by the engine before assembly.</summary>
public sealed record Resistor(string Id, int P, int N, double Ohms) : Element(Id, P, N);
/// <summary>Positive capacitance in farads, with charge defined by P-to-N voltage.</summary>
public sealed record Capacitor(string Id, int P, int N, double Farads) : Element(Id, P, N);
/// <summary>Positive inductance in henries; its branch current participates in MNA state ordering.</summary>
public sealed record Inductor(string Id, int P, int N, double Henries) : Element(Id, P, N);
/// <summary>Independent signed DC voltage from P to N in volts.</summary>
public sealed record Voltage(string Id, int P, int N, double Volts) : Element(Id, P, N);
/// <summary>Independent signed DC current flowing P to N in amperes.</summary>
public sealed record Current(string Id, int P, int N, double Amps) : Element(Id, P, N);
/// <summary>Ideal voltage-controlled voltage source: V(P)-V(N)=Gain*(V(Cp)-V(Cn)); gain is dimensionless.</summary>
public sealed record Vcvs(string Id,int P,int N,int Cp,int Cn,double Gain) : Element(Id,P,N);
/// <summary>Ideal voltage-controlled current source: current P-to-N equals Siemens*(V(Cp)-V(Cn)).</summary>
public sealed record Vccs(string Id,int P,int N,int Cp,int Cn,double Siemens) : Element(Id,P,N);
/// <summary>Output current P-to-N equals Gain times the P-to-N branch current of ControlId; gain is dimensionless.</summary>
public sealed record Cccs(string Id,int P,int N,string ControlId,double Gain):Element(Id,P,N);
/// <summary>Memoryless P-to-N current Gain*I(ControlId)*(1+VoltageCoefficient*(V(Cp)-V(Cn))). Gain is dimensionless, VoltageCoefficient is inverse volts; controls add no current draw or storage.</summary>
public sealed record VoltageScaledCccs(string Id,int P,int N,int Cp,int Cn,string ControlId,double Gain,double VoltageCoefficient):Element(Id,P,N);
/// <summary>Output voltage P-to-N equals Ohms times the P-to-N branch current of ControlId.</summary>
public sealed record Ccvs(string Id,int P,int N,string ControlId,double Ohms):Element(Id,P,N);
/// <summary>Smooth PSpice VSWITCH, ohms/volts. Current P-to-N is (Vp-Vn)/R(Vcp-Vcn); control is high impedance.</summary>
public sealed record PspiceSwitch(string Id,int P,int N,int Cp,int Cn,double Ron,double Roff,double Von,double Voff):Element(Id,P,N);
/// <summary>Shockley diode parameters use SI amps and volts; current flows P to N.</summary>
public sealed record Diode(string Id, int P, int N, double SaturationAmps, double ThermalVolts) : Element(Id, P, N);
/// <summary>Effective SI junction parameters at a fixed temperature. Depletion
/// and diffusion charge use CJO,VJ,M,FC,TT; no series resistance is hidden in Q.</summary>
public sealed record SpiceDiodeModel(double SaturationAmps,double ThermalVolts,double JunctionFarads,
    double JunctionVolts,double Grading,double ForwardCoefficient,double TransitSeconds);
/// <summary>Junction with nonlinear SPICE depletion and diffusion storage.
/// The i_conduction observable excludes displacement current.</summary>
public sealed record SpiceDiode(string Id,int P,int N,SpiceDiodeModel Model):Element(Id,P,N);
/// <summary>Pure depletion/transport junction storage with zero static F. Model
/// saturation current scales diffusion Q only; current senses remain separate.</summary>
public sealed record JunctionStorage(string Id,int P,int N,SpiceDiodeModel Model):Element(Id,P,N)
{
    /// <summary>Optional normalized collector control for VAF-only forward diffusion charge.</summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public EarlyChargeControl? EarlyEffect { get; init; }
}
/// <summary>Normalized BC control nodes and positive forward Early voltage in volts.</summary>
public sealed record EarlyChargeControl(int Cp,int Cn,double Volts);
/// <summary>Shockley conduction in parallel with constant Q=C*V; SI amps, volts and
/// strictly positive farads. This bounded profile has no depletion or transport charge.
/// Derived i_conduction(id) excludes displacement; total current requires circuit KCL.</summary>
public sealed record DiodeWithCapacitance(string Id, int P, int N, double SaturationAmps,
    double ThermalVolts, double Farads) : Element(Id, P, N);

/// <summary>Explicitly selected bounded analysis; unsupported policies fail rather than silently changing methods.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OperatingPoint), "operating_point")]
[JsonDerivedType(typeof(OperatingPointNewton), "operating_point_newton")]
[JsonDerivedType(typeof(Transient), "transient")]
[JsonDerivedType(typeof(Ac), "ac")]
[JsonDerivedType(typeof(TransientConfigured), "transient_configured")]
public abstract record Analysis;
/// <summary>Nominal DC excitation, automatically using Newton when nonlinear devices are present.</summary>
public sealed record OperatingPoint : Analysis;
/// <summary>Explicit bounded nonlinear DC convergence policy.</summary>
public sealed record OperatingPointNewton(NewtonOptions Options) : Analysis;
/// <summary>Optional source continuation is explicit so ordinary operating-point semantics remain unchanged.</summary>
/// <remarks>AbsoluteTolerance is a validated legacy wire field. Convergence uses
/// VoltageAbsoluteTolerance and CurrentAbsoluteTolerance with their SI dimensions.</remarks>
public sealed record NewtonOptions(int MaxIterations = 50, double AbsoluteTolerance = 1e-12,
    double RelativeTolerance = 1e-9, ContinuationOptions? Continuation = null, double? GminSiemens = null,
    double VoltageAbsoluteTolerance = 1e-9, double CurrentAbsoluteTolerance = 1e-12,
    GminContinuationOptions? GminContinuation = null);
/// <summary>Bounded DC source-stepping policy; each accepted stage seeds the next stage.</summary>
public sealed record ContinuationOptions(double InitialStep = 0.125, double MinimumStep = 1e-4, int MaxStages = 64);
/// <summary>Bounded DC Gmin warm starts followed by one mandatory unregularized final solve.</summary>
public sealed record GminContinuationOptions(double InitialSiemens = 1e-3, double MinimumSiemens = 1e-12,
    double ReductionFactor = 0.1, int MaxStages = 16);
/// <summary>Legacy fixed-step backward-Euler transient request.</summary>
public sealed record Transient(LegacyTransientOptions Options) : Analysis;
/// <summary>Stop/step times in seconds and an explicit accepted-step budget.</summary>
public sealed record LegacyTransientOptions(double StopS, double StepS, int MaxSteps);
/// <summary>Small-signal frequency sweep in hertz, around the DC bias for nonlinear circuits.</summary>
[method: JsonConstructor]
public sealed record Ac(double[] FrequenciesHz, AcSourcePhasor[]? SourcePhasors = null) : Analysis
{
    /// <summary>Retain the original one-argument constructor signature for existing clients.</summary>
    public Ac(double[] FrequenciesHz) : this(FrequenciesHz, null) { }
    /// <summary>Retain the original one-value deconstruction signature in addition to the phasor form.</summary>
    public void Deconstruct(out double[] FrequenciesHz) => FrequenciesHz = this.FrequenciesHz;
}
/// <summary>Replace one independent source AC amplitude in volts or amperes;
/// real/imaginary components use exp(j*omega*t). The DC bias value is unchanged.</summary>
public sealed record AcSourcePhasor(string Id, double Real, double Imag);
/// <summary>Configured dynamic integration with explicit method, history and source policy.</summary>
public sealed record TransientConfigured(TransientConfiguration Options) : Analysis;

/// <summary>Method is backward_euler or trapezoidal. InitialState uses Circuit.Nodes excluding node zero, followed by V/L/E/H branch currents in Circuit.Elements order; voltages are volts and branch currents are P-to-N amperes. Optional InitialNewton controls nonlinear time-zero bias only; it is exclusive with InitialState and fixed Gmin is unsupported. Continuation requires actual time-zero sources to equal nominal DC.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1708:Identifiers should differ by more than case",
    Justification = "Retain existing named-argument/wire compatibility: MaxStepS is seconds and MaxSteps is a count.")]
public sealed record TransientConfiguration(double StopS, double MaxStepS, int MaxSteps = 10000,
    string Method = "backward_euler", double[]? InitialState = null, SourceOverride[]? Sources = null, AdaptiveOptions? Adaptive = null,
    NewtonOptions? InitialNewton = null)
{
    /// <summary>Requested upper bound on nonlinear current convergence error in amperes.
    /// The engine retains its stricter physical-consistency floor and reports the effective bound.</summary>
    public double? CurrentAbsoluteTolerance { get; init; }
}
/// <summary>Step-doubling tolerances in relative, volts and amperes, with minimum seconds and attempt budget.
/// CurrentRelative optionally separates derivative-current accuracy from voltage-state accuracy; null retains Relative.</summary>
public sealed record AdaptiveOptions(double Relative, double VoltageAbsolute, double CurrentAbsolute, double MinStepS, int MaxAttempts,
    double? CurrentRelative = null);
/// <summary>Bind a time waveform to an existing independent source by its identity.</summary>
public sealed record SourceOverride(string Id, Waveform Waveform);
/// <summary>One waveform breakpoint in seconds and the bound source's SI quantity.</summary>
public sealed record Point(double TimeS, double Value);
/// <summary>Source excitation over time; amplitudes retain the source's voltage or current unit.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Constant), "constant")]
[JsonDerivedType(typeof(Pwl), "pwl")]
[JsonDerivedType(typeof(Sine), "sine")]
[JsonDerivedType(typeof(DampedSine), "damped_sine")]
[JsonDerivedType(typeof(Pulse), "pulse")]
[JsonDerivedType(typeof(Exponential), "exponential")]
[JsonDerivedType(typeof(FrequencyModulated), "frequency_modulated")]
[JsonDerivedType(typeof(AmplitudeModulated), "amplitude_modulated")]
public abstract record Waveform;
/// <summary>Time-independent source value in its SI unit.</summary>
public sealed record Constant(double Value) : Waveform;
/// <summary>Piecewise-linear source samples with engine-validated time ordering.</summary>
public sealed record Pwl(Point[] Points) : Waveform;
/// <summary>Sinusoidal source with hertz frequency and radian phase.</summary>
public sealed record Sine(double Offset, double Amplitude, double FrequencyHz, double PhaseRad) : Waveform;
/// <summary>Delayed/damped sinusoid; delay seconds, damping inverse seconds, phase radians. Before delay the phase-biased initial value is held.</summary>
public sealed record DampedSine(double Offset,double Amplitude,double FrequencyHz,double DelayS,double DampingPerS,double PhaseRad):Waveform;
/// <summary>Finite-edge periodic pulse with all timing fields in seconds. Engine validates nonoverlapping edges.</summary>
public sealed record Pulse(double Low,double High,double DelayS,double RiseS,double FallS,double WidthS,double PeriodS):Waveform;
/// <summary>Exponential rise/return with delays and strictly positive time constants in seconds.</summary>
public sealed record Exponential(double Initial,double Pulsed,double RiseDelayS,double RiseTauS,double FallDelayS,double FallTauS):Waveform;
/// <summary>FM with hertz frequencies, radian phases and seconds delay. Zero through delay; engine requires index in [0, carrier/modulation frequency ratio].</summary>
public sealed record FrequencyModulated(double Offset,double Amplitude,double CarrierHz,double ModulationIndex,double ModulationHz,double DelayS,double ModulationPhaseRad,double CarrierPhaseRad):Waveform;
/// <summary>AM with hertz frequencies, radian phases, seconds delay and source-unit offset/amplitudes. Zero through delay.</summary>
public sealed record AmplitudeModulated(double Offset,double ModulationOffset,double ModulationAmplitude,double ModulationHz,double CarrierHz,double DelayS,double ModulationPhaseRad,double CarrierPhaseRad):Waveform;
/// <summary>Requested scalar measurements and unit-aware bounds, evaluated from recorded results.</summary>
public sealed record Experiment(Measurement[] Measurements, Assertion[] Assertions);
/// <summary>Named scalar operation over a signal descriptor ID.</summary>
public sealed record Measurement(string Id, string Signal, MeasurementOperation Operation);
/// <summary>Inclusive accepted scalar interval in an explicit unit; missing values cannot pass.</summary>
public sealed record Assertion(string Id, string Measurement, string Unit, double Low, double High);
/// <summary>Bounded recorded-sample measurement, without hidden waveform interpolation guarantees.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ValueAt), "value_at")]
[JsonDerivedType(typeof(Minimum), "minimum")]
[JsonDerivedType(typeof(Maximum), "maximum")]
[JsonDerivedType(typeof(Integral), "integral")]
[JsonDerivedType(typeof(Rms), "rms")]
public abstract record MeasurementOperation;
/// <summary>Recorded signal evaluation at the requested time in seconds.</summary>
public sealed record ValueAt(double TimeS) : MeasurementOperation;
/// <summary>Minimum over a bounded time window in seconds.</summary>
public sealed record Minimum(double StartS, double StopS) : MeasurementOperation;
/// <summary>Maximum over a bounded time window in seconds.</summary>
public sealed record Maximum(double StartS, double StopS) : MeasurementOperation;
/// <summary>Time integral over the declared seconds, carrying signal-unit times seconds.</summary>
public sealed record Integral(double StartS, double StopS) : MeasurementOperation;
/// <summary>Root-mean-square recorded signal over the declared seconds.</summary>
public sealed record Rms(double StartS, double StopS) : MeasurementOperation;

/// <summary>Shared serializer options keep typed C#/F# clients on the canonical wire schema.</summary>
public static class WireFormat
{
    /// <summary>Source-generated snake-case JSON metadata with absent optional fields omitted.</summary>
    public static JsonSerializerOptions Options { get; } = new() {
        // A validated expression permits 64 levels; allow the enclosing request and circuit objects too.
        MaxDepth = 128,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver=WireContext.Default
    };
}

/// <summary>Effective MOS1 SI channel parameters at the nominal temperature; beta includes W/L.</summary>
public sealed record Mos1Model(bool PChannel,double ThresholdVolts,double BetaAmpsPerVoltSquared,
    double BodyEffectSqrtVolts,double SurfacePotentialVolts,double ChannelLengthModulationPerVolt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double OxideFarads = 0);
/// <summary>Drain P to source N channel only. Gate and Bulk participate in optional intrinsic Meyer displacement.</summary>
public sealed record Mos1Channel(string Id,int P,int N,int Gate,int Bulk,Mos1Model Model):Element(Id,P,N);

/// <summary>Explicit bulk BSIM3 3.3.0 model and instance parameters.</summary>
public sealed record Bsim330Model(int Polarity,double Tox,double Nch,double Nsub,double Vth0,double W,double L,
    double Pd,double Ps,double Ad,[property: System.Text.Json.Serialization.JsonPropertyName("as")] double SourceArea);

/// <summary>Four-terminal device backed by the opt-in native DC provider.</summary>
public sealed record Bsim330(string Id,int P,int N,int Gate,int Bulk,Bsim330Model Model):Element(Id,P,N);

/// <summary>Pinned Berkeley FD SOI static reference device with an independent back gate.</summary>
public sealed record BsimSoiFdReference(string Id,int P,int N,int Gate,int BackGate):Element(Id,P,N);
