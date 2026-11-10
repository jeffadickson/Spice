#:property PublishAot=false
#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;

var sensitivity = args.Length > 0 && args[0] == "--sensitivity";
if (sensitivity) args = args[1..];
if (args.Length is < 2 or > 3 || !File.Exists(args[0]) || !File.Exists(args[1]))
    throw new ArgumentException("Pass an existing self-contained deck, solver executable, and optional backend ID.");

var path = Path.GetFullPath(args[0]);
if (new FileInfo(path).Length > 1024 * 1024)
    throw new InvalidDataException("DC small-signal deck exceeds 1 MiB.");
var syntax = DeckParser.Parse(await File.ReadAllTextAsync(path), collectDeclarations: true);
var backend = args.Length == 3 ? args[2] : "lu";
var client = new SimulationClient(Path.GetFullPath(args[1]));
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(sensitivity ? 300 : 150));
try
{
    if (sensitivity)
    {
        var result = await SensitivitySelection.ExecuteAsync(syntax, client, backend, deadline.Token);
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Success ? 0 : 1;
    }

    var plan = TransferFunctionSelection.Prepare(syntax, backend);
    if (!plan.Success)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { plan.Diagnostic }));
        return 1;
    }

    var success = true;
    foreach (var operation in plan.Operations)
    {
        var result = await TransferFunctionSelection.ExecuteAsync(operation, client, deadline.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { operation.InputSource, operation.Range, Result = result }));
        success &= result.Success;
    }
    return success ? 0 : 1;
}
catch (OperationCanceledException) when (deadline.IsCancellationRequested)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Diagnostic = "selected_dc_deadline" }));
    return 1;
}
