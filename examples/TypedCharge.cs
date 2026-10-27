#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
using Simulator.Client;
// Explicit constant charge, not a general SPICE depletion/transport model. All inputs SI.
if (args.Length != 1) throw new ArgumentException("Expected simulator executable.");
var request = new SimulationRequest("typed-charge", new Circuit(["0", "out"],
    [new Voltage("V1", 1, 0, 0.1), new DiodeWithCapacitance("D1", 1, 0, 1e-12, 0.025, 1e-6)]),
    new Ac([100], [new AcSourcePhasor("V1", 1, 0)]), "faer");
var result = (await new SimulationClient(Path.GetFullPath(args[0])).RunAsync(request)).ReadManifest();
var branch = result.AcSamples[0].Values[1];
// These independent g(V)+j*omega*C values also exercise generated metadata
// after NativeAOT publication; publishing alone is not behavioral evidence.
if (Math.Abs(branch.Real + 1e-12 * Math.Exp(4) / 0.025) > 1e-14 ||
    Math.Abs(branch.Imag + 2 * Math.PI * 100 * 1e-6) > 1e-14 || result.Signals[2].Id != "i_conduction(D1)")
    throw new InvalidDataException("Constant-charge typed equation mismatch.");
Console.WriteLine($"Source branch: {result.AcSamples[0].Values[1]}; diode observable: {result.Signals[2].Id}");
