using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Simulator.Syntax;

if(args is ["--inventory",var primaryReport,var replayReport,var adjudications,var registry,var outputStem])
{
    InventoryCommand.Run(primaryReport,replayReport,adjudications,registry,outputStem);
    return;
}

// Campaign limits are explicit configuration, independent of language support.
var maxBytes=Setting("SPICE_PARSE_MAX_BYTES",4_194_304,1,67_108_864);
var maxTokens=Setting("SPICE_PARSE_MAX_TOKENS",2_000_000,1,4_000_000);
var workerSeconds=Setting("SPICE_PARSE_WORKER_SECONDS",20,1,300);
var campaignMinutes=Setting("SPICE_PARSE_CAMPAIGN_MINUTES",30,1,1440);
var workers=Setting("SPICE_PARSE_WORKERS",1,1,4);
var legacyBytes=Setting("SPICE_PARSE_LEGACY_BYTES",0,0,1)==1;
var collectFeatures=Setting("SPICE_PARSE_FEATURES",0,0,1)==1;
var collectSemantics=Setting("SPICE_PARSE_SEMANTICS",0,0,1)==1;
var collectDeclarations=Setting("SPICE_PARSE_DECLARATIONS",0,0,1)==1||collectSemantics;

// A worker parses only. There is no simulator, preprocessor, shell interpreter,
// resource resolution or numerical execution on this worker call path.
if (args is ["--worker",var input])
{
    WorkerResult result;
    string? digest=null;
    try
    {
        var file = new FileInfo(input);
        if (!file.Exists) result = new("input_unavailable",0,[],null);
        else if ((file.Attributes & FileAttributes.ReparsePoint)!=0 || file.Length>maxBytes)
            result = new("resource_rejected",0,[],null);
        else
        {
            var bytes=File.ReadAllBytes(input);
            digest=Convert.ToHexStringLower(SHA256.HashData(bytes));
            string text;var encoding="utf-8";
            try { text=new UTF8Encoding(false,true).GetString(bytes); }
            catch(DecoderFallbackException) when(legacyBytes)
            {
                // Explicit opt-in, lossless byte-to-character mapping. This does
                // not claim the author's actual code page or repair broken UTF-8.
                text=Encoding.Latin1.GetString(bytes);encoding="latin1-byte-preserving";
            }
            var syntax=DeckParser.Parse(text.TrimStart('\uFEFF'),new ParserLimits(maxBytes,maxTokens),collectFeatures,collectDeclarations);
            result=new(syntax.Status,syntax.Statements.Length,syntax.Diagnostics.Take(3).ToArray(),digest,encoding,syntax.Format,
                collectFeatures&&syntax.Status=="accepted_structure"?CapabilityInventory.Tally(syntax.Features):null,
                syntax.Declarations is { } catalog?DeclarationSummary.From(catalog):null,
                collectSemantics&&syntax.Declarations is {} semanticCatalog?SemanticSummary.From(semanticCatalog):null);
        }
    }
    catch (DecoderFallbackException) { result=new("encoding_rejected",0,[],digest); }
    catch (IOException e) { result=new("input_unavailable",0,[new("io",e.GetType().Name,0,0)],null); }
    catch (UnauthorizedAccessException) { result=new("input_unavailable",0,[],null); }
    Console.WriteLine(JsonSerializer.Serialize(result));
    return;
}
if (args.Length is not (3 or 4))
    throw new ArgumentException("Usage: <upstream inventory JSON> <repository root> <output JSON> [corpus root override]");
var inventoryPath=args[0];var repository=args[1];var output=args[2];
repository=Path.GetFullPath(repository);
if (!Directory.Exists(repository) || !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
    throw new DirectoryNotFoundException("Repository and output parent must exist");
using var inventory=JsonDocument.Parse(File.ReadAllText(inventoryPath));
var upstream=args.Length==4?Path.GetFullPath(args[3]):inventory.RootElement.GetProperty("corpus_root").GetString()!;
if (!Directory.Exists(upstream)) throw new DirectoryNotFoundException(upstream);
var inputs=new List<(string Scope,string Root,string Path)>();
foreach(var row in inventory.RootElement.GetProperty("rows").EnumerateArray())
    inputs.Add(("ngspice",upstream,row.GetProperty("path").GetString()!));
var tracked=await Git("ls-files","-z","--","*.cir","*.sp","*.spice","*.ckt","*.deck","*.net");
foreach(var path in tracked.Split('\0',StringSplitOptions.RemoveEmptyEntries)) inputs.Add(("repository",repository,path));
var provenance=new Dictionary<string,string>();
foreach(var folder in new[]{"dotnet/Simulator.Syntax","dotnet/Simulator.Syntax.Corpus"})
{
    foreach(var file in Directory.EnumerateFiles(Path.Combine(repository,folder),"*",SearchOption.AllDirectories)
        .Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(s=>s is "bin" or "obj")))
        provenance[Path.GetRelativePath(repository,file).Replace('\\','/')]=Hash(file);
}
var sourceCommit=(await Git("rev-parse","HEAD")).Trim();
var dirty=!string.IsNullOrWhiteSpace(await Git("status","--porcelain"));
var corpusDirty=!string.IsNullOrWhiteSpace(await GitAt(upstream,"status","--porcelain"));
var corpusCommit=(await GitAt(upstream,"rev-parse","HEAD")).Trim();
var binaries=new Dictionary<string,string>();
foreach(var file in Directory.EnumerateFiles(AppContext.BaseDirectory,"*.dll"))binaries[Path.GetFileName(file)]=Hash(file);
var started=DateTimeOffset.UtcNow;
var watch=Stopwatch.StartNew();
var rows=new object?[inputs.Count];
var counts=new ConcurrentDictionary<string,int>(StringComparer.Ordinal);
var completed=0;
var checkpointLock=new object();
WriteReport("running");
await Parallel.ForEachAsync(Enumerable.Range(0,inputs.Count),new ParallelOptions{MaxDegreeOfParallelism=workers},async (index,_)=>
{
    var (scope,root,path)=inputs[index];
    var absolute=Path.GetFullPath(path,root);
    var prefix=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar;
    if (!absolute.StartsWith(prefix,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
        throw new InvalidDataException("Inventory path escaped its source root");
    var result=watch.Elapsed.TotalMinutes<campaignMinutes ? await RunWorker(absolute,workerSeconds) : new("budget_unattempted",0,[],null);
    rows[index]=new {scope,path,result};
    counts.AddOrUpdate(result.Status,1,(_,count)=>count+1);
    var done=Interlocked.Increment(ref completed);
    if(done%100==0)
    {
        Console.WriteLine($"Parsed {done}/{inputs.Count} inputs");
        lock(checkpointLock)WriteReport("running");
    }
});
var unchanged=provenance.All(pair=>Hash(Path.Combine(repository,pair.Key))==pair.Value)
    && binaries.All(pair=>Hash(Path.Combine(AppContext.BaseDirectory,pair.Key))==pair.Value);
WriteReport(unchanged?"completed_with_results":"provenance_changed");
if(!unchanged)throw new IOException("Syntax sources changed during the campaign; results cannot qualify current source");
Console.WriteLine(JsonSerializer.Serialize(new{output,counts,inputs=inputs.Count}));

void WriteReport(string state)
{
var report=new {
    schema_version=2,state,started_utc=started,updated_utc=DateTimeOffset.UtcNow,elapsed_seconds=watch.Elapsed.TotalSeconds,
    source_commit=sourceCommit,source_dirty=dirty,
    inventory_sha256=Hash(inventoryPath),corpus_commit=corpusCommit,
    inventory_corpus_commit=inventory.RootElement.GetProperty("corpus_commit").GetString(),
    corpus_root=upstream,corpus_dirty=corpusDirty,
    framework="ANTLR generator/runtime 4.13.1",runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    inputs=inputs.Count,completed,counts,source_sha256=provenance,binary_sha256=binaries,rows=rows.Where(row=>row is not null).ToArray(),
    limits=new{input_bytes=maxBytes,tokens=maxTokens,nesting=128,rule_depth=512,worker_seconds=workerSeconds,campaign_minutes=campaignMinutes,workers,legacy_bytes=legacyBytes,collect_features=collectFeatures,collect_declarations=collectDeclarations,collect_semantics=collectSemantics,diagnostics_per_row=3},
    limitations=new[]{"Structural acceptance only; not complete ngspice device/parameter/expression dialect conformance.",
        "Control blocks and quoted expressions are retained without validating the embedded language.",
        collectSemantics?"Includes are not loaded; observed reference binding and root parameters are separate bounded profiles, not full elaboration or support.":"Includes are not loaded; model references, subcircuit binding and physical parameter validity are not checked.",
        "Syntax rejection means rejection by this grammar, not independent adjudication that an upstream deck is malformed.",
        "Numerical support is not evaluated; missing, timed-out or budget-unattempted inputs are not passes.",
        "Windows execution only; no Linux, NativeAOT or Rust-core qualification from this campaign."}
};
// Atomic checkpoints keep interrupted work inspectable; raw artifacts stay local.
File.WriteAllText(output+".tmp",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
File.Move(output+".tmp",output,true);
}

async Task<string> Git(params string[] arguments)=>await GitAt(repository,arguments);
static async Task<string> GitAt(string root,params string[] arguments)
{
    var start=new ProcessStartInfo("git"){RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    start.ArgumentList.Add("-C");start.ArgumentList.Add(root);
    foreach(var argument in arguments)start.ArgumentList.Add(argument);
    using var process=Process.Start(start)!;
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if(process.ExitCode!=0)throw new IOException(await stderr);
    return await stdout;
}
static async Task<WorkerResult> RunWorker(string path,int seconds)
{
    var start=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    start.ArgumentList.Add(typeof(WorkerResult).Assembly.Location);start.ArgumentList.Add("--worker");start.ArgumentList.Add(path);
    using var process=Process.Start(start)!;
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    try { await process.WaitForExitAsync(cancel.Token); }
    catch(OperationCanceledException)
    {
        process.Kill(entireProcessTree:true);await process.WaitForExitAsync();
        await Task.WhenAll(stdout,stderr);
        return new("timeout",0,[],null);
    }
    if(process.ExitCode!=0) return new("worker_failed",0,[new("worker_exit",$"Exit {process.ExitCode}; {Bound(await stderr)}",0,0)],null);
    try { return JsonSerializer.Deserialize<WorkerResult>(await stdout) ?? new("worker_failed",0,[],null); }
    catch(JsonException) { return new("worker_failed",0,[new("protocol","Invalid worker JSON",0,0)],null); }
}
static string Bound(string text)=>text.Length<=512?text:text[..512];
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static int Setting(string name,int fallback,int minimum,int maximum)
{
    var text=Environment.GetEnvironmentVariable(name);
    if(text is null)return fallback;
    if(!int.TryParse(text,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var value)
        || value<minimum || value>maximum)throw new ArgumentException($"{name} must be an integer in [{minimum},{maximum}]");
    return value;
}
internal sealed record WorkerResult(string Status,int Statements,SyntaxDiagnostic[] Diagnostics,string? Sha256,string? Encoding=null,string? InputKind=null,FeatureTally[]? Features=null,DeclarationSummary? Declarations=null,SemanticSummary? Semantics=null);
