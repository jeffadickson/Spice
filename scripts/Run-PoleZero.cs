#:property PublishAot=false
#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
using System.Text.Json;
using Simulator.Frontend;
using Simulator.Syntax;

if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass one existing, self-contained pole-zero deck.");

var path = Path.GetFullPath(args[0]);
if (new FileInfo(path).Length > 1024 * 1024)
    throw new InvalidDataException("Pole-zero deck exceeds 1 MiB.");
var result = PoleZeroSelection.Analyze(DeckParser.Parse(
    await File.ReadAllTextAsync(path), collectDeclarations: true));
Console.WriteLine(JsonSerializer.Serialize(result));
return result.Success ? 0 : 1;
