using SForge.Language;

var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "clamp.sforge"));
var valid = SForgeParser.Parse(source);
if (!valid.Success || valid.Document!.Declarations.Count != 2) throw new Exception("valid SForge example did not parse");
if (SForgeSemanticValidator.Validate(valid.Document).Count != 0) throw new Exception("valid SForge example did not validate");
var request = SForgeLowering.Lower(valid.Document, "startup");
if (request.Circuit.Elements.Length != 4 || request.Analysis is not Simulator.Client.Transient) throw new Exception("SForge did not lower to the simulator request contract");
if (args.Length == 1)
{
    var response = await new Simulator.Client.SimulationClient(args[0]).RunAsync(request);
    if (!response.Succeeded) throw new Exception($"lowered SForge request did not execute successfully: {response.Error}");
}
if (SForgeParser.Parse("circuit { }").Success) throw new Exception("malformed source was accepted");
var lexed = SForgeLexer.Lex("node a;\n# ignored\nnode b;");
if (lexed.Diagnostics.Count != 0 || lexed.Tokens[0].Location.Line != 1 || lexed.Tokens[3].Location.Line != 3) throw new Exception("lexer location contract failed");

var random = new Random(0x5F0A6E);
const string alphabet = "abcdefghijklmnopqrstuvwxyz{}(),:;#@+-0123456789. \n";
for (var i = 0; i < 2_000; i++)
{
    var chars = new char[random.Next(0, 256)];
    for (var j = 0; j < chars.Length; j++) chars[j] = alphabet[random.Next(alphabet.Length)];
    _ = SForgeParser.Parse(new string(chars));
}
Console.WriteLine("SForge conformance: 6 checks passed; malformed-input fuzz seed 0x5F0A6E, 2000 cases.");
