#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Supervise real mutation frameworks, preserving their native reports locally.
// A timeout is partial evidence, never a killed mutant or a completed campaign.
var minutes=60d;var shard="0/1";var rustFile="src/*.rs";
for(var i=0;i<args.Length;i++) switch(args[i]) {
    case "--minutes":minutes=double.Parse(args[++i],CultureInfo.InvariantCulture);break;
    case "--shard":shard=args[++i];break;
    case "--rust-file":rustFile=args[++i];break;
    default:throw new ArgumentException("Unknown framework argument: "+args[i]);
}
if(!double.IsFinite(minutes)||minutes<=0||minutes>1440)throw new ArgumentException("Minutes must be in (0,1440].");
var parts=shard.Split('/');
if(parts.Length!=2||!int.TryParse(parts[0],out var n)||!int.TryParse(parts[1],out var k)||n<0||k<=n||k>1024)throw new ArgumentException("Shard must be N/K with 0 <= N < K <= 1024.");
if(!rustFile.StartsWith("src/",StringComparison.Ordinal)||rustFile.Contains("..")||rustFile.Contains('\\'))throw new ArgumentException("Rust filter must stay under src/.");
var root=Directory.GetCurrentDirectory();
Directory.CreateDirectory("work");
using var exclusive=new FileStream("work/framework-mutation.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var run=Path.Combine(root,"work","framework-mutations",DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(run);
var options=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
var report=new FrameworkReport{RequestedSeconds=minutes*60,Shard=shard,RustFile=rustFile};
var clock=Stopwatch.StartNew();
double Remaining()=>Math.Max(0,minutes*60-clock.Elapsed.TotalSeconds);
async Task<(int Exit,bool Timeout)> Execute(string file,IEnumerable<string> arguments,string log,double seconds,Dictionary<string,string>? environment=null,string? directory=null) {
    var start=new ProcessStartInfo(file){WorkingDirectory=directory??root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    foreach(var arg in arguments)start.ArgumentList.Add(arg);
    start.Environment["CARGO_BUILD_JOBS"]="2";start.Environment["RUST_TEST_THREADS"]="2";
    if(environment is not null)foreach(var pair in environment)start.Environment[pair.Key]=pair.Value;
    using var process=Process.Start(start)??throw new IOException("Could not start "+file);
    // Drain both pipes after truncating retained text, avoiding child deadlocks.
    async Task<string> Read(StreamReader reader){var text=new StringBuilder();var buffer=new char[4096];int count;while((count=await reader.ReadAsync(buffer))>0)if(text.Length<1_048_576)text.Append(buffer,0,Math.Min(count,1_048_576-text.Length));return text.ToString();}
    var stdout=Read(process.StandardOutput);var stderr=Read(process.StandardError);var timeout=false;
    using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(.01,Math.Min(seconds,Remaining()))));
    try{await process.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){timeout=true;process.Kill(true);await process.WaitForExitAsync();}
    await File.WriteAllTextAsync(Path.Combine(run,log),await stdout+"\n"+await stderr);
    return(process.ExitCode,timeout);
}
void Save(){report.ElapsedSeconds=clock.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(run,"summary.json"),JsonSerializer.Serialize(report,options));}
Console.WriteLine("Framework summary: "+Path.Combine(run,"summary.json"));Save();
try {
    var cargo=Environment.GetEnvironmentVariable("SPICE_CARGO_MUTANTS")??Path.Combine(root,".tools","mutation","cargo-mutants"+(OperatingSystem.IsWindows()?".exe":""));
    var version=await Execute(cargo,["mutants","--version"],"cargo-version.log",15);
    if(version.Exit!=0||version.Timeout||!File.ReadAllText(Path.Combine(run,"cargo-version.log")).Contains("27.1.0"))throw new IOException("cargo-mutants version does not match pin.");
    var tools=await Execute("dotnet",["tool","list","--local"],"dotnet-tools.log",15);
    if(tools.Exit!=0||tools.Timeout||!File.ReadAllText(Path.Combine(run,"dotnet-tools.log")).Contains("5.0.0"))throw new IOException("Restore pinned Stryker tool first.");
    // Compile the real engine once; managed mutation tests call this executable,
    // so every mutant exercises the same unmodified native dependency.
    var build=await Execute("cargo",["build","--locked"],"engine-build.log",Math.Min(900,Remaining()/3));
    if(build.Exit!=0||build.Timeout)throw new IOException("Native test dependency build failed or timed out.");
    var target=Environment.GetEnvironmentVariable("CARGO_TARGET_DIR");
    var engine=Path.Combine(target is null?Path.Combine(root,"target"):Path.GetFullPath(target,root),"debug","circuit-simulator"+(OperatingSystem.IsWindows()?".exe":""));
    // Stryker replaces managed binaries during testing. Copy only owned sources,
    // never bin/obj, so concurrent ordinary client builds remain independent.
    var managedRoot=Path.Combine(run,"managed");Directory.CreateDirectory(managedRoot);
    foreach(var path in new[]{"dotnet-tools.json","global.json"}.Concat(Directory.EnumerateFiles("dotnet","*",SearchOption.AllDirectories).Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(part=>part is "bin" or "obj" or "TestResults")))) {
        var destination=Path.Combine(managedRoot,path);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(path,destination);
    }
    var stryker=await Execute("dotnet",["tool","run","dotnet-stryker","--","--config-file","dotnet/Simulator.Client.Tests/stryker-config.json","--test-project","dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj","--output",Path.Combine(run,"stryker")],"stryker.log",Remaining()*.4,new(){["SPICE_TEST_ROOT"]=root,["SPICE_TEST_EXECUTABLE"]=engine},managedRoot);
    report.Stryker.Status=stryker.Timeout?"partial_timeout":stryker.Exit==0?"completed":"failed";
    report.Stryker.ExitCode=stryker.Exit;report.Stryker.LogSha256=Hash(Path.Combine(run,"stryker.log"));
    var managed=Directory.Exists(Path.Combine(run,"stryker"))?Directory.GetFiles(Path.Combine(run,"stryker"),"mutation-report.json",SearchOption.AllDirectories):[];
    if(managed.Length==1){using var json=JsonDocument.Parse(File.ReadAllText(managed[0]));foreach(var file in json.RootElement.GetProperty("files").EnumerateObject())foreach(var mutant in file.Value.GetProperty("mutants").EnumerateArray())report.Stryker.Add(mutant.GetProperty("status").GetString()!);report.Stryker.ReportSha256=Hash(managed[0]);}
    // Limit cargo-mutants' copy root to Rust-owned inputs. Its default copy mode
    // also visits work/ and can collide with our exclusive campaign lock.
    var rustRoot=Path.Combine(run,"rust-source");Directory.CreateDirectory(rustRoot);
    foreach(var path in new[]{"Cargo.toml","Cargo.lock","rust-toolchain.toml"}.Concat(new[]{"src","tests","examples","compat"}.SelectMany(folder=>Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories)))) {
        var destination=Path.Combine(rustRoot,path);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(path,destination);
    }
    var rust=await Execute(cargo,["mutants","--dir",rustRoot,"--file",rustFile,"--shard",shard,"--sharding","round-robin","--no-shuffle","--jobs","1","--jobserver-tasks","2","--timeout","120","--build-timeout","900","--output",Path.Combine(run,"rust"),"--colors","never","--cargo-arg=--locked"],"cargo-mutants.log",Remaining());
    report.CargoMutants.Status=rust.Timeout?"partial_timeout":rust.Exit is 0 or 2 or 3?"completed":"failed";
    report.CargoMutants.ExitCode=rust.Exit;report.CargoMutants.LogSha256=Hash(Path.Combine(run,"cargo-mutants.log"));
    var native=Path.Combine(run,"rust","mutants.out","outcomes.json");
    if(File.Exists(native)){using var json=JsonDocument.Parse(File.ReadAllText(native));foreach(var outcome in json.RootElement.GetProperty("outcomes").EnumerateArray())if(outcome.GetProperty("scenario").ValueKind==JsonValueKind.Object)report.CargoMutants.Add(outcome.GetProperty("summary").GetString()!);report.CargoMutants.ReportSha256=Hash(native);}
    var inventory=Path.Combine(run,"rust","mutants.out","mutants.json");
    if(File.Exists(inventory)){using var json=JsonDocument.Parse(File.ReadAllText(inventory));report.CargoMutants.Candidates=json.RootElement.GetArrayLength();}
    report.Status=report.Stryker.Status=="failed"||report.CargoMutants.Status=="failed"?"failed":report.Stryker.Status=="partial_timeout"||report.CargoMutants.Status=="partial_timeout"?"partial_timeout":"completed";
}catch(Exception error){report.Status="failed";File.WriteAllText(Path.Combine(run,"failure.log"),error.ToString());}
finally{Save();}
if(report.Status=="failed")Environment.ExitCode=1;
static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

/// Small path-free evidence suitable for the remote manifest allowlist.
sealed class FrameworkReport {
    public string Status{get;set;}="running";public double RequestedSeconds{get;set;}public double ElapsedSeconds{get;set;}
    public string Shard{get;set;}="";public string RustFile{get;set;}="";
    public ToolCounts CargoMutants{get;set;}=new(){Version="27.1.0"};public ToolCounts Stryker{get;set;}=new(){Version="5.0.0"};
}
/// Native categories remain separate; unrecognized statuses are never counted as kills.
sealed class ToolCounts {
    public string Version{get;set;}="";public string Status{get;set;}="not_run";public int? ExitCode{get;set;}
    public int Candidates{get;set;}public int Killed{get;set;}public int Survived{get;set;}public int TimedOut{get;set;}public int Invalid{get;set;}public int NotCovered{get;set;}public int Pending{get;set;}public int Other{get;set;}public int Ignored{get;set;}
    public int Unattempted=>Math.Max(0,Candidates-Killed-Survived-TimedOut-Invalid-NotCovered-Pending-Other-Ignored);
    public string? ReportSha256{get;set;}public string? LogSha256{get;set;}
    public void Add(string status){Candidates++;switch(status){case "Killed":case "CaughtMutant":Killed++;break;case "Survived":case "MissedMutant":Survived++;break;case "Timeout":TimedOut++;break;case "CompileError":case "Unviable":Invalid++;break;case "NoCoverage":NotCovered++;break;case "NotRun":case "Pending":Pending++;break;case "Ignored":Ignored++;break;default:Other++;break;}}
}
