#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
#:property PublishAot=false
using System.Text.Json;
using Simulator.Syntax;
using Simulator.Frontend;
using Simulator.Client;

// Explicit trusted input directory: no implicit resource access in syntax parsing.
if(args is not [var input,var engine]||!File.Exists(input)||!File.Exists(engine))
    throw new ArgumentException("Expected existing deck and Rust executable paths");
var directory=Path.GetDirectoryName(Path.GetFullPath(input))!;
var resolved=ResourceResolver.Resolve(input,new([directory]));
if(!resolved.Success)throw new InvalidDataException(JsonSerializer.Serialize(resolved.Diagnostics));
var syntax=DeckParser.Parse(resolved.Text!,collectDeclarations:true);
var prepared=OperatingPointBridge.Lower(syntax);
if(!prepared.Success)throw new InvalidDataException(JsonSerializer.Serialize(prepared.Diagnostics));
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
var response=await new SimulationClient(engine).RunAsync(prepared.Request!,timeout.Token);
if(!response.Succeeded)throw new InvalidDataException(JsonSerializer.Serialize(response.Error));
// Map user-facing root signals; compact wire names are intentionally private.
foreach(var node in prepared.RootNodes)
    Console.WriteLine($"v({node.Key}) = {response.FinalValue("v("+node.Value+")"):R}");
