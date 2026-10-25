#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
using Simulator.Client;
// Execute from the repository root: dotnet run --file examples/Run.cs -- <exe> <request.json>
if (args.Length != 2) throw new ArgumentException("Expected simulator executable and request JSON path.");
var result = await new SimulationClient(Path.GetFullPath(args[0])).RunJsonAsync(await File.ReadAllTextAsync(args[1]));
Console.WriteLine($"status={result.Status}, complete={result.Complete}");
if (result.Succeeded) Console.WriteLine($"v(out)={result.FinalValue("v(out)"):G17} V");

