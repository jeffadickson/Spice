#:property PublishAot=false
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Summarize only collector-mapped copies with the exact current owned-source
// bytes. Synthetic subprocess fixtures are eligible only if their hash matches.
if(args.Length<3)throw new ArgumentException("Usage: <Cobertura XML> <JSON output> <owned source> [...]");
if(!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(args[1]))))throw new DirectoryNotFoundException("Evidence parent missing.");
var document=XDocument.Load(args[0]);var metrics=new List<object>();
foreach(var file in args.Skip(2)) {
    var hash=Hash(file);
    var classes=document.Descendants("class").Where(c=>{
        var path=(string?)c.Attribute("filename");
        return path is not null&&path.Replace('\\','/').EndsWith("/"+file.Replace('\\','/'),StringComparison.OrdinalIgnoreCase)&&File.Exists(path)&&Hash(path)==hash;
    }).ToArray();
    var lines=classes.SelectMany(c=>c.Element("lines")!.Elements("line")).GroupBy(l=>(int)l.Attribute("number")!).ToArray();
    int branches=0,coveredBranches=0;
    foreach(var line in lines) {
        // Duplicate async state-machine mappings are merged per source line;
        // maxima avoid double-counting, but do not establish path coverage.
        var best=line.Select(l=>Regex.Match((string?)l.Attribute("condition-coverage")??"",@"\((\d+)/(\d+)\)"))
            .Where(m=>m.Success).Select(m=>(Hit:int.Parse(m.Groups[1].Value),Total:int.Parse(m.Groups[2].Value)))
            .OrderByDescending(v=>v.Total).ThenByDescending(v=>v.Hit).FirstOrDefault();
        branches+=best.Total;coveredBranches+=best.Hit;
    }
    var covered=lines.Count(g=>g.Any(l=>(long)l.Attribute("hits")!>0));
    metrics.Add(new{file,source_sha256=hash,lines=lines.Length,covered_lines=covered,line_percent=lines.Length==0?(double?)null:100d*covered/lines.Length,
        branches,covered_branches=coveredBranches,branch_percent=branches==0?(double?)null:100d*coveredBranches/branches});
}
File.WriteAllText(args[1],JsonSerializer.Serialize(new{tool="dotnet-coverage 18.12",collector_sha256=Hash(args[0]),metrics,
    limitations=new[]{"Scoped managed tooling execution; synthetic fixture counts are not simulator qualification.","Per-line maxima for duplicate branch mappings; not complete path coverage.","PowerShell, native children, vendor internals and unexecuted platform/failure paths are unmeasured."}},
    new JsonSerializerOptions{WriteIndented=true,NewLine="\n"}));
Console.WriteLine(JsonSerializer.Serialize(metrics));
static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
