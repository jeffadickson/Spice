#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
using Simulator.Client;
// Build a request without hand-written JSON; GUI consumers use this same API.
if (args.Length != 1) throw new ArgumentException("Expected simulator executable.");
var request = new SimulationRequest("typed-csharp",
    new Circuit(["0","in","out"],[new Voltage("V1",1,0,10),new Resistor("R1",1,2,1000),new Resistor("R2",2,0,1000)]),
    new OperatingPoint(),"faer",
    new Experiment([new Measurement("output","v(out)",new ValueAt(0))],[new Assertion("within-bounds","output","V",4.99,5.01)]));
var result=(await new SimulationClient(Path.GetFullPath(args[0])).RunAsync(request)).ReadManifest();
Console.WriteLine($"backend={result.Backend.Id}, output={result.Measurements[0].Value} V, assertion={result.Assertions[0].Outcome}");

