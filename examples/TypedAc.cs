#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
using Simulator.Client;
// SI values; exp(j*omega*t) phase convention. This amplitude override leaves DC bias intact.
if (args.Length != 1) throw new ArgumentException("Expected simulator executable.");
var request = new SimulationRequest("typed-ac",
    new Circuit(["0", "in", "out"], [new Voltage("V1", 1, 0, 1),
        new Resistor("R1", 1, 2, 1000), new Capacitor("C1", 2, 0, 1e-6)]),
    new Ac([1.0 / (2.0 * Math.PI * 0.001)], [new AcSourcePhasor("V1", 1, 2)]), "faer");
var manifest = (await new SimulationClient(Path.GetFullPath(args[0])).RunAsync(request)).ReadManifest();
var index = Array.FindIndex(manifest.Signals, signal => signal.Id == "v(out)");
if (index < 0) throw new InvalidDataException("Missing output voltage.");
var value = manifest.AcSamples[0].Values[index];
Console.WriteLine($"v(out)={value.Real}+j*{value.Imag} V; expected 1.5+j*0.5 V");
