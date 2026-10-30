#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Private process fixtures reproduce inherited-pipe lifetime independently of
// mutation tools. The holder is always terminated by its owning self-test.
if(args is ["--pipe-holder"]) {await Task.Delay(TimeSpan.FromSeconds(30));return;}
if(args is ["--pipe-parent"]) {
    using var holder=Process.Start(SelfStart("--pipe-holder"))??throw new IOException("Probe holder did not start.");
    Console.WriteLine("owned-pipe-pid:"+holder.Id);return;
}
var pipeSelfTest=args is ["--pipe-self-test"];
if(pipeSelfTest)args=["--tool","rust","--minutes","0.2","--workers","1"];

if(args is ["--snapshot-self-test"]) {
    var temporary=Path.Combine(Path.GetTempPath(),"spice-snapshot-"+Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporary);
    try {
        var source=Path.Combine(temporary,"source");var frozen=Path.Combine(temporary,"frozen");
        File.WriteAllText(source,"original");var hash=FreezeFile(source,frozen);
        File.WriteAllText(source,"changed original");
        VerifyFrozen(frozen,hash);
        File.WriteAllText(frozen,"changed snapshot");
        try {VerifyFrozen(frozen,hash);throw new InvalidOperationException("Snapshot tampering accepted.");}
        catch(IOException) { }
        Console.WriteLine("Snapshot isolation and tamper rejection passed.");
    } finally {Directory.Delete(temporary,true);}
    return;
}

// Supervise real mutation frameworks, preserving their native reports locally.
// A timeout is partial evidence, never a killed mutant or a completed campaign.
if(args is ["--selection-self-test"]) {
    var rest="src/waveform.rs:260:13: delete match arm Self::DampedSine{delay_s, ..} in Waveform::breakpoints";
    if(!ValidExactName(rest))throw new InvalidOperationException("Valid Rust rest pattern rejected.");
    foreach(var invalid in new[]{"src/../escape.rs:1:1: replace x", "src/a/../../escape.rs:1:1: replace x", "src/a\\escape.rs:1:1: replace x", "outside.rs:1:1: replace x", "src/a.rs:1:1: bad\nname",new string('x',513)})
        if(ValidExactName(invalid))throw new InvalidOperationException("Unsafe selection accepted.");
    Console.WriteLine("Exact selection path/rest-pattern contracts passed.");
    return;
}
var minutes=60d;var shard="0/1";var rustFile="src/*.rs";var rustTest="all";var selectedTool="all";var rustFilter="";var workers=2;
string? rustNamesFile=null;string[]? selectedNames=null;string? selectionHash=null;
for(var i=0;i<args.Length;i++) switch(args[i]) {
    case "--minutes":minutes=double.Parse(args[++i],CultureInfo.InvariantCulture);break;
    case "--shard":shard=args[++i];break;
    case "--rust-file":rustFile=args[++i];break;
    case "--rust-test":rustTest=args[++i];break;
    case "--tool":selectedTool=args[++i];break;
    case "--rust-filter":rustFilter=args[++i];break;
    case "--rust-names-file":rustNamesFile=args[++i];break;
    case "--workers":workers=int.Parse(args[++i],CultureInfo.InvariantCulture);break;
    default:throw new ArgumentException("Unknown framework argument: "+args[i]);
}
if(!double.IsFinite(minutes)||minutes<=0||minutes>1440)throw new ArgumentException("Minutes must be in (0,1440].");
if(selectedTool is not ("all" or "rust" or "dotnet"))throw new ArgumentException("Tool must be all, rust or dotnet.");
if(workers<1||workers>16)throw new ArgumentException("Workers must be in 1..16.");
if(rustFilter.Length>512||rustFilter.Contains('\0'))throw new ArgumentException("Rust filter exceeds its bound.");
if(rustNamesFile is not null) {
    if(rustFilter.Length!=0||shard!="0/1")throw new ArgumentException("Exact names cannot be combined with a regex or shard.");
    var path=Path.GetFullPath(rustNamesFile);
    if(!File.Exists(path)||new FileInfo(path).Length>32768)throw new ArgumentException("Missing/oversized exact-name selection.");
    var bytes=File.ReadAllBytes(path);selectionHash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    using var names=JsonDocument.Parse(bytes);
    if(names.RootElement.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Expected an array of exact native names.");
    selectedNames=names.RootElement.EnumerateArray().Select(item=>item.GetString()??throw new ArgumentException("Null mutant name.")).ToArray();
    if(selectedNames.Length is <1 or >64||selectedNames.Distinct(StringComparer.Ordinal).Count()!=selectedNames.Length||
        selectedNames.Any(name=>!ValidExactName(name)))
        throw new ArgumentException("Invalid exact-name selection.");
    rustFilter="^(?:"+string.Join("|",selectedNames.Select(System.Text.RegularExpressions.Regex.Escape))+")$";
    if(rustFilter.Length>8192)throw new ArgumentException("Exact-name expression exceeds its bound.");
}
// Exact selections use ordinal membership; compiling their large union in the
// local symbolic regex engine can exceed its automaton bound unnecessarily.
var selectedSet=selectedNames?.ToHashSet(StringComparer.Ordinal);
var requestedFilter=rustFilter.Length==0||selectedSet is not null?null:new System.Text.RegularExpressions.Regex(rustFilter,
    System.Text.RegularExpressions.RegexOptions.CultureInvariant|System.Text.RegularExpressions.RegexOptions.NonBacktracking,
    TimeSpan.FromMilliseconds(100));
var parts=shard.Split('/');
if(parts.Length!=2||!int.TryParse(parts[0],out var n)||!int.TryParse(parts[1],out var k)||n<0||k<=n||k>1024)throw new ArgumentException("Shard must be N/K with 0 <= N < K <= 1024.");
if(!rustFile.StartsWith("src/",StringComparison.Ordinal)||rustFile.Contains("..")||rustFile.Contains('\\'))throw new ArgumentException("Rust filter must stay under src/.");
var testTargets=rustTest.Split(',');
if(testTargets.Length>8||testTargets.Distinct(StringComparer.Ordinal).Count()!=testTargets.Length||
    (testTargets.Contains("all")&&testTargets.Length!=1)||
    testTargets.Any(test=>test is not ("all" or "lib")&&
        (!System.Text.RegularExpressions.Regex.IsMatch(test,@"\A[A-Za-z0-9_-]{1,64}\z")||!File.Exists(Path.Combine("tests",test+".rs")))))
    throw new ArgumentException("Rust tests must be all, lib, or up to eight distinct named integration tests.");
var root=Directory.GetCurrentDirectory();
if(!Directory.Exists("work"))Directory.CreateDirectory("work");
// Rust and managed snapshots/build outputs are isolated. All-tool campaigns
// acquire both locks in the same order; independent one-worker runs may overlap.
using var rustLock=selectedTool!="dotnet"?new FileStream("work/framework-rust.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None):null;
using var managedLock=selectedTool!="rust"?new FileStream("work/framework-dotnet.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None):null;
var run=Path.Combine(root,"work","framework-mutations",DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(run);
var options=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
var report=new FrameworkReport{RequestedSeconds=minutes*60,Shard=shard,RustFile=rustFile,RustTest=rustTest,Tool=selectedTool,RustFilter=rustFilter,Workers=workers};
report.RustSelectedNames=selectedNames;report.RustSelectionSha256=selectionHash;
var clock=Stopwatch.StartNew();
double Remaining()=>Math.Max(0,minutes*60-clock.Elapsed.TotalSeconds);
async Task<(int Exit,bool Timeout)> Execute(string file,IEnumerable<string> arguments,string log,double seconds,Dictionary<string,string>? environment=null,string? directory=null) {
    var start=new ProcessStartInfo(file){WorkingDirectory=directory??root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    foreach(var arg in arguments)start.ArgumentList.Add(arg);
    start.Environment["CARGO_BUILD_JOBS"]=workers.ToString(CultureInfo.InvariantCulture);start.Environment["RUST_TEST_THREADS"]=workers.ToString(CultureInfo.InvariantCulture);
    if(environment is not null)foreach(var pair in environment)start.Environment[pair.Key]=pair.Value;
    using var process=Process.Start(start)??throw new IOException("Could not start "+file);
    // The same budget covers execution AND pipe draining. A descendant can
    // retain inherited handles after the immediate parent has already exited.
    var timeout=false;
    using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(.01,Math.Min(seconds,Remaining()))));
    async Task<string> Read(StreamReader reader) {
        var text=new StringBuilder();var buffer=new char[4096];int count;
        try{while((count=await reader.ReadAsync(buffer.AsMemory(),limit.Token))>0)
            if(text.Length<1_048_576)text.Append(buffer,0,Math.Min(count,1_048_576-text.Length));}
        catch(OperationCanceledException){timeout=true;}
        catch(Exception error) when(limit.IsCancellationRequested&&error is IOException or ObjectDisposedException){timeout=true;}
        return text.ToString();
    }
    var stdout=Read(process.StandardOutput);var stderr=Read(process.StandardError);
    try{await process.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){timeout=true;if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));}
    report.OwnedWorkersReaped+=ReapOwnedWorkers(run);
    try{await Task.WhenAll(stdout,stderr).WaitAsync(limit.Token);}
    catch(OperationCanceledException){timeout=true;process.StandardOutput.Dispose();process.StandardError.Dispose();
        try{await Task.WhenAll(stdout,stderr).WaitAsync(TimeSpan.FromSeconds(2));}catch(TimeoutException){}}
    await File.WriteAllTextAsync(Path.Combine(run,log),
        (stdout.IsCompletedSuccessfully?stdout.Result:"[stdout drain incomplete]")+"\n"+
        (stderr.IsCompletedSuccessfully?stderr.Result:"[stderr drain incomplete]"));
    return(process.ExitCode,timeout);
}
void Save(){report.ElapsedSeconds=clock.Elapsed.TotalSeconds;File.WriteAllText(Path.Combine(run,"summary.json"),JsonSerializer.Serialize(report,options));}
Console.WriteLine("Framework summary: "+Path.Combine(run,"summary.json"));Save();
if(pipeSelfTest) {
    var probe=SelfStart("--pipe-parent");
    try {
        var result=await Execute(probe.FileName,probe.ArgumentList,"pipe-probe.log",2);
        if(!result.Timeout||result.Exit!=0)throw new IOException("Inherited pipe did not trigger a bounded drain timeout.");
    } finally {
        var text=File.ReadAllText(Path.Combine(run,"pipe-probe.log"));
        var match=System.Text.RegularExpressions.Regex.Match(text,@"owned-pipe-pid:(\d+)");
        if(!match.Success)throw new IOException("Probe did not publish its owned PID.");
        try{using var holder=Process.GetProcessById(int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture));
            _=holder.Handle;
            if(holder.MainModule?.FileName!=Environment.ProcessPath)throw new IOException("Probe process identity changed.");
            holder.Kill(true);holder.WaitForExit(2000);}
        catch(ArgumentException){ }
    }
    // Copy only our compiled fixture into this run, proving workspace-scoped
    // cleanup without selecting processes from other projects or campaigns.
    var worker=Path.Combine(run,"worker");Directory.CreateDirectory(worker);
    var stem=Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
    foreach(var source in Directory.EnumerateFiles(AppContext.BaseDirectory,stem+"*"))File.Copy(source,Path.Combine(worker,Path.GetFileName(source)));
    var copied=Path.Combine(worker,Path.GetFileName(probe.FileName));
    var clean=await Execute(copied,["--pipe-parent"],"owned-worker.log",5);
    if(clean.Timeout||clean.Exit!=0||report.OwnedWorkersReaped!=1)throw new IOException("Owned worker cleanup failed.");
    report.Status="completed";Save();Console.WriteLine("Inherited pipe deadline and owned worker cleanup passed.");return;
}
try {
    var cargo=Environment.GetEnvironmentVariable("SPICE_CARGO_MUTANTS")??Path.Combine(root,".tools","mutation","cargo-mutants"+(OperatingSystem.IsWindows()?".exe":""));
    if(selectedTool!="dotnet") {
    var version=await Execute(cargo,["mutants","--version"],"cargo-version.log",15);
    if(version.Exit!=0||version.Timeout||!File.ReadAllText(Path.Combine(run,"cargo-version.log")).Contains("27.1.0"))throw new IOException("cargo-mutants version does not match pin.");
    }
    if(selectedTool!="rust") {
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
    // Freeze the real native dependency and fixture inputs too: otherwise a
    // concurrent rebuild/edit in the original checkout changes mutant semantics.
    var inputsRoot=Path.Combine(run,"managed-inputs");Directory.CreateDirectory(inputsRoot);
    var nativeName="circuit-simulator"+(OperatingSystem.IsWindows()?".exe":"");
    var frozenEngine=Path.Combine(inputsRoot,nativeName);
    report.ManagedInputHashes[nativeName]=FreezeFile(engine,frozenEngine);
    foreach(var relative in new[]{"examples/divider.json","examples/rc.json","examples/adaptive-rc.json"}) {
        var destination=Path.Combine(inputsRoot,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        report.ManagedInputHashes[relative]=FreezeFile(Path.Combine(root,relative),destination);
    }
    var stryker=await Execute("dotnet",["tool","run","dotnet-stryker","--","--config-file","dotnet/Simulator.Client.Tests/stryker-config.json","--test-project","dotnet/Simulator.Client.Tests/Simulator.Client.Tests.csproj","--concurrency",workers.ToString(CultureInfo.InvariantCulture),"--output",Path.Combine(run,"stryker")],"stryker.log",Remaining()*(selectedTool=="dotnet"?1:.4),new(){["SPICE_TEST_ROOT"]=inputsRoot,["SPICE_TEST_EXECUTABLE"]=frozenEngine},managedRoot);
    foreach(var pair in report.ManagedInputHashes)VerifyFrozen(Path.Combine(inputsRoot,pair.Key),pair.Value);
    report.Stryker.Status=stryker.Timeout?"partial_timeout":stryker.Exit==0?"completed":"failed";
    report.Stryker.ExitCode=stryker.Exit;report.Stryker.LogSha256=Hash(Path.Combine(run,"stryker.log"));
    var managed=Directory.Exists(Path.Combine(run,"stryker"))?Directory.GetFiles(Path.Combine(run,"stryker"),"mutation-report.json",SearchOption.AllDirectories):[];
    if(managed.Length==1){using var json=JsonDocument.Parse(File.ReadAllText(managed[0]));foreach(var file in json.RootElement.GetProperty("files").EnumerateObject())foreach(var mutant in file.Value.GetProperty("mutants").EnumerateArray())report.Stryker.Add(mutant.GetProperty("status").GetString()!);report.Stryker.ReportSha256=Hash(managed[0]);}
    }else report.Stryker.Status="skipped_explicitly";
    if(selectedTool!="dotnet") {
    // Limit cargo-mutants' copy root to Rust-owned inputs. Its default copy mode
    // also visits work/ and can collide with our exclusive campaign lock.
    var rustRoot=Path.Combine(run,"rust-source");Directory.CreateDirectory(rustRoot);
    foreach(var path in new[]{"Cargo.toml","Cargo.lock","rust-toolchain.toml"}.Concat(new[]{"src","tests","examples","compat"}.SelectMany(folder=>Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories)))) {
        var destination=Path.Combine(rustRoot,path);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(path,destination);
    }
    var rustArguments=new List<string>{"mutants","--dir",rustRoot,"--file",rustFile,"--shard",shard,"--sharding","round-robin","--no-shuffle","--jobs","1","--jobserver-tasks",workers.ToString(CultureInfo.InvariantCulture),"--timeout","120","--build-timeout","900","--output",Path.Combine(run,"rust"),"--colors","never","--cargo-arg=--locked"};
    if(rustFilter.Length!=0){rustArguments.Add("--re");rustArguments.Add(rustFilter);}
    // Test selection must constrain BOTH the compile baseline and test runs;
    // cargo-test-arg alone still compiles every integration binary.
    if(rustTest!="all")foreach(var test in testTargets) {
        if(test=="lib")rustArguments.Add("--cargo-arg=--lib");
        else {rustArguments.Add("--cargo-arg=--test");rustArguments.Add("--cargo-arg="+test);}
    }
    if(requestedFilter is not null||selectedSet is not null) {
        // The native field-deletion genre has been observed outside --re.
        // Check its actual name inventory before any mutation, failing closed.
        var listingArguments=new List<string>{"mutants","--list","--dir",rustRoot,"--file",rustFile,"--re",rustFilter,
            "--shard",shard,"--sharding","round-robin","--colors","never"};
        var listing=await Execute(cargo,listingArguments,"cargo-inventory.log",Math.Min(60,Remaining()));
        var listed=File.ReadAllText(Path.Combine(run,"cargo-inventory.log"));
        var names=listed.Split('\n').Select(line=>line.Trim()).Where(line=>line.StartsWith("src/",StringComparison.Ordinal)).ToArray();
        report.CargoMutants.Candidates=names.Length;
        if(listing.Timeout||listing.Exit!=0||listed.Contains("[log truncated",StringComparison.Ordinal)||names.Any(name=>selectedSet is not null?!selectedSet.Contains(name):!requestedFilter!.IsMatch(name))) {
            report.CargoMutants.Status="failed";
            throw new IOException("Native mutation inventory failed or escaped the requested filter; no mutants executed.");
        }
        if(selectedNames is not null&&!new HashSet<string>(selectedNames,StringComparer.Ordinal).SetEquals(names))
            throw new IOException("Exact native selection is missing or gained names; no mutants executed.");
    }
    var cache=Path.GetFullPath(Path.Combine(root,"target","framework-rust"));
    var targetRoot=Path.GetFullPath(Path.Combine(root,"target"));
    if(!cache.StartsWith(targetRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
        throw new IOException("Mutation cache escaped the target directory.");
    foreach(var path in new[]{targetRoot,cache})
        if(Directory.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Refusing to clean a linked mutation cache.");
    // Restoring source timestamps can leave a mutant binary marked Fresh in a
    // reused Cargo target. Clean only this owned package before each baseline,
    // retaining fingerprinted vendor dependencies and the isolated target path.
    var clean=await Execute("cargo",["clean","--package","circuit-simulator"],"cargo-clean-owned.log",
        Math.Min(60,Remaining()),new(){["CARGO_TARGET_DIR"]=cache},rustRoot);
    if(clean.Timeout||clean.Exit!=0)throw new IOException("Owned mutation artifacts could not be refreshed.");
    var rust=await Execute(cargo,rustArguments,"cargo-mutants.log",Remaining(),
        new(){["CARGO_TARGET_DIR"]=cache});
    report.CargoMutants.Status=rust.Timeout?"partial_timeout":rust.Exit is 0 or 2 or 3?"completed":"failed";
    report.CargoMutants.ExitCode=rust.Exit;report.CargoMutants.LogSha256=Hash(Path.Combine(run,"cargo-mutants.log"));
    var native=Path.Combine(run,"rust","mutants.out","outcomes.json");
    if(File.Exists(native)){using var json=JsonDocument.Parse(File.ReadAllText(native));foreach(var outcome in json.RootElement.GetProperty("outcomes").EnumerateArray())if(outcome.GetProperty("scenario").ValueKind==JsonValueKind.Object)report.CargoMutants.Add(outcome.GetProperty("summary").GetString()!);report.CargoMutants.ReportSha256=Hash(native);}
    var inventory=Path.Combine(run,"rust","mutants.out","mutants.json");
    if(File.Exists(inventory)){using var json=JsonDocument.Parse(File.ReadAllText(inventory));report.CargoMutants.Candidates=json.RootElement.GetArrayLength();}
    }else report.CargoMutants.Status="skipped_explicitly";
    report.Status=report.Stryker.Status=="failed"||report.CargoMutants.Status=="failed"?"failed":report.Stryker.Status=="partial_timeout"||report.CargoMutants.Status=="partial_timeout"?"partial_timeout":"completed";
}catch(Exception error){report.Status="failed";File.WriteAllText(Path.Combine(run,"failure.log"),error.ToString());}
finally{Save();}
if(report.Status=="failed")Environment.ExitCode=1;
static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
// Only the filename prefix is a path. The mutation description may legitimately
// contain Rust rest patterns (".."); it is escaped as regex data and separately
// checked against the exact native inventory before any mutation is executed.
static bool ValidExactName(string name) {
    if(name.Length>512||!name.StartsWith("src/",StringComparison.Ordinal)||name.Any(char.IsControl))return false;
    var separator=name.IndexOf(':');
    var path=separator<0?name:name[..separator];
    return !path.Contains("..",StringComparison.Ordinal)&&!path.Contains('\\');
}
static ProcessStartInfo SelfStart(string argument) {
    var path=Environment.ProcessPath??throw new IOException("Current executable unavailable.");
    var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true};
    if(Path.GetFileNameWithoutExtension(path).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add(argument);return start;
}
static int ReapOwnedWorkers(string directory) {
    var prefix=Path.GetFullPath(directory)+Path.DirectorySeparatorChar;var count=0;
    foreach(var process in Process.GetProcesses())using(process) {
        string? executable;
        try{_=process.Handle;executable=process.MainModule?.FileName;}
        catch(Exception error) when(error is System.ComponentModel.Win32Exception or InvalidOperationException){continue;}
        if(executable is null||!executable.StartsWith(prefix,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))continue;
        var name=Path.GetFileNameWithoutExtension(executable);
        if(name is not ("Simulator.TransportFixture" or "circuit-simulator" or "testhost" or "FrameworkMutations"))continue;
        if(process.HasExited)continue;
        process.Kill(true);if(!process.WaitForExit(1000))throw new IOException("Owned mutation worker did not terminate.");count++;
    }
    return count;
}
// Verify both source-copy stability and later snapshot integrity. A mismatch
// invalidates the campaign instead of publishing incomparable mutant counts.
static string FreezeFile(string source,string destination) {
    var before=Hash(source);File.Copy(source,destination);
    if(Hash(source)!=before||Hash(destination)!=before)throw new IOException("Mutation input changed during snapshot.");
    return before;
}
static void VerifyFrozen(string path,string hash) {
    if(Hash(path)!=hash)throw new IOException("Mutation input snapshot changed during execution.");
}

/// Small path-free evidence suitable for the remote manifest allowlist.
sealed class FrameworkReport {
    public string Status{get;set;}="running";public double RequestedSeconds{get;set;}public double ElapsedSeconds{get;set;}
    public string Shard{get;set;}="";public string RustFile{get;set;}="";
    public string RustTest{get;set;}="all";
    public string Tool{get;set;}="all";
    public string RustFilter{get;set;}="";
    public int Workers{get;set;}=2;
    public int OwnedWorkersReaped{get;set;}
    public string[]? RustSelectedNames{get;set;}public string? RustSelectionSha256{get;set;}
    public Dictionary<string,string> ManagedInputHashes{get;set;}=new(StringComparer.Ordinal);
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
