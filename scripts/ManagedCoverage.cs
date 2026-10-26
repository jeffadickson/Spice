#:property PublishAot=false
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

// Reproducible Coverlet/VSTest collection. Vendor, generated and synthetic
// fixture code is excluded from the owned client metric; raw reports stay local.
if(args.Length!=0 || !File.Exists("Cargo.toml")) throw new ArgumentException("Run without arguments from the Spice checkout.");
foreach(var required in new[]{"dotnet/Simulator.Client","dotnet/Simulator.Client.Tests","dotnet/Simulator.Conformance","dotnet/Simulator.TransportFixture","evidence"})
    if(!Directory.Exists(required)) throw new DirectoryNotFoundException(required);
var run=Path.GetFullPath(Path.Combine("build","managed-coverage",Guid.NewGuid().ToString("N")));
Ensure(run);
var testFiles=new[]{"dotnet/Simulator.Client.Tests","dotnet/Simulator.Conformance","dotnet/Simulator.TransportFixture"}
    .SelectMany(directory=>Directory.GetFiles(directory,"*.cs"))
    .Concat(new[]{"dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj","dotnet/coverlet.runsettings","scripts/ManagedCoverage.cs"});
var testedHashes=Directory.GetFiles("dotnet/Simulator.Client","*.cs").Concat(testFiles).Distinct()
    .ToDictionary(p=>p.Replace('\\','/'),Hash);
await Run("dotnet",["test","dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj","--collect","XPlat Code Coverage",
    "--settings","dotnet/coverlet.runsettings","--results-directory",run], "tests.log");
var reports=Directory.GetFiles(run,"coverage.cobertura.xml",SearchOption.AllDirectories);
if(reports.Length!=1) throw new InvalidDataException("Expected one completed collector report.");
await Run("dotnet",["tool","run","reportgenerator","--","-reports:"+reports[0],"-targetdir:"+Path.Combine(run,"report"),
    "-reporttypes:Html;JsonSummary;Cobertura","-assemblyfilters:+Simulator.Client","-filefilters:-**/obj/**"],"reportgenerator.log");
var coverage=XDocument.Load(reports[0]).Root!;
foreach(var (file,hash) in testedHashes)
    if(Hash(file)!=hash)throw new IOException("Source changed during coverage: "+file);
var hashes=testedHashes.Where(pair=>pair.Key.StartsWith("dotnet/Simulator.Client/",StringComparison.Ordinal)).ToDictionary();
var metadata=new {
    collector="coverlet.collector 10.1.0",report_generator="5.5.11",source_commit=await Capture("git",["rev-parse","HEAD"]),
    source_dirty=(await Capture("git",["status","--porcelain"])).Length!=0,source_hashes=hashes,
    test_and_helper_source_hashes=testedHashes.Where(pair=>!hashes.ContainsKey(pair.Key)).ToDictionary(),
    sdk=await Capture("dotnet",["--version"]),
    lines=(int)coverage.Attribute("lines-valid")!,covered_lines=(int)coverage.Attribute("lines-covered")!,
    branches=(int)coverage.Attribute("branches-valid")!,covered_branches=(int)coverage.Attribute("branches-covered")!,
    report_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reports[0]))).ToLowerInvariant(),
    local_report=Path.GetRelativePath(Directory.GetCurrentDirectory(),reports[0]).Replace('\\','/'),
    limitations=new[]{"Owned Simulator.Client assembly, excluding obj-generated sources; fixture/test/native/vendor code excluded.",
        "Coverlet branch mapping differs from historical dotnet-coverage; percentages are not directly comparable.",
        "File-based coverage orchestration was managed execution, not NativeAOT."}
};
File.WriteAllText("evidence/managed-coverlet.json",JsonSerializer.Serialize(metadata,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(metadata));

static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
void Ensure(string directory) {
    if(Directory.Exists(directory)) return;
    var parent=Path.GetDirectoryName(directory)??throw new IOException("Missing parent.");
    if(!Directory.Exists(parent)) Ensure(parent);
    Directory.CreateDirectory(directory);
}
async Task Run(string file,string[] arguments,string log) {
    try { File.WriteAllText(Path.Combine(run,log),await Capture(file,arguments)); }
    catch(Exception error) { File.WriteAllText(Path.Combine(run,log),error.ToString()); throw; }
}
async Task<string> Capture(string file,string[] arguments) {
    var start=new ProcessStartInfo(file){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    foreach(var argument in arguments)start.ArgumentList.Add(argument);
    using var process=Process.Start(start)??throw new IOException("Could not start "+file);
    var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
    using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try{await process.WaitForExitAsync(timeout.Token);}
    catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw new TimeoutException(file);}
    var text=(await output+await errors).Trim();
    if(process.ExitCode!=0)throw new IOException(file+" failed: "+text);
    return text;
}
