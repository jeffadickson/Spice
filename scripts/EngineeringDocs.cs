#:property PublishAot=false
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Sphinx is the required documentation engine; C# owns source staging and the
// compiled-XML projection. Unchanged files retain timestamps for incremental builds.
if(args.Length is not (2 or 4 or 6)||args[0]!="--python"||!File.Exists(args[1])||!File.Exists("Cargo.toml")||args.Length>=4&&args[2]!="--output"||args.Length==6&&(args[4]!="--api-xml"||!File.Exists(args[5])))
    throw new ArgumentException("From the checkout: EngineeringDocs.cs -- --python <Sphinx environment python>");
foreach(var path in new[]{"docs/engineering","dotnet/Simulator.Client","evidence"})
    if(!Directory.Exists(path))throw new DirectoryNotFoundException(path);
var platform=OperatingSystem.IsWindows()?"windows":"linux";
var build=args.Length>=4?Path.GetFullPath(args[3]):Path.GetFullPath(Path.Combine("build","engineering",platform+"-"+System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()));
var source=Path.Combine(build,"source");Ensure(source);Ensure(Path.Combine(source,"requirements"));
await Run("dotnet",["build","dotnet/Simulator.Client/Simulator.Client.csproj","--artifacts-path",Path.Combine(build,"managed")],"managed-build.log");
foreach(var file in Directory.GetFiles("docs/engineering").Where(p=>p.EndsWith(".rst")||p.EndsWith(".py")))
    Write(Path.Combine(source,Path.GetFileName(file)),File.ReadAllText(file));
foreach(var file in Directory.GetFiles("docs","*.sdoc"))
    Write(Path.Combine(source,"requirements",Path.GetFileName(file)),File.ReadAllText(file));
var xml=Path.Combine(build,"managed","bin","Simulator.Client","debug","Simulator.Client.xml");
var apiFiles=args.Length==6?new[]{xml,args[5]}:new[]{xml};
var members=apiFiles.SelectMany(file=>XDocument.Load(file).Descendants("member")).OrderBy(m=>(string?)m.Attribute("name"),StringComparer.Ordinal).ToArray();
var api=new StringBuilder("Managed API reference\n=====================\n\nGenerated from the compiled XML documentation. The experimental wire schema and\nSI/domain contracts remain subject to the canonical requirements.\n\n");
foreach(var member in members) {
    var name=(string?)member.Attribute("name")??throw new InvalidDataException("Unnamed XML member.");
    api.Append(".. rubric:: ``").Append(name).AppendLine("``").AppendLine();
    api.AppendLine(Regex.Replace(member.Element("summary")?.Value??"", @"\s+"," ").Trim()).AppendLine();
}
Write(Path.Combine(source,"api.rst"),api.ToString());
await Run(args[1],["-m","sphinx","-W","--keep-going","-b","html","-j","2",source,Path.Combine(build,"html")],"sphinx.log");
var version=await Run(args[1],["-m","sphinx","--version"],"sphinx-version.log");
if(version.Trim()!="sphinx-build 9.1.0")throw new InvalidDataException("Sphinx version differs from the cross-platform pin.");
var pythonVersion=await Run(args[1],["--version"],"python-version.log");
var hashes=Directory.GetFiles("docs/engineering").Concat(Directory.GetFiles("docs","*.sdoc")).Concat(Directory.GetFiles("dotnet/Simulator.Client","*.cs")).Concat(new[]{xml,"scripts/EngineeringDocs.cs","scripts/Setup-EngineeringDocs.ps1"})
    .ToDictionary(p=>p.Replace('\\','/'),Hash);
File.WriteAllText(args.Length>=4?Path.Combine(build,"verification.json"):"evidence/engineering-docs.json",JsonSerializer.Serialize(new{status="built_with_warnings_as_errors",tool=version.Trim(),python=pythonVersion.Trim(),
    xml_member_entries=members.Length,api_xml_sha256=apiFiles.ToDictionary(file=>file,Hash),source_hashes=hashes,local_html=Path.GetRelativePath(Directory.GetCurrentDirectory(),Path.Combine(build,"html","index.html")),
    limitations=new[]{"Compiled XML text projection, not a complete semantic API-compatibility proof.","StrictDoc parsing is a separate required gate.","Python is required by Sphinx; the C# helper executed managed, not NativeAOT."}},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Engineering HTML: "+Path.Combine(build,"html","index.html"));

static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
static void Ensure(string path){if(Directory.Exists(path))return;var parent=Path.GetDirectoryName(path)??throw new IOException("Missing parent.");if(!Directory.Exists(parent))Ensure(parent);Directory.CreateDirectory(path);}
static void Write(string path,string text){if(!File.Exists(path)||File.ReadAllText(path)!=text)File.WriteAllText(path,text,new UTF8Encoding(false));}
async Task<string> Run(string file,string[] arguments,string log){
    var start=new ProcessStartInfo(file){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    foreach(var argument in arguments)start.ArgumentList.Add(argument);
    using var process=Process.Start(start)??throw new IOException(file);
    var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
    using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw new TimeoutException(file);}
    var text=await output+await errors;File.WriteAllText(Path.Combine(build,log),text);
    if(process.ExitCode!=0)throw new IOException(file+" failed; inspect "+Path.Combine(build,log));
    return text;
}
