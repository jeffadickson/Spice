#:property PublishAot=false
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Model declarations are evidence of demand, not proof that a device instance
// is present or that removing its first diagnostic will make the deck runnable.
if(args is not [var reportPath,var output] || !File.Exists(reportPath))
    throw new ArgumentException("Usage: <corpus report JSON> <bounded output JSON>");
if(!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
    throw new DirectoryNotFoundException("Output parent missing");
using var report=JsonDocument.Parse(File.ReadAllText(reportPath));
var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(report.RootElement.GetProperty("corpus_root").GetString()!));
if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
var pathComparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
var families=new SortedDictionary<string,SortedSet<string>>(StringComparer.Ordinal);
var unreadable=new List<string>();
var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
foreach(var row in report.RootElement.GetProperty("rows").EnumerateArray())
{
    var relative=row.GetProperty("path").GetString()!;
    var path=Path.GetFullPath(relative,root);
    if(!path.StartsWith(root+Path.DirectorySeparatorChar,pathComparison))
        throw new InvalidDataException("Inventory path escapes corpus");
    if(!File.Exists(path)||new FileInfo(path).Length>1_048_576){unreadable.Add(relative);continue;}
    var bytes=File.ReadAllBytes(path);
    if(bytes.Length>1_048_576){unreadable.Add(relative);continue;}
    string text;
    try{text=new UTF8Encoding(false,true).GetString(bytes);}
    catch(DecoderFallbackException){unreadable.Add(relative);continue;}
    var matches=Regex.Matches(string.Join('\n',text.Split('\n').Skip(1)),@"(?im)^\s*\.model\s+\S+\s+([A-Za-z][A-Za-z0-9_]*)\b");
    if(matches.Count==0)continue;
    hashes[relative]=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    foreach(Match match in matches)
    {
        var family=match.Groups[1].Value.ToUpperInvariant();
        if(!families.TryGetValue(family,out var decks))families[family]=decks=new(StringComparer.Ordinal);
        decks.Add(relative);
    }
}
File.WriteAllText(output,JsonSerializer.Serialize(new{
    report_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant(),
    families=families.Select(p=>new{family=p.Key,decks=p.Value.Count,examples=p.Value.Take(5)}),
    unreadable,source_hashes=hashes,
    limitations=new[]{"Literal model declarations in readable inventoried decks only; no include/expression expansion or model execution.","Families may overlap within decks. Declaration counts are not device counts or compatibility passes."}
},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Inventoried {families.Count} model families; {unreadable.Count} unreadable/oversized/missing decks.");
