#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
using Simulator.Client;
// Explicit constant charge, not a general SPICE depletion/transport model. All inputs SI.
if (args.Length != 1) throw new ArgumentException("Expected simulator executable.");
var request = new SimulationRequest("typed-charge", new Circuit(["0", "out"],
    [new Voltage("V1", 1, 0, 0.1), new DiodeWithCapacitance("D1", 1, 0, 1e-12, 0.025, 1e-6)]),
    new Ac([100], [new AcSourcePhasor("V1", 1, 0)]), "faer");
var result = (await new SimulationClient(Path.GetFullPath(args[0])).RunAsync(request)).ReadManifest();
Console.WriteLine($"Source branch: {result.AcSamples[0].Values[1]}; diode observable: {result.Signals[2].Id}");
