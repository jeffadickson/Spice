#:project ../dotnet/Simulator.Syntax/Simulator.Syntax.csproj
#:property PublishAot=false
using System.Text.Json;
using Simulator.Syntax;

// Explicit file policy and separate stage checks; this example never simulates.
if(args.Length!=2)throw new ArgumentException("Expected deck file and allowed root directory");
var file=Path.GetFullPath(args[0]);var allowed=Path.GetFullPath(args[1]);
if(!Directory.Exists(allowed)||!File.Exists(file))throw new FileNotFoundException("Existing input/root required");
var resources=ResourceResolver.Resolve(file,new([allowed]));
if(!resources.Success){Console.WriteLine(JsonSerializer.Serialize(resources.Diagnostics));Environment.ExitCode=1;return;}
var syntax=DeckParser.Parse(resources.Text!,collectDeclarations:true);
if(syntax.Declarations is not {} catalog){Console.WriteLine(JsonSerializer.Serialize(syntax.Diagnostics));Environment.ExitCode=1;return;}
var binding=InstanceBinder.Bind(catalog);var globals=ParameterEvaluator.Evaluate(catalog);
var rootCalls=binding.Instances.Where(i=>i.Header.ScopeId==0&&i.Header.Family=="X"&&i.Status==BindingStatus.Bound)
    .Select(call=>new{name=call.Header.Name,parameters=ParameterEvaluator.ForInstance(catalog,call,globals)}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new{syntax=syntax.Status,sources=resources.Sources.Length,source_segments=resources.Segments.Length,
    observed=SemanticSummary.From(catalog),root_calls=rootCalls,simulation="not_executed"},new JsonSerializerOptions{WriteIndented=true}));
if(!binding.Complete||!globals.Success||rootCalls.Any(c=>!c.parameters.Success))Environment.ExitCode=1;
