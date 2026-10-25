// Usage: dotnet run --file scripts/Plan-NgspiceFeatureFamilies.cs
// Emits the stable, intentionally finite 100-family compatibility campaign.
// Status is a planning state, never a claim that an ngspice construct works.
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var text = """
F001|Netlist title and comment records|supported
F002|Ground aliases and node naming|supported
F003|Resistor cards and SI suffixes|supported
F004|Capacitor cards|supported
F005|Inductor cards|supported
F006|Independent DC voltage sources|supported
F007|Independent DC current sources|supported
F008|Operating-point directive|supported
F009|End-of-deck directive|supported
F010|Resource-limit rejection|supported
F011|Transient directive grammar|import_only
F012|Transient time-grid semantics|future
F013|Transient initial conditions|future
F014|Transient breakpoint scheduling|future
F015|Pulse source waveform|supported
F016|Sine source waveform|supported
F017|Piecewise-linear waveform|supported
F018|Exponential waveform|supported
F019|Frequency-modulated waveform|future
F020|Behavioral source waveform|future
F021|AC directive grammar|import_only
F022|Linear AC frequency grid|supported_api_only
F023|AC DEC grid|supported
F024|AC OCT grid|supported
F025|AC LIN grid|supported
F026|AC source amplitude and phase|supported
F027|AC-qualified resistor values|supported
F028|AC-qualified capacitor values|supported
F029|AC-qualified inductor values|supported
F030|Small-signal nonlinear bias|supported_api_only
F031|Shockley diode device|supported
F032|Diode model cards|supported
F033|Diode temperature controls|future
F034|BJT level-1|future
F035|MOSFET level-1|future
F036|JFET device|future
F037|MESFET device|future
F038|HFET device|future
F039|VBIC BJT model|future
F040|MESA device model|future
F041|BSIM1 MOS model|future
F042|BSIM2 MOS model|future
F043|BSIM3 SOI FD model|future
F044|BSIM3 SOI PD model|future
F045|BSIM3 SOI DD model|future
F046|MOS level-6 model|future
F047|Subcircuit definitions|future
F048|Subcircuit instantiation|future
F049|Parameter definitions|future
F050|Parameter expressions|future
F051|Include directives|future
F052|Library sections|future
F053|Continuation lines|supported
F054|Quoted identifiers|future
F055|Case preservation rules|future
F056|Control blocks|unsupported
F057|Interactive pipe protocol|unsupported
F058|Alter commands|unsupported
F059|Altermod commands|unsupported
F060|Interactive plot commands|unsupported
F061|Print output directive|import_only
F062|Plot output directive|import_only
F063|Probe output directive|import_only
F064|Measurement directive|future
F065|Fourier analysis|future
F066|Sensitivity analysis|future
F067|Pole-zero analysis|future
F068|Noise analysis|future
F069|Distortion analysis|future
F070|Transfer-function analysis|future
F071|Switch elements|future
F072|Transmission lines|future
F073|Lossless transmission lines|future
F074|Coupled inductors|future
F075|Mutual inductance|future
F076|Polynomial controlled sources|future
F077|Voltage-controlled sources|supported
F078|Current-controlled sources|supported
F079|Arbitrary controlled sources|future
F080|Digital XSPICE code models|future
F081|Mixed-signal bridges|future
F082|Code-model loading|unsupported
F083|External shared model loading|unsupported
F084|Rawfile binary output|future
F085|Rawfile ASCII output|future
F086|CSV/tabular result export|supported_api_only
F087|Arrow result export|future
F088|Parquet result export|future
F089|GPU linear solver provider|future
F090|GPU nonlinear solver provider|future
F091|Sparse direct solver provider|future
F092|Iterative solver provider|future
F093|Newton damping strategy|supported
F094|Newton continuation strategy|future
F095|Convergence diagnostics|supported_api_only
F096|Floating-point determinism|future
F097|Malformed-deck fuzzing|supported
F098|Property-based numerical tests|supported
F099|Upstream oracle execution|planned
F100|Corpus regression manifest|planned
""";
var families = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Select(line => line.Split('|'))
    .Select(parts => new { id = parts[0], family = parts[1], status = parts[2] })
    .ToArray();
if (families.Length != 100 || families.Select(family => family.id).Distinct().Count() != 100)
    throw new InvalidOperationException("The compatibility campaign must contain exactly 100 unique family IDs.");
Console.WriteLine(JsonSerializer.Serialize(families, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
