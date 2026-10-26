#:property PublishAot=false
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

// Run only inside a dedicated checkout. Publication stages an explicit small
// JSON allowlist on a unique evidence branch, never main or arbitrary logs.
var minutes=300.0; var label="lab"; var publish=true; var skipMutation=false; var skipCoverage=false; string? import=null;
for(var i=0;i<args.Length;i++) switch(args[i]) {
    case "--minutes": minutes=double.Parse(args[++i],CultureInfo.InvariantCulture);break;
    case "--label": label=args[++i];break;
    case "--no-publish":publish=false;break;
    case "--skip-mutation":skipMutation=true;break;
    case "--skip-coverage":skipCoverage=true;break;
    case "--import-run":import=Path.GetFullPath(args[++i]);break;
    default:throw new ArgumentException("Unknown remote-run argument: "+args[i]);
}
if(!double.IsFinite(minutes)||minutes<=0||minutes>1440) throw new ArgumentException("Minutes must be in (0,1440].");
if(!Regex.IsMatch(label,@"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,47}\z")) throw new ArgumentException("Machine label must be 1..48 ASCII letters/digits, hyphen or underscore.");
var root=Directory.GetCurrentDirectory();
if(!File.Exists("Cargo.toml")) throw new ArgumentException("Run from a Spice checkout.");
var platform=OperatingSystem.IsWindows()?"windows":OperatingSystem.IsLinux()?"linux":"unsupported";
if(platform=="unsupported") throw new PlatformNotSupportedException();
var identity=platform+"-"+RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()+"-"+label.ToLowerInvariant();
var runId=DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")+"-"+Guid.NewGuid().ToString("N")[..8];
var local=Path.Combine(root,"work","remote-runs",runId);Directory.CreateDirectory(local);
var json=new JsonSerializerOptions { WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower };
async Task<CommandResult> Command(string file,IEnumerable<string> arguments,string log,int seconds=1800) {
    var start=new ProcessStartInfo(file) { WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
    foreach(var arg in arguments)start.ArgumentList.Add(arg);
    start.Environment["GIT_TERMINAL_PROMPT"]="0";
    start.Environment["CARGO_BUILD_JOBS"]="2"; start.Environment["RUST_TEST_THREADS"]="2";
    using var process=Process.Start(start)??throw new IOException("Could not start "+file);
    async Task<string> Drain(StreamReader stream) { var text=new System.Text.StringBuilder();var buffer=new char[4096];int size;while((size=await stream.ReadAsync(buffer))>0)if(text.Length<1_048_576)text.Append(buffer,0,Math.Min(size,1_048_576-text.Length));return text.ToString(); }
    var stdout=Drain(process.StandardOutput);var stderr=Drain(process.StandardError);var timeout=false;
    using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    try { await process.WaitForExitAsync(limit.Token); }catch(OperationCanceledException){timeout=true;process.Kill(true);await process.WaitForExitAsync();}
    var output=await stdout;var error=await stderr;
    await File.WriteAllTextAsync(Path.Combine(local,log),output+"\n"+error);
    return new(process.ExitCode,timeout,output);
}
async Task<string> Git(params string[] arguments) { var result=await Command("git",arguments,"git-local.log",120);if(result.Exit!=0||result.TimedOut)throw new IOException("Git operation failed; inspect "+Path.Combine(local,"git-local.log"));return result.Output.Trim(); }
var source=await Git("rev-parse","HEAD");
if(!Regex.IsMatch(source,"\\A[0-9a-f]{40}\\z"))throw new IOException("Invalid source revision.");
if((await Git("status","--porcelain")).Length!=0)throw new IOException("Remote qualification requires a clean dedicated checkout.");
var branch=$"remote-runs/{identity}/{runId}-{source[..12]}";
if(publish)await Git("push","--dry-run","--","origin",$"HEAD:refs/heads/{branch}");
var report=new RemoteReport { RunId=runId,MachineLabel=identity,SourceCommit=source,Branch=branch,StartedUtc=DateTime.UtcNow,RequestedMinutes=minutes,Mode=import is null?"full_workflow":"endurance_only_import",Platform=RuntimeInformation.OSDescription,Architecture=RuntimeInformation.ProcessArchitecture.ToString() };
async Task<bool> Step(string name,string file,IEnumerable<string> arguments,int timeout=1800) {
    var result=await Command(file,arguments,name+".log",timeout);
    report.Steps.Add(new(name,result.TimedOut?"timed_out":result.Exit==0?"passed":"failed",result.Exit,Hash(Path.Combine(local,name+".log"))));
    return result.Exit==0&&!result.TimedOut;
}
var checksPassed=true;string? run=import;
if(import is null) {
    checksPassed=await Step("contracts","pwsh",["-NoProfile","-File","scripts/Test.ps1"]);
    var python=Environment.GetEnvironmentVariable("SPICE_STRICTDOC_PYTHON");
    if(python is null) {report.Steps.Add(new("strictdoc","unavailable",null,null));checksPassed=false;}
    else checksPassed=await Step("strictdoc",python,["-c","from pathlib import Path; from strictdoc.backend.sdoc.reader import SDReader; files=list(Path('docs').glob('*.sdoc')); [SDReader.read(p.read_text(encoding='utf-8-sig'),str(p)) for p in files]; print(f'Parsed {len(files)} documents')"])&&checksPassed;
    if(checksPassed) {
        var arguments=new List<string>{"run","--file","scripts/Endurance.cs","--","--minutes",minutes.ToString(CultureInfo.InvariantCulture)};
        if(skipMutation)arguments.Add("--skip-mutation");
        var passed=await Step("endurance","dotnet",arguments,checked((int)Math.Ceiling(minutes*60)+120));
        checksPassed&=passed;
        // Use the emitted absolute run path, not a stale newest-directory guess.
        var log=await File.ReadAllTextAsync(Path.Combine(local,"endurance.log"));
        var marker=Regex.Match(log,@"(?m)^Campaign summary: (.+)\r?$");
        if(marker.Success)run=Path.GetDirectoryName(marker.Groups[1].Value.Trim());
    }
}
if(run is not null) {
    var path=Path.Combine(run,"summary.json");
    if(!File.Exists(path)||new FileInfo(path).Length>256*1024)throw new IOException("Missing or oversized endurance summary.");
    report.Endurance=JsonSerializer.Deserialize<EnduranceEvidence>(await File.ReadAllTextAsync(path),json)??throw new IOException("Invalid endurance summary.");
    var evidence=report.Endurance;
    if(evidence.SourceCommit!=source||evidence.SourceDirty)throw new IOException("Endurance source revision/cleanliness does not match this checkout.");
    if(!new[]{"failed","budget_exhausted","budget_exhausted_partial_batch"}.Contains(evidence.Status)||evidence.FinishedUtc is null||evidence.FinishedUtc<evidence.StartedUtc||!double.IsFinite(evidence.BudgetSeconds)||evidence.BudgetSeconds<=0||evidence.BudgetSeconds>86400||!double.IsFinite(evidence.ElapsedSeconds)||evidence.ElapsedSeconds<0||!ulong.TryParse(evidence.InitialSeed,out _))throw new IOException("Malformed or unfinished endurance report.");
    if(!new[]{"passed","failed","not_run","skipped_explicitly","incomplete_timeout"}.Contains(evidence.MutationStatus))throw new IOException("Unknown mutation state.");
    if(import is not null)report.RequestedMinutes=evidence.BudgetSeconds/60.0;
    // Preserve old archived runs while making the new case-count meaning
    // explicit. Library replay/shrink attempts are not fabricated case counts.
    var propertyMode=evidence.CaseCountKind=="completed_requested_property_cases_excludes_replay_and_shrinks";
    if(propertyMode) {
        if(evidence.PropertyLibrary!="proptest 1.9.0"||evidence.PropertyCount<1||evidence.PropertyCount>128||
            evidence.PropertyCasesPerMethod<1||evidence.PropertyCasesPerMethod>100000)throw new IOException("Invalid property runner metadata.");
        var perBatch=(ulong)evidence.PropertyCount*(ulong)evidence.PropertyCasesPerMethod;
        if(evidence.Batches>ulong.MaxValue/perBatch||evidence.Cases!=evidence.Batches*perBatch)throw new IOException("Inconsistent completed property case count.");
    } else if(evidence.CaseCountKind!="legacy_seeded_cases"||evidence.PropertyLibrary is not null||evidence.PropertyCount!=0||evidence.PropertyCasesPerMethod!=0)
        throw new IOException("Unknown or contradictory case-count provenance.");
    foreach(var required in new[]{"Cargo.toml","Cargo.lock",propertyMode?"tests/property_campaign.rs":"tests/endurance.rs"})if(!evidence.SourceHashes.ContainsKey(required))throw new IOException("Missing source hash: "+required);
    foreach(var (relative,hash) in evidence.SourceHashes) {
        if(relative.Contains('\\')||relative.Split('/').Any(p=>p is "" or "." or "..")||Path.IsPathRooted(relative)||!Regex.IsMatch(hash,"\\A[0-9a-f]{64}\\z"))throw new IOException("Unsafe source hash entry.");
        if(!new[]{"Cargo.toml","Cargo.lock","rust-toolchain.toml","global.json","dotnet-tools.json","Makefile"}.Contains(relative)&&!new[]{"src/","tests/","scripts/","compat/","examples/","dotnet/"}.Any(prefix=>relative.StartsWith(prefix,StringComparison.Ordinal)))throw new IOException("Source hash path is outside the source allowlist.");
        if(!File.Exists(relative)||Hash(relative)!=hash)throw new IOException("Tested source hash mismatch: "+relative);
    }
    checksPassed&=evidence.Status.StartsWith("budget_exhausted",StringComparison.Ordinal)&&evidence.Cases>0&&evidence.MutationStatus is not ("failed" or "incomplete_timeout");
    if(!new[]{"not_run","skipped_explicitly","completed","partial_timeout","failed"}.Contains(evidence.FrameworkMutationStatus))throw new IOException("Unknown framework mutation state.");
    if(evidence.FrameworkSummarySha256 is not null) {
        var frameworkPath=Path.Combine(run,"framework-mutations.json");
        if(!Regex.IsMatch(evidence.FrameworkSummarySha256,"\\A[0-9a-f]{64}\\z")||!File.Exists(frameworkPath)||new FileInfo(frameworkPath).Length>65536||Hash(frameworkPath)!=evidence.FrameworkSummarySha256)throw new IOException("Framework summary hash mismatch.");
        using var framework=JsonDocument.Parse(File.ReadAllText(frameworkPath));
        if(framework.RootElement.GetProperty("status").GetString()!=evidence.FrameworkMutationStatus||framework.RootElement.GetProperty("shard").GetString()!=evidence.FrameworkShard)throw new IOException("Framework summary metadata mismatch.");
        var expected=new Dictionary<string,int>();
        foreach(var tool in new[]{"cargo_mutants","stryker"})foreach(var count in new[]{"candidates","killed","survived","timed_out","invalid","not_covered","pending","other","ignored","unattempted"})expected[tool+"_"+count]=framework.RootElement.GetProperty(tool).GetProperty(count).GetInt32();
        if(expected.Any(pair=>pair.Value<0||!evidence.FrameworkMutationCounts.TryGetValue(pair.Key,out var value)||value!=pair.Value)||expected.Count!=evidence.FrameworkMutationCounts.Count)throw new IOException("Framework count mismatch.");
        foreach(var tool in new[]{"cargo_mutants","stryker"}) {
            var completed=expected.Where(pair=>pair.Key.StartsWith(tool+"_",StringComparison.Ordinal)&&pair.Key!=tool+"_candidates"&&pair.Key!=tool+"_unattempted").Sum(pair=>(long)pair.Value);
            if(completed+expected[tool+"_unattempted"]!=expected[tool+"_candidates"])throw new IOException("Inconsistent framework accounting.");
            if(!evidence.FrameworkToolVersions.TryGetValue(tool,out var version)||version!=framework.RootElement.GetProperty(tool).GetProperty("version").GetString())throw new IOException("Framework version mismatch.");
            if(!evidence.FrameworkToolStatuses.TryGetValue(tool,out var status)||!new[]{"completed","partial_timeout","failed","not_run","skipped_explicitly"}.Contains(status)||status!=framework.RootElement.GetProperty(tool).GetProperty("status").GetString())throw new IOException("Framework tool status mismatch.");
            if(!evidence.FrameworkNativeReportHashes.TryGetValue(tool,out var nativeHash)||nativeHash!=framework.RootElement.GetProperty(tool).GetProperty("report_sha256").GetString()||(nativeHash is not null&&!Regex.IsMatch(nativeHash,"\\A[0-9a-f]{64}\\z")))throw new IOException("Framework native report availability/hash mismatch.");
        }
        if(evidence.FrameworkToolVersions.Count!=2||evidence.FrameworkToolStatuses.Count!=2||evidence.FrameworkNativeReportHashes.Count!=2||evidence.FrameworkToolVersions["cargo_mutants"]!="27.1.0"||evidence.FrameworkToolVersions["stryker"]!="5.0.0")throw new IOException("Unexpected framework tool metadata.");
        if(evidence.FrameworkShard is null||!Regex.IsMatch(evidence.FrameworkShard,"\\A[0-9]{1,4}/[0-9]{1,4}\\z"))throw new IOException("Unsafe framework shard.");
        var shardParts=evidence.FrameworkShard.Split('/').Select(int.Parse).ToArray();
        if(shardParts[1]<1||shardParts[1]>1024||shardParts[0]>=shardParts[1])throw new IOException("Invalid framework shard.");
    } else if(evidence.FrameworkMutationCounts.Count!=0||evidence.FrameworkToolVersions.Count!=0||evidence.FrameworkToolStatuses.Count!=0||evidence.FrameworkNativeReportHashes.Count!=0||evidence.FrameworkShard is not null||evidence.FrameworkMutationStatus is "completed" or "partial_timeout")throw new IOException("Missing framework provenance.");
    checksPassed&=evidence.FrameworkMutationStatus!="failed";
    var mutation=Path.Combine(run,"mutation.json");
    if(File.Exists(mutation)) {
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(mutation));var value=document.RootElement;
        report.Mutations=new(value.GetProperty("killed").GetInt32(),value.GetProperty("total").GetInt32());
        if(report.Mutations.Killed<0||report.Mutations.Total<report.Mutations.Killed)throw new IOException("Invalid mutation counts.");
    }
}
if(import is null&&!skipCoverage) {
    var priorCoverage=File.Exists("evidence/rust-coverage.json")?File.ReadAllBytes("evidence/rust-coverage.json"):null;
    try {
    var measured=await Step("rust_coverage","pwsh",["-NoProfile","-File","scripts/Test-Coverage.ps1"]);
    if(measured&&File.Exists("evidence/rust-coverage.json")) {
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync("evidence/rust-coverage.json"));var value=document.RootElement;
        report.Coverage=new("measured",value.GetProperty("lines").Clone(),value.GetProperty("functions").Clone(),value.GetProperty("regions").Clone());
    }else report.Coverage=new("unavailable",null,null,null);
    } finally { if(priorCoverage is null)File.Delete("evidence/rust-coverage.json");else File.WriteAllBytes("evidence/rust-coverage.json",priorCoverage); }
}else report.Coverage=new("not_measured",null,null,null);
report.Outcome=checksPassed?"completed_with_declared_limits":"test_failure";
report.FinishedUtc=DateTime.UtcNow;
var relativeDirectory=$"evidence/remote-runs/{identity}/{runId}-{source[..12]}";
Directory.CreateDirectory(relativeDirectory);
var manifest=Path.Combine(relativeDirectory,"manifest.json");
var payload=JsonSerializer.SerializeToUtf8Bytes(report,json);
if(payload.Length>256*1024)throw new IOException("Remote evidence exceeds 256 KiB cap.");
await File.WriteAllBytesAsync(manifest,payload);
await File.WriteAllTextAsync(Path.Combine(relativeDirectory,"checksums.json"),JsonSerializer.Serialize(new Dictionary<string,string>{{"manifest.json",Hash(manifest)}},json));
if(publish) {
    await Git("switch","-c",branch,source);
    await Git("add","--",relativeDirectory);
    var staged=(await Git("diff","--cached","--name-only")).Split('\n',StringSplitOptions.RemoveEmptyEntries);
    if(staged.Length!=2||staged.Any(path=>path!=relativeDirectory+"/manifest.json"&&path!=relativeDirectory+"/checksums.json"))throw new IOException("Unexpected staged paths; publication stopped.");
    await Git("-c","user.name=Spice Qualification Runner","-c","user.email=spice-qualification@localhost","commit","-m",$"Remote qualification {identity} {runId} source {source[..12]}");
    await Git("push","--","origin",$"HEAD:refs/heads/{branch}");
}
Console.WriteLine($"Evidence: {relativeDirectory}; branch: {branch}; published: {publish}; outcome: {report.Outcome}");
if(!checksPassed)Environment.ExitCode=1;
static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

/// Export an explicit schema rather than upload raw logs, absolute local paths,
/// downloaded binaries or failure corpora. Hashes permit local artifact correlation.
sealed class RemoteReport {
    public string SchemaVersion {get;set;}="1";
    public string RunId {get;set;}="";public string MachineLabel {get;set;}="";public string SourceCommit {get;set;}="";public string Branch {get;set;}="";
    public string Platform {get;set;}="";public string Architecture {get;set;}="";public string Mode {get;set;}="";public string Outcome {get;set;}="";
    public DateTime StartedUtc {get;set;}public DateTime FinishedUtc {get;set;}public double RequestedMinutes {get;set;}
    public List<StepEvidence> Steps {get;set;}=new();public EnduranceEvidence? Endurance {get;set;}public MutationEvidence? Mutations {get;set;}public CoverageEvidence? Coverage {get;set;}
}
sealed record StepEvidence(string Name,string Status,int? ExitCode,string? LocalLogSha256);
sealed record MutationEvidence(int Killed,int Total);
sealed record CoverageEvidence(string Status,JsonElement? Lines,JsonElement? Functions,JsonElement? Regions,
    string? BranchCoverage=null,string Scope="owned Rust src/*.rs; managed/vendor/GPU excluded");
sealed class EnduranceEvidence {
    public string Status {get;set;}="";public string SourceCommit {get;set;}="";public bool SourceDirty {get;set;}
    public DateTime StartedUtc {get;set;}public DateTime? FinishedUtc {get;set;}public double BudgetSeconds {get;set;}public double ElapsedSeconds {get;set;}
    public ulong Cases {get;set;}public ulong Batches {get;set;}public ulong CheckpointRetries {get;set;}public string InitialSeed {get;set;}="";public string? PendingSeed {get;set;}
    public string CaseCountKind {get;set;}="legacy_seeded_cases";
    public string? PropertyLibrary {get;set;}
    public int PropertyCount {get;set;}public int PropertyCasesPerMethod {get;set;}
    public string MutationStatus {get;set;}="";public string Rustc {get;set;}="";public string? RustcVerbose {get;set;}public string Dotnet {get;set;}="";
    public Dictionary<string,string> SourceHashes {get;set;}=new();
    public string FrameworkMutationStatus {get;set;}="not_run";public string? FrameworkShard {get;set;}public string? FrameworkSummarySha256 {get;set;}
    public Dictionary<string,int> FrameworkMutationCounts {get;set;}=new();
    public Dictionary<string,string> FrameworkToolVersions {get;set;}=new();
    public Dictionary<string,string> FrameworkToolStatuses {get;set;}=new();
    public Dictionary<string,string?> FrameworkNativeReportHashes {get;set;}=new();
}
sealed record CommandResult(int Exit,bool TimedOut,string Output);
