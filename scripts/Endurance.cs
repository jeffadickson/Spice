#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// A wall-clock supervisor over an isolated source snapshot. Never mutates the
// caller's source or promotes overnight results to release qualification.
var minutes=300.0; var seed=BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); var skipMutation=false;
for (var i=0;i<args.Length;i++) {
    switch(args[i]) {
        case "--minutes": minutes=double.Parse(args[++i],CultureInfo.InvariantCulture); break;
        case "--seed": seed=ulong.Parse(args[++i],CultureInfo.InvariantCulture); break;
        case "--skip-mutation": skipMutation=true; break;
        default: throw new ArgumentException("Unknown campaign argument: "+args[i]);
    }
}
if (!double.IsFinite(minutes)||minutes<=0||minutes>1440) throw new ArgumentException("Minutes must be in (0,1440].");
var root=Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root,"Cargo.toml"))) throw new ArgumentException("Run from the Spice repository root.");
Directory.CreateDirectory(Path.Combine(root,"work"));
// An OS file lock is released even after a crash; stale PID files cannot allow overlap.
using var campaignLock=new FileStream(Path.Combine(root,"work","endurance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var run=Path.Combine(root,"work","endurance",DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8]);
var archive=Path.Combine(run,"repo"); Directory.CreateDirectory(archive);
var snapshot=Path.GetFullPath(Path.Combine(root,"work","endurance-workspace"));
// Reuse a stable build path, but retain each immutable source archive for replay.
if(Path.GetDirectoryName(snapshot)!=Path.Combine(root,"work")) throw new IOException("Snapshot escaped work directory.");
if(Directory.Exists(snapshot)) {
    if((File.GetAttributes(snapshot)&FileAttributes.ReparsePoint)!=0) throw new IOException("Refusing a linked build workspace.");
    Directory.Delete(snapshot,true);
}
Directory.CreateDirectory(snapshot);
var report=new CampaignReport { StartedUtc=DateTime.UtcNow, BudgetSeconds=minutes*60, InitialSeed=seed.ToString(CultureInfo.InvariantCulture), Snapshot=archive, BuildWorkspace=snapshot };
var clock=Stopwatch.StartNew();
var reportPath=Path.Combine(run,"summary.json");
var options=new JsonSerializerOptions { WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower };
void Save() {
    report.ElapsedSeconds=clock.Elapsed.TotalSeconds;
    var temporary=reportPath+".tmp";
    File.WriteAllText(temporary,JsonSerializer.Serialize(report,options));
    // Windows readers/antivirus can briefly deny atomic replacement. Retain the
    // complete temporary file and retry for at most two seconds; permanent errors
    // still fail rather than silently discard checkpoint evidence.
    var retry=Stopwatch.StartNew();
    while(true) {
        try { File.Move(temporary,reportPath,true); break; }
        catch(Exception error) when((error is IOException or UnauthorizedAccessException)&&retry.Elapsed<TimeSpan.FromSeconds(2)) {
            report.CheckpointRetries++; Thread.Sleep(20);
        }
    }
}
TimeSpan Remaining()=>TimeSpan.FromSeconds(Math.Max(0,report.BudgetSeconds-clock.Elapsed.TotalSeconds));
async Task<ProcessResult> Execute(string file,IEnumerable<string> arguments,string cwd,TimeSpan timeout,Dictionary<string,string>? env=null) {
    if (timeout<=TimeSpan.Zero) return new(-1,"",true);
    var start=new ProcessStartInfo(file) { WorkingDirectory=cwd,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
    foreach(var argument in arguments) start.ArgumentList.Add(argument);
    if(env is not null) foreach(var (key,value) in env) start.Environment[key]=value;
    using var child=Process.Start(start)??throw new IOException("Could not start "+file);
    // Drain both pipes concurrently, bounding retained text to 1 MiB per stream.
    async Task<string> Read(StreamReader reader) {
        var buffer=new char[4096]; var text=new StringBuilder(); int read; long total=0;
        while((read=await reader.ReadAsync(buffer))>0) { total+=read; if(text.Length<1_048_576) text.Append(buffer,0,Math.Min(read,1_048_576-text.Length)); }
        if(total>1_048_576) text.Append("\n[log truncated at 1 MiB]\n");
        return text.ToString();
    }
    var stdout=Read(child.StandardOutput); var stderr=Read(child.StandardError); var timedOut=false;
    using var limit=new CancellationTokenSource(timeout);
    try { await child.WaitForExitAsync(limit.Token); }
    catch(OperationCanceledException) { timedOut=true; child.Kill(true); await child.WaitForExitAsync(); }
    return new(child.ExitCode,await stdout+"\n"+await stderr,timedOut);
}
TimeSpan Cap(int seconds)=>Remaining()<TimeSpan.FromSeconds(seconds)?Remaining():TimeSpan.FromSeconds(seconds);
Save(); Console.WriteLine("Campaign summary: "+reportPath);
try {
    var git=await Execute("git",["rev-parse","HEAD"],root,Cap(15)); report.SourceCommit=git.Log.Trim();
    var dirty=await Execute("git",["status","--porcelain"],root,Cap(15)); report.SourceDirty=dirty.Log.Trim().Length!=0;
    var rust=await Execute("rustc",["--version"],root,Cap(15)); report.Rustc=rust.Log.Trim();
    var rustVerbose=await Execute("rustc",["-vV"],root,Cap(15)); report.RustcVerbose=rustVerbose.Log.Trim();
    var dotnet=await Execute("dotnet",["--version"],root,Cap(15)); report.Dotnet=dotnet.Log.Trim();
    if(git.Exit!=0||dirty.Exit!=0||rust.Exit!=0||dotnet.Exit!=0) throw new IOException("Campaign provenance discovery failed.");
    var files=new[]{"Cargo.toml","Cargo.lock","rust-toolchain.toml","global.json","Makefile","scripts/Mutation.cs","scripts/Endurance.cs","scripts/Run-Endurance.ps1","scripts/HostTools.ps1"}.Concat(new[]{"src","tests","examples","compat"}.SelectMany(folder=>Directory.EnumerateFiles(Path.Combine(root,folder),"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(root,path)))).ToArray();
    foreach(var file in files) {
        var bytes=File.ReadAllBytes(Path.Combine(root,file));
        report.SourceHashes[file.Replace('\\','/')]=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        foreach(var destination in new[]{snapshot,archive}) {
            var target=Path.Combine(destination,file); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target,bytes);
        }
    }
    // Detect edits racing snapshot acquisition rather than claim a consistent revision.
    foreach(var file in files) if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,file)))).ToLowerInvariant()!=report.SourceHashes[file.Replace('\\','/')]) throw new IOException("Source changed while taking snapshot: "+file);
    Save();
    if(!skipMutation) {
        report.Phase="selected_mutations"; Save();
        var result=await Execute("dotnet",["run","--file","scripts/Mutation.cs"],snapshot,Cap(2700),new(){["SPICE_MUTATION_TARGET_DIR"]=Path.Combine(root,"target","endurance-mutation"),["SPICE_MUTATION_TIMEOUT_SECONDS"]="900",["CARGO_BUILD_JOBS"]="2",["RUST_TEST_THREADS"]="2"});
        File.WriteAllText(Path.Combine(run,"mutation.log"),result.Log);
        if(result.TimedOut) { report.MutationStatus="incomplete_timeout"; throw new TimeoutException("Mutation phase did not finish within its allocated budget."); }
        if(result.Exit!=0) { report.MutationStatus="failed"; throw new IOException("Selected mutation campaign failed; inspect mutation.log and snapshot/evidence/mutation.json."); }
        report.MutationStatus="passed";
        File.Copy(Path.Combine(snapshot,"evidence","mutation.json"),Path.Combine(run,"mutation.json"));
    } else report.MutationStatus="skipped_explicitly";
    report.Phase="build_endurance"; Save();
    var environment=new Dictionary<string,string>{["CARGO_TARGET_DIR"]=Path.Combine(root,"target","endurance"),["CARGO_BUILD_JOBS"]="2",["RUST_TEST_THREADS"]="2",["RUST_LOG"]="off"};
    var build=await Execute("cargo",["test","--locked","--test","endurance","--no-run","--message-format=json"],snapshot,Cap(900),environment);
    File.WriteAllText(Path.Combine(run,"build.log"),build.Log);
    if(build.Exit!=0||build.TimedOut) throw new IOException("Endurance build failed or exhausted its budget; inspect build.log.");
    string? executable=null;
    foreach(var line in build.Log.Split('\n')) {
        if(!line.StartsWith('{')) continue;
        using var document=JsonDocument.Parse(line); var value=document.RootElement;
        if(value.TryGetProperty("reason",out var reason)&&reason.GetString()=="compiler-artifact"&&value.GetProperty("target").GetProperty("name").GetString()=="endurance"&&value.TryGetProperty("executable",out var path)&&path.ValueKind==JsonValueKind.String) executable=path.GetString();
    }
    if(executable is null) throw new IOException("Endurance test executable was not discovered.");
    report.Phase="seeded_batches"; Save();
    while(Remaining()>TimeSpan.FromSeconds(1)) {
        var batchSeed=unchecked(seed+report.Cases*0x9e3779b97f4a7c15UL);
        var failure=Path.Combine(run,"failure");
        environment["SPICE_CAMPAIGN_SEED"]=batchSeed.ToString(CultureInfo.InvariantCulture);
        environment["SPICE_CAMPAIGN_CASES"]="1024"; environment["SPICE_CAMPAIGN_FAILURES"]=failure;
        report.PendingSeed=environment["SPICE_CAMPAIGN_SEED"]; Save();
        var remaining=Remaining(); var result=await Execute(executable,["--ignored","--exact","seeded_batch","--nocapture"],snapshot,Cap(60),environment);
        if(result.TimedOut) {
            File.WriteAllText(Path.Combine(run,"last-batch.log"),result.Log);
            if(remaining<=TimeSpan.FromSeconds(60)) { report.Status="budget_exhausted_partial_batch"; break; }
            throw new TimeoutException("A seeded batch exceeded 60 seconds; replay pending_seed.");
        }
        if(result.Exit!=0||!result.Log.Contains($"ENDURANCE cases=1024 seed={batchSeed}")) {
            File.WriteAllText(Path.Combine(run,"failure.log"),result.Log); throw new IOException("Seeded batch failed; replay pending_seed or failure/failure.json.");
        }
        report.Cases+=1024; report.Batches++; report.PendingSeed=null; Save();
    }
    if(report.Cases==0) throw new TimeoutException("No seeded batch completed within the campaign budget.");
    if(report.Status=="running") report.Status="budget_exhausted";
} catch(Exception error) {
    report.Status="failed"; report.Error=error.ToString(); Environment.ExitCode=1;
} finally { report.Phase="finished"; report.FinishedUtc=DateTime.UtcNow; Save(); }
Console.WriteLine(JsonSerializer.Serialize(report,options));

/// Counts include completed batches only. Budget exhaustion is not a pass or coverage claim.
sealed class CampaignReport {
    public string Status { get;set; }="running";
    public string Phase { get;set; }="snapshot";
    public DateTime StartedUtc { get;set; }
    public DateTime? FinishedUtc { get;set; }
    public double BudgetSeconds { get;set; }
    public double ElapsedSeconds { get;set; }
    public string InitialSeed { get;set; }="";
    public string? PendingSeed { get;set; }
    public ulong Cases { get;set; }
    public ulong Batches { get;set; }
    public ulong CheckpointRetries { get;set; }
    public string MutationStatus { get;set; }="not_run";
    public string? SourceCommit { get;set; }
    public bool SourceDirty { get;set; }
    public string? Rustc { get;set; }
    public string? RustcVerbose { get;set; }
    public string? Dotnet { get;set; }
    public string Snapshot { get;set; }="";
    public string BuildWorkspace { get;set; }="";
    public Dictionary<string,string> SourceHashes { get;set; }=new();
    public string? Error { get;set; }
}
record ProcessResult(int Exit,string Log,bool TimedOut);
