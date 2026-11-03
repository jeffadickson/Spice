#:property PublishAot=false
#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;

// Match native ngspice started with -D no_auto_gnd. Here GND is a
// deliberately driven 2 V node, so the independent 1 V source makes out 3 V.
if(args.Length!=1||!File.Exists(args[0])||!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(args[0]))))
    throw new ArgumentException("Expected an existing circuit-simulator executable.");
var syntax=DeckParser.Parse("Explicit ground policy\nVref GND 0 2\nV1 out GND 1\nR1 out 0 1k\n.end\n",collectDeclarations:true);
var prepared=OperatingPointSelection.Select(syntax,new(){GroundAliases=GroundAliasPolicy.Disabled}).Prepared;
if(!prepared.Success)throw new InvalidDataException(JsonSerializer.Serialize(prepared.Diagnostics));
using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
var response=await new SimulationClient(args[0]).RunAsync(prepared.Request!,deadline.Token);
if(!response.Succeeded)throw new InvalidOperationException(response.Payload.GetRawText());
var output=response.FinalValue("v("+prepared.RootNodes["out"]+")");
var reference=response.FinalValue("v("+prepared.RootNodes["GND"]+")");
if(Math.Abs(output-3)>1e-10||Math.Abs(reference-2)>1e-10)throw new InvalidDataException("Independent voltage constraints failed.");
Console.WriteLine(JsonSerializer.Serialize(new{ground_policy=prepared.GroundAliases.ToString(),output_volts=output,reference_volts=reference}));
