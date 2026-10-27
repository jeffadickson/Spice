#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

if(args is ["--budget-self-test"]) { CampaignBudget.CheckContracts(); return; }
if(args is ["--power-probe"]) {
    var request=SystemPowerRequest.Acquire();Console.WriteLine(request.Status);
    request.Dispose();Console.WriteLine("released="+request.Released);
    if(OperatingSystem.IsWindows()&&request.Released!=true)Environment.ExitCode=1;
    return;
}

// A wall-clock supervisor over an isolated source snapshot. Never mutates the
// caller's source or promotes overnight results to release qualification.
var minutes=300.0; var seed=BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)); var skipMutation=false; var selectedRegressions=false;
for (var i=0;i<args.Length;i++) {
    switch(args[i]) {
        case "--minutes": minutes=double.Parse(args[++i],CultureInfo.InvariantCulture); break;
        case "--seed": seed=ulong.Parse(args[++i],CultureInfo.InvariantCulture); break;
        case "--skip-mutation": skipMutation=true; break;
        case "--selected-regressions": selectedRegressions=true; break;
        default: throw new ArgumentException("Unknown campaign argument: "+args[i]);
    }
}
if (!double.IsFinite(minutes)||minutes<=0||minutes>1440) throw new ArgumentException("Minutes must be in (0,1440].");
if(skipMutation&&selectedRegressions)throw new ArgumentException("Selected regressions conflict with skipped mutations.");
var root=Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root,"Cargo.toml"))) throw new ArgumentException("Run from the Spice repository root.");
EnsureDirectory(Path.Combine(root,"work"));
// An OS file lock is released even after a crash; stale PID files cannot allow overlap.
using var campaignLock=new FileStream(Path.Combine(root,"work","endurance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var run=Path.Combine(root,"work","endurance",DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8]);
var archive=Path.Combine(run,"repo"); EnsureDirectory(archive);
var snapshot=Path.GetFullPath(Path.Combine(root,"work","endurance-workspace"));
// Reuse a stable build path, but retain each immutable source archive for replay.
if(Path.GetDirectoryName(snapshot)!=Path.Combine(root,"work")) throw new IOException("Snapshot escaped work directory.");
if(Directory.Exists(snapshot)) {
    if((File.GetAttributes(snapshot)&FileAttributes.ReparsePoint)!=0) throw new IOException("Refusing a linked build workspace.");
    Directory.Delete(snapshot,true);
}
EnsureDirectory(snapshot);
using var powerRequest=SystemPowerRequest.Acquire();
var budget=new CampaignBudget(TimeSpan.FromMinutes(minutes),TimeProvider.System);
var report=new CampaignReport { StartedUtc=budget.StartedUtc.UtcDateTime, DeadlineUtc=budget.DeadlineUtc.UtcDateTime,
    SleepPrevention=powerRequest.Status, BudgetSeconds=minutes*60, InitialSeed=seed.ToString(CultureInfo.InvariantCulture), Snapshot=archive, BuildWorkspace=snapshot };
var reportPath=Path.Combine(run,"summary.json");
var options=new JsonSerializerOptions { WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower };
void Save() {
    report.ElapsedSeconds=budget.Elapsed.TotalSeconds;
    report.MonotonicElapsedSeconds=budget.MonotonicElapsed.TotalSeconds;
    report.UtcElapsedSeconds=budget.UtcElapsed.TotalSeconds;
    report.BudgetOverrunSeconds=budget.Overrun.TotalSeconds;
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
TimeSpan Remaining()=>budget.Remaining;
async Task<ProcessResult> Execute(string file,IEnumerable<string> arguments,string cwd,TimeSpan timeout,Dictionary<string,string>? env=null) {
    if (timeout<=TimeSpan.Zero) return new(-1,"",true);
    var start=new ProcessStartInfo(file) { WorkingDirectory=cwd,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
    foreach(var argument in arguments) start.ArgumentList.Add(argument);
    if(env is not null) foreach(var (key,value) in env) start.Environment[key]=value;
    using var child=Process.Start(start)??throw new IOException("Could not start "+file);
    // Drain both pipes concurrently, bounding retained text to 1 MiB per stream.
    using var readers=new CancellationTokenSource();
    async Task<string> Read(StreamReader reader) {
        var buffer=new char[4096]; var text=new StringBuilder(); int read; long total=0;
        try { while((read=await reader.ReadAsync(buffer.AsMemory(),readers.Token))>0) { total+=read; if(text.Length<1_048_576) text.Append(buffer,0,Math.Min(read,1_048_576-text.Length)); } }
        catch(OperationCanceledException) when(readers.IsCancellationRequested) { text.Append("\n[pipe deadline]\n"); }
        if(total>1_048_576) text.Append("\n[log truncated at 1 MiB]\n");
        return text.ToString();
    }
    var stdout=Read(child.StandardOutput);var stderr=Read(child.StandardError);
    var completion=Task.WhenAll(stdout,stderr,child.WaitForExitAsync());
    var childBudget=new CampaignBudget(timeout,TimeProvider.System);
    // Recheck absolute UTC as well as monotonic elapsed time after every wake.
    // Timer cancellation alone cannot execute while the OS has suspended us.
    while(!completion.IsCompleted&&Remaining()>TimeSpan.Zero&&childBudget.Remaining>TimeSpan.Zero) {
        var delay=Remaining()<childBudget.Remaining?Remaining():childBudget.Remaining;
        await Task.WhenAny(completion,Task.Delay(delay<TimeSpan.FromSeconds(1)?delay:TimeSpan.FromSeconds(1)));
    }
    var timedOut=!completion.IsCompleted;
    if(timedOut) {readers.Cancel();if(!child.HasExited)child.Kill(true);}
    await completion;
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
    var files=new[]{"Cargo.toml","Cargo.lock","rust-toolchain.toml","global.json","dotnet-tools.json","Makefile","scripts/Mutation.cs","scripts/FrameworkMutations.cs","scripts/Endurance.cs","scripts/Run-Endurance.ps1","scripts/HostTools.ps1"}.Concat(new[]{"src","tests","examples","compat","dotnet"}.SelectMany(folder=>Directory.EnumerateFiles(Path.Combine(root,folder),"*",SearchOption.AllDirectories).Where(path=>!Path.GetRelativePath(root,path).Split(Path.DirectorySeparatorChar).Any(part=>part is "bin" or "obj" or "TestResults")).Select(path=>Path.GetRelativePath(root,path)))).ToArray();
    foreach(var file in files) {
        var bytes=File.ReadAllBytes(Path.Combine(root,file));
        report.SourceHashes[file.Replace('\\','/')]=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        foreach(var destination in new[]{snapshot,archive}) {
            var target=Path.Combine(destination,file); EnsureDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target,bytes);
        }
    }
    // Detect edits racing snapshot acquisition rather than claim a consistent revision.
    foreach(var file in files) if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,file)))).ToLowerInvariant()!=report.SourceHashes[file.Replace('\\','/')]) throw new IOException("Source changed while taking snapshot: "+file);
    Save();
    if(!skipMutation) {
        report.MutationStatus="skipped_explicitly";
        // The maintained mutation frameworks are the primary campaign engine.
        // Reserve half the remaining budget for them, leaving the rest for
        // library-generated numerical/parser properties. Rotate deterministic
        // round-robin shards by the recorded seed rather than retest one prefix.
        report.Phase="framework_mutations";report.FrameworkShard=$"{seed%16}/16";Save();
        var tool=Environment.GetEnvironmentVariable("SPICE_CARGO_MUTANTS")??Path.Combine(root,".tools","mutation","cargo-mutants"+(OperatingSystem.IsWindows()?".exe":""));
        var framework=await Execute("dotnet",["run","--file","scripts/FrameworkMutations.cs","--","--minutes",(Remaining().TotalMinutes*.5).ToString(CultureInfo.InvariantCulture),"--shard",report.FrameworkShard],snapshot,Remaining()*.55,new(){["SPICE_CARGO_MUTANTS"]=tool,["CARGO_TARGET_DIR"]=Path.Combine(root,"target","endurance")});
        File.WriteAllText(Path.Combine(run,"framework.log"),framework.Log);
        var marker=System.Text.RegularExpressions.Regex.Match(framework.Log,@"(?m)^Framework summary: (.+)\r?$");
        if(!marker.Success)throw new IOException("Framework mutation report was not emitted.");
        var frameworkPath=marker.Groups[1].Value.Trim();
        if(new FileInfo(frameworkPath).Length>65536)throw new IOException("Framework summary exceeds bound.");
        var frameworkBytes=File.ReadAllBytes(frameworkPath);File.WriteAllBytes(Path.Combine(run,"framework-mutations.json"),frameworkBytes);
        report.FrameworkSummarySha256=Convert.ToHexString(SHA256.HashData(frameworkBytes)).ToLowerInvariant();
        using(var document=JsonDocument.Parse(frameworkBytes)) {
            report.FrameworkMutationStatus=document.RootElement.GetProperty("status").GetString()!;
            foreach(var name in new[]{"cargo_mutants","stryker"}) {
                var toolReport=document.RootElement.GetProperty(name);
                report.FrameworkToolVersions[name]=toolReport.GetProperty("version").GetString()!;
                report.FrameworkToolStatuses[name]=toolReport.GetProperty("status").GetString()!;
                report.FrameworkNativeReportHashes[name]=toolReport.GetProperty("report_sha256").GetString();
            }
            foreach(var name in new[]{"cargo_mutants","stryker"})foreach(var count in new[]{"candidates","killed","survived","timed_out","invalid","not_covered","pending","other","ignored","unattempted"})
                report.FrameworkMutationCounts[name+"_"+count]=document.RootElement.GetProperty(name).GetProperty(count).GetInt32();
        }
        if(framework.TimedOut||framework.Exit!=0)throw new IOException("Generated mutation tooling failed; inspect framework.log.");
        if(selectedRegressions) {
            report.Phase="selected_mutations";Save();
            var regressionBudget=Remaining()*.25;
            if(regressionBudget>TimeSpan.FromMinutes(15))regressionBudget=TimeSpan.FromMinutes(15);
            var selected=await Execute("dotnet",["run","--file","scripts/Mutation.cs"],snapshot,regressionBudget,new(){["SPICE_MUTATION_TARGET_DIR"]=Path.Combine(root,"target","endurance-mutation"),["SPICE_MUTATION_TIMEOUT_SECONDS"]="900",["CARGO_BUILD_JOBS"]="2",["RUST_TEST_THREADS"]="2"});
            File.WriteAllText(Path.Combine(run,"mutation.log"),selected.Log);
            if(selected.TimedOut){report.MutationStatus="incomplete_timeout";throw new TimeoutException("Supplemental selected regressions exhausted their budget.");}
            if(selected.Exit!=0){report.MutationStatus="failed";throw new IOException("Supplemental selected regressions failed.");}
            report.MutationStatus="passed";
            File.Copy(Path.Combine(snapshot,"evidence","mutation.json"),Path.Combine(run,"mutation.json"));
        }
    } else {report.MutationStatus="skipped_explicitly";report.FrameworkMutationStatus="skipped_explicitly";}
    report.Phase="build_endurance"; Save();
    var environment=new Dictionary<string,string>{["CARGO_TARGET_DIR"]=Path.Combine(root,"target","endurance"),["CARGO_BUILD_JOBS"]="2",["RUST_TEST_THREADS"]="2",["RUST_LOG"]="off"};
    var build=await Execute("cargo",["test","--locked","--test","property_campaign","--no-run","--message-format=json"],snapshot,Cap(900),environment);
    File.WriteAllText(Path.Combine(run,"build.log"),build.Log);
    if(build.Exit!=0||build.TimedOut) throw new IOException("Endurance build failed or exhausted its budget; inspect build.log.");
    string? executable=null;
    foreach(var line in build.Log.Split('\n')) {
        if(!line.StartsWith('{')) continue;
        using var document=JsonDocument.Parse(line); var value=document.RootElement;
        if(value.TryGetProperty("reason",out var reason)&&reason.GetString()=="compiler-artifact"&&value.GetProperty("target").GetProperty("name").GetString()=="property_campaign"&&value.TryGetProperty("executable",out var path)&&path.ValueKind==JsonValueKind.String) executable=path.GetString();
    }
    if(executable is null) throw new IOException("Endurance test executable was not discovered.");
    var listing=await Execute(executable,["--list"],snapshot,Cap(15),environment);
    if(listing.Exit!=0||listing.TimedOut)throw new IOException("Property inventory failed.");
    report.PropertyCount=System.Text.RegularExpressions.Regex.Matches(listing.Log,@"(?m)^[^\r\n]+: test\r?$").Count;
    if(report.PropertyCount<=0||report.PropertyCount>128)throw new IOException("Invalid property inventory.");
    report.Phase="property_batches"; Save();
    while(Remaining()>TimeSpan.FromSeconds(1)) {
        var batchSeed=unchecked(seed+report.Cases*0x9e3779b97f4a7c15UL);
        var failure=Path.Combine(run,"failure");
        environment["PROPTEST_RNG_SEED"]=batchSeed.ToString(CultureInfo.InvariantCulture);
        environment["PROPTEST_CASES"]=report.PropertyCasesPerMethod.ToString(CultureInfo.InvariantCulture);
        report.PendingSeed=environment["PROPTEST_RNG_SEED"]; Save();
        var remaining=Remaining(); var result=await Execute(executable,["--nocapture"],snapshot,Cap(60),environment);
        if(result.TimedOut) {
            File.WriteAllText(Path.Combine(run,"last-batch.log"),result.Log);
            if(remaining<=TimeSpan.FromSeconds(60)) { report.Status="budget_exhausted_partial_batch"; break; }
            throw new TimeoutException("A seeded batch exceeded 60 seconds; replay pending_seed.");
        }
        if(result.Exit!=0||!result.Log.Contains($"test result: ok. {report.PropertyCount} passed; 0 failed;")) {
            File.WriteAllText(Path.Combine(run,"failure.log"),result.Log);
            EnsureDirectory(failure);
            var regression=Path.Combine(snapshot,"tests","regressions","property_campaign.txt");
            if(File.Exists(regression))File.Copy(regression,Path.Combine(failure,"property_campaign.txt"));
            throw new IOException("Property batch failed; replay pending_seed and retained library regression seeds.");
        }
        if(Remaining()<=TimeSpan.Zero) {
            // A process can complete during suspension or delayed scheduling.
            // Preserve its seed/log, but do not count work outside the deadline.
            report.LateCompletedRequestedCases+=(ulong)(report.PropertyCount*report.PropertyCasesPerMethod);
            File.WriteAllText(Path.Combine(run,"last-batch.log"),result.Log);
            report.Status="budget_exhausted_partial_batch";break;
        }
        // Count only requested successful cases. Extra replay/shrink attempts
        // are deliberately not guessed or compared with legacy ad-hoc throughput.
        report.Cases+=(ulong)(report.PropertyCount*report.PropertyCasesPerMethod); report.Batches++; report.PendingSeed=null; Save();
    }
    if(report.Cases==0) throw new TimeoutException("No seeded batch completed within the campaign budget.");
    if(report.Status=="running") report.Status="budget_exhausted";
} catch(Exception error) {
    report.Status="failed"; report.Error=error.ToString(); Environment.ExitCode=1;
} finally {
    powerRequest.Dispose();report.SleepPreventionReleased=powerRequest.Released;
    report.Status=budget.FinalStatus(report.Status);
    if(report.Status=="budget_overrun")Environment.ExitCode=1;
    report.Phase="finished";report.FinishedUtc=DateTime.UtcNow;Save();
}
Console.WriteLine(JsonSerializer.Serialize(report,options));

// Create only missing directories after checking each parent. Source snapshot
// deletion is separately restricted to the exact owned work directory above.
static void EnsureDirectory(string path) {
    if(Directory.Exists(path))return;
    var parent=Path.GetDirectoryName(path)??throw new IOException("Directory has no parent.");
    if(!Directory.Exists(parent))EnsureDirectory(parent);
    Directory.CreateDirectory(path);
}

/// Counts include completed batches only. Budget exhaustion is not a pass or coverage claim.
sealed class CampaignReport {
    public string Status { get;set; }="running";
    public string Phase { get;set; }="snapshot";
    public DateTime StartedUtc { get;set; }
    public DateTime? FinishedUtc { get;set; }
    public double BudgetSeconds { get;set; }
    public double ElapsedSeconds { get;set; }
    public DateTime DeadlineUtc {get;set;}
    public double MonotonicElapsedSeconds {get;set;}
    public double UtcElapsedSeconds {get;set;}
    public double BudgetOverrunSeconds {get;set;}
    public string SleepPrevention {get;set;}="not_requested";
    public bool? SleepPreventionReleased {get;set;}
    public ulong LateCompletedRequestedCases {get;set;}
    public string InitialSeed { get;set; }="";
    public string? PendingSeed { get;set; }
    public ulong Cases { get;set; }
    public ulong Batches { get;set; }
    public string PropertyLibrary {get;set;}="proptest 1.9.0";
    public string CaseCountKind {get;set;}="completed_requested_property_cases_excludes_replay_and_shrinks";
    public int PropertyCount {get;set;}
    public int PropertyCasesPerMethod {get;set;}=128;
    public ulong CheckpointRetries { get;set; }
    public string MutationStatus { get;set; }="not_run";
    public string FrameworkMutationStatus {get;set;}="not_run";
    public string? FrameworkShard {get;set;}
    public string? FrameworkSummarySha256 {get;set;}
    public Dictionary<string,int> FrameworkMutationCounts {get;set;}=new();
    public Dictionary<string,string> FrameworkToolVersions {get;set;}=new();
    public Dictionary<string,string> FrameworkToolStatuses {get;set;}=new();
    // Null distinguishes unavailable native outcome counts from a measured zero.
    public Dictionary<string,string?> FrameworkNativeReportHashes {get;set;}=new();
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

// Domain glue over the BCL clock: neither a backward UTC correction nor a
// monotonic clock paused by suspension is allowed to extend the campaign.
sealed class CampaignBudget(TimeSpan duration,TimeProvider provider)
{
    private readonly long started=provider.GetTimestamp();
    private TimeSpan maximumElapsed;
    public DateTimeOffset StartedUtc {get;}=provider.GetUtcNow();
    public DateTimeOffset DeadlineUtc=>StartedUtc+duration;
    public TimeSpan MonotonicElapsed=>provider.GetElapsedTime(started);
    public TimeSpan UtcElapsed=>TimeSpan.FromTicks(Math.Max(0,(provider.GetUtcNow()-StartedUtc).Ticks));
    public TimeSpan Elapsed {
        get {
            var observed=MonotonicElapsed>UtcElapsed?MonotonicElapsed:UtcElapsed;
            // A corrected forward UTC jump must not subsequently restore budget.
            if(observed>maximumElapsed)maximumElapsed=observed;
            return maximumElapsed;
        }
    }
    public TimeSpan Remaining=>TimeSpan.FromTicks(Math.Max(0,(duration-Elapsed).Ticks));
    public TimeSpan Overrun=>TimeSpan.FromTicks(Math.Max(0,(Elapsed-duration).Ticks));
    // Five seconds is explicitly cleanup/reporting allowance, not test budget.
    public string FinalStatus(string status)=>status.StartsWith("budget_exhausted",StringComparison.Ordinal)&&Overrun>TimeSpan.FromSeconds(5)?"budget_overrun":status;
    public static void CheckContracts() {
        var time=new SplitClock();var budget=new CampaignBudget(TimeSpan.FromSeconds(60),time);
        time.Advance(20,20);if(budget.Remaining!=TimeSpan.FromSeconds(40))throw new Exception("Ordinary budget failed.");
        time.Advance(0,120);if(budget.Remaining!=TimeSpan.Zero||budget.FinalStatus("budget_exhausted")!="budget_overrun")throw new Exception("Suspend/UTC jump extended deadline.");
        time.Advance(0,-120);if(budget.Remaining!=TimeSpan.Zero)throw new Exception("UTC correction restored expired budget.");
        time=new SplitClock();budget=new CampaignBudget(TimeSpan.FromSeconds(60),time);
        time.Advance(60,-3600);if(budget.Remaining!=TimeSpan.Zero||budget.Elapsed!=TimeSpan.FromSeconds(60))throw new Exception("Backward UTC extended monotonic budget.");
        time.Advance(6,0);if(budget.FinalStatus("failed")!="failed"||budget.FinalStatus("budget_exhausted_partial_batch")!="budget_overrun")throw new Exception("Overrun classification failed.");
        Console.WriteLine("Four split-clock deadline contracts passed.");
    }
    // Deliberately independent test clocks model OS suspension/time correction;
    // production uses TimeProvider.System. No production time source is replaced.
    private sealed class SplitClock:TimeProvider {
        private long timestamp;private DateTimeOffset utc=new(2026,1,1,0,0,0,TimeSpan.Zero);
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>timestamp;
        public override DateTimeOffset GetUtcNow()=>utc;
        public void Advance(int monotonicSeconds,int utcSeconds){timestamp+=TimeSpan.FromSeconds(monotonicSeconds).Ticks;utc+=TimeSpan.FromSeconds(utcSeconds);}
    }
}

// Process-owned Windows power requests survive async thread switches. They do
// not change persistent power policy, keep the display on, or override user sleep.
sealed class SystemPowerRequest:IDisposable
{
    private SafeFileHandle? handle;private IntPtr reason;
    public string Status {get;private set;}="not_requested_on_this_platform";
    public bool? Released {get;private set;}
    public static SystemPowerRequest Acquire() {
        var request=new SystemPowerRequest();
        if(!OperatingSystem.IsWindows())return request;
        request.reason=Marshal.StringToHGlobalUni("Spice bounded qualification campaign is active");
        var context=new ReasonContext {Version=0,Flags=1,SimpleReason=request.reason};
        request.handle=PowerCreateRequest(ref context);
        if(request.handle.IsInvalid)request.Status="windows_request_unavailable_"+Marshal.GetLastWin32Error();
        else if(!PowerSetRequest(request.handle,0))request.Status="windows_request_denied_"+Marshal.GetLastWin32Error();
        else {request.Status="windows_system_required_requested";return request;}
        request.Dispose();return request;
    }
    public void Dispose() {
        if(handle is not null) {
            if(!handle.IsInvalid&&Status=="windows_system_required_requested")Released=PowerClearRequest(handle,0);
            handle.Dispose();handle=null;
        }
        if(reason!=IntPtr.Zero){Marshal.FreeHGlobal(reason);reason=IntPtr.Zero;}
    }
    // Space for the complete 64-bit native union, not only its simple-string arm.
    [StructLayout(LayoutKind.Explicit,Size=32)] private struct ReasonContext {
        [FieldOffset(0)] public uint Version;[FieldOffset(4)] public uint Flags;[FieldOffset(8)] public IntPtr SimpleReason;
    }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern SafeFileHandle PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PowerSetRequest(SafeFileHandle handle,int type);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PowerClearRequest(SafeFileHandle handle,int type);
}
