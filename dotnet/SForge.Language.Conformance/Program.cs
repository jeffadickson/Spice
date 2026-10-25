using SForge.Language;

var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "clamp.sforge"));
var valid = SForgeParser.Parse(source);
if (!valid.Success || valid.Document!.Declarations.Count != 2) throw new Exception("valid SForge example did not parse");
if (SForgeParser.Parse("circuit { }").Success) throw new Exception("malformed source was accepted");
Console.WriteLine("SForge conformance: 2 checks passed.");
