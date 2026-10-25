using SForge.Language;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: SForge.Runner <source.sforge> <simulator.exe> [run-name]");
    return 64;
}
var source = await File.ReadAllTextAsync(args[0]);
var parsed = SForgeParser.Parse(source);
if (!parsed.Success)
{
    foreach (var diagnostic in parsed.Diagnostics) Console.Error.WriteLine($"{diagnostic.Span.Start}: {diagnostic.Message}");
    return 65;
}
var semantic = SForgeSemanticValidator.Validate(parsed.Document!);
if (semantic.Count != 0)
{
    foreach (var diagnostic in semantic) Console.Error.WriteLine($"{diagnostic.Span.Start}: {diagnostic.Message}");
    return 66;
}
var runName = args.Length == 3 ? args[2] : parsed.Document!.Declarations.OfType<Run>().Single().Name;
var request = SForgeLowering.Lower(parsed.Document!, runName, Path.GetFileNameWithoutExtension(args[0]));
var response = await new Simulator.Client.SimulationClient(args[1]).RunAsync(request);
Console.WriteLine(response.Payload.GetRawText());
return response.Succeeded ? 0 : 2;
