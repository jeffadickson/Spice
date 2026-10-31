using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Simulator.Benchmarks;
internal static class CorpusCommand
{
    // The reusable runner publishes one issue per invocation only after recording
    // local outcomes. Offline callers explicitly select --publish-issue false.
    internal static async Task<int> Run(string[] args)
    {
        CorpusReport? report=null;string? reportPath=null;string? ownerRoot=null;
        try
        {
            var options=new Dictionary<string,string>(StringComparer.Ordinal);
            for(var i=0;i<args.Length;i+=2)
                if(i+1>=args.Length||args[i] is not("--root" or "--engine" or "--ngspice" or "--corpus-root" or "--output" or "--budget-seconds" or "--publish-issue" or "--inventory-mode")||!options.TryAdd(args[i],args[i+1]))
                    throw new ArgumentException("Invalid or duplicate corpus option");
            string Get(string name,string fallback)=>options.GetValueOrDefault(name,fallback);
            var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Get("--root",Directory.GetCurrentDirectory())));
            ownerRoot=root;
            var corpus=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Get("--corpus-root","")));
            if(!Directory.Exists(root)||!File.Exists(Path.Combine(root,"Cargo.toml"))||!Directory.Exists(corpus))throw new DirectoryNotFoundException("Expected existing Spice and corpus roots");
            var engine=Path.GetFullPath(Get("--engine","target/release/circuit-simulator"+(OperatingSystem.IsWindows()?".exe":"")),root);
            var ngspice=Path.GetFullPath(Get("--ngspice","ngspice"),root);
            if(!File.Exists(engine)||!File.Exists(ngspice))throw new FileNotFoundException("Supply existing engine/ngspice executables");
            var seconds=int.Parse(Get("--budget-seconds","1800"),CultureInfo.InvariantCulture);
            if(seconds is < 1 or > 21600)throw new ArgumentException("Corpus budget must be 1..21600 seconds");
            var publish=bool.Parse(Get("--publish-issue","true"));
            var mode=Get("--inventory-mode","tracked");if(mode is not("tracked" or "files"))throw new ArgumentException("Inventory mode must be tracked or files");
            var output=Path.GetFullPath(Get("--output","evidence/ngspice-corpus"),root);Ensure(output);
            using var runLock=new FileStream(Path.Combine(output,"campaign.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var historyPath=Path.Combine(output,"history.json");
            var history=File.Exists(historyPath)?JsonSerializer.Deserialize(Runner.Input(historyPath),CorpusJson.Default.CorpusHistoryArray)??[]:[];
            if(history.Length>=1000||history.Any(h=>h.Total<0||h.Passed<0||h.Passed>h.Total))throw new InvalidDataException("Invalid/full corpus history");
            var runId=DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")[..8];
            var raw=Path.Combine(root,"build","corpus",runId);Ensure(raw);
            async Task<string> Metadata(string exe,string[] argv,string cwd)
            {
                var p=await ProcessRunner.Run(exe,argv,null,cwd,TimeSpan.FromSeconds(15));
                if(p.ExitCode!=0)throw new IOException($"Metadata command failed: {exe} {string.Join(' ',argv)}");return p.Output.Trim();
            }
            var gitProbe=await ProcessRunner.Run("git",["rev-parse","--show-toplevel"],null,corpus,TimeSpan.FromSeconds(15));
            var ownGitRoot=gitProbe.ExitCode==0&&Path.GetFullPath(gitProbe.Output.Trim()).Equals(corpus,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
            if(mode=="tracked"&&!ownGitRoot)
                throw new InvalidDataException("Tracked inventory requires the corpus Git root; use files mode explicitly for a subdirectory");
            report=new CorpusReport{RunId=runId,StartedUtc=DateTimeOffset.UtcNow.ToString("O"),RawDirectory=raw,CorpusRoot=corpus,InventoryMode=mode,
                SourceCommit=await Metadata("git",["rev-parse","HEAD"],root),CorpusCommit=ownGitRoot?await Metadata("git",["rev-parse","HEAD"],corpus):"unversioned files-mode directory",
                NgspiceVersion=await Metadata(ngspice,["-n","--version"],raw),IssueState=publish?"pending":"offline"};
            report.SourceDirty=(await Metadata("git",["status","--porcelain"],root)).Length!=0;
            report.CorpusDirty=ownGitRoot?(await Metadata("git",["status","--porcelain"],corpus)).Length!=0:null;
            report.Hashes["engine"]=Runner.Hash(engine);report.Hashes["ngspice"]=Runner.Hash(ngspice);
            report.HelperMode=System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported?"managed":"native_aot";
            var helper=report.HelperMode=="managed"?Path.Combine(AppContext.BaseDirectory,"Simulator.Benchmarks.dll"):Environment.ProcessPath!;
            report.Hashes["executed_helper"]=Runner.Hash(helper);
            foreach(var folder in new[]{"src","dotnet/Simulator.Benchmarks","dotnet/Simulator.Client"})
                foreach(var path in Directory.GetFiles(Path.Combine(root,folder),folder=="src"?"*.rs":"*.cs"))report.Hashes[Path.GetRelativePath(root,path).Replace('\\','/')]=Runner.Hash(path);
            foreach(var path in new[]{"Cargo.toml","Cargo.lock","global.json","rust-toolchain.toml","dotnet/Simulator.Benchmarks/Simulator.Benchmarks.csproj"})report.Hashes[path]=Runner.Hash(Path.Combine(root,path));
            reportPath=Path.Combine(output,runId+".json");
            if(File.Exists(reportPath))throw new IOException("Run identifier collision");
            // Do not traverse junctions/symlinks or .git. Every recognized deck
            // gets a row before child execution; oversize/unreadable data stays visible.
            var files=mode=="tracked"?(await Metadata("git",["ls-files","-z"],corpus)).Split('\0',StringSplitOptions.RemoveEmptyEntries).Select(p=>Path.GetFullPath(p,corpus)):Discover(corpus);
            foreach(var file in files.Order(StringComparer.Ordinal))
            {
                var relative=Path.GetRelativePath(corpus,file).Replace('\\','/');
                var known=CorpusPolicy.IsDeck(relative,"");
                if(!known&&Path.GetExtension(file).Length!=0)continue;
                if(!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                {
                    if(known)report.Rows.Add(new(relative,null,relative.Split('/')[0],[]){Status="unavailable_input",Diagnostic="Missing or linked tracked deck; not followed"});
                    else report.UnclassifiedAssets[relative]="Missing/linked extensionless asset; not followed";
                    continue;
                }
                var length=new FileInfo(file).Length;
                string text="";string? hash=null;string? error=null;
                try {hash=Runner.Hash(file);if(length>1_048_576)throw new InvalidDataException("Deck exceeds 1 MiB importer budget");var bytes=Runner.Input(file);text=new UTF8Encoding(false,true).GetString(bytes);}
                catch(Exception e)when(e is IOException or InvalidDataException or DecoderFallbackException){error=e.Message;}
                if(!known&&error is null&&!CorpusPolicy.IsDeck(relative,text))continue;
                if(!known&&error is not null){report.UnclassifiedAssets[relative]=error;continue;}
                var row=new CorpusRow(relative,hash,relative.Split('/')[0],CorpusPolicy.Features(text));
                if(error is not null){row.Status="resource_or_input_rejected";row.Diagnostic=error;}
                report.Rows.Add(row);if(report.Rows.Count>20000)throw new InvalidDataException("Corpus deck inventory exceeds 20000");
            }
            if(report.Rows.Count==0)throw new InvalidDataException("No decks discovered");
            report.Inventory=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mode+"|"+report.ComparisonPolicy+"|"+report.Hashes["ngspice"]+"|"+
                report.AbsoluteToleranceV.ToString("R",CultureInfo.InvariantCulture)+"|"+report.AbsoluteToleranceA.ToString("R",CultureInfo.InvariantCulture)+"|"+report.RelativeTolerance.ToString("R",CultureInfo.InvariantCulture)+"|"+
                string.Join('\n',report.Rows.Select(r=>r.Path+":"+(r.Sha256??("unhashed-"+runId))))))).ToLowerInvariant();
            void Save(){report.Counts=report.Rows.GroupBy(r=>r.Status).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);Atomic(reportPath,JsonSerializer.Serialize(report,CorpusJson.Default.CorpusReport));}
            Save();
            using var stop=new CancellationTokenSource();ConsoleCancelEventHandler handler=(_,e)=>{e.Cancel=true;stop.Cancel();};Console.CancelKeyPress+=handler;
            try {await CorpusRunner.Run(report,engine,ngspice,seconds,stop.Token,Save);}
            finally {Console.CancelKeyPress-=handler;}
            foreach(var pair in report.Hashes)
            {
                var path=pair.Key=="engine"?engine:pair.Key=="ngspice"?ngspice:pair.Key=="executed_helper"?helper:Path.Combine(root,pair.Key);
                if(Runner.Hash(path)!=pair.Value){report.State="provenance_changed";foreach(var row in report.Rows.Where(r=>r.Status=="compared_pass")){row.Status="provenance_changed";row.Diagnostic="Code/executable changed during corpus run";}}
            }
            history=[..history,new CorpusHistory(runId,report.Inventory,report.Rows.Count,report.Rows.Count(r=>r.Status=="compared_pass"),report.FinishedUtc!)];
            Atomic(historyPath,JsonSerializer.Serialize(history,CorpusJson.Default.CorpusHistoryArray));
            Atomic(Path.Combine(output,"burndown.svg"),CorpusPresentation.Chart(history,report.Inventory));
            Atomic(Path.Combine(output,runId+".svg"),CorpusPresentation.Chart(history,report.Inventory));
            Atomic(Path.Combine(output,runId+".md"),CorpusPresentation.Markdown(report,true));
            var body=CorpusPresentation.Markdown(report,false)+"\n## Burndown observations\n\n| Run | Total | Remaining |\n|---|---:|---:|\n"+
                string.Join('\n',history.Where(h=>h.Inventory==report.Inventory).TakeLast(30).Select(h=>$"| `{h.RunId}` | {h.Total} | {h.Total-h.Passed} |"));
            var chartRows=history.Where(h=>h.Inventory==report.Inventory).TakeLast(30).ToArray();
            body+="\n\n```mermaid\nxychart-beta\n  title \"Remaining ngspice decks\"\n  x-axis ["+
                string.Join(",",Enumerable.Range(1,chartRows.Length).Select(i=>"\"Run "+i+"\""))+
                "]\n  y-axis \"Remaining (not passing)\" 0 --> "+Math.Max(1,chartRows.Max(h=>h.Total))+
                "\n  line ["+string.Join(",",chartRows.Select(h=>h.Total-h.Passed))+"]\n```\n";
            var bodyPath=Path.Combine(raw,"issue-body.md");Atomic(bodyPath,body.Length>60000?body[..60000]+"\n\nDetails truncated; full report retained locally.":body);
            if(publish)
            {
                await Publish(report,root,bodyPath);
            }
            Save();Console.WriteLine(reportPath);if(report.IssueUrl is not null)Console.WriteLine(report.IssueUrl);
            return CorpusPolicy.ExitCode(report);
        }
        catch(Exception e)
        {
            if(report is not null&&reportPath is not null)
            {
                report.State="harness_failed";report.Failure=CorpusPolicy.Clip(e.GetType().Name+": "+e.Message);
                foreach(var row in report.Rows.Where(r=>r.Status=="pending")){row.Status="unattempted_after_harness_failure";row.Diagnostic=report.Failure;}
                report.Counts=report.Rows.GroupBy(r=>r.Status).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
                report.FinishedUtc=DateTimeOffset.UtcNow.ToString("O");
                Atomic(reportPath,JsonSerializer.Serialize(report,CorpusJson.Default.CorpusReport));
                if(report.IssueState=="pending"&&ownerRoot is not null)
                {var body=Path.Combine(report.RawDirectory,"failure-issue-body.md");Atomic(body,CorpusPresentation.Markdown(report,false));await Publish(report,ownerRoot,body);Atomic(reportPath,JsonSerializer.Serialize(report,CorpusJson.Default.CorpusReport));}
            }
            Console.Error.WriteLine(CorpusPolicy.Clip(e.GetType().Name+": "+e.Message));return 4;
        }
    }
    private static async Task Publish(CorpusReport report,string root,string bodyPath)
    {
        if(report.IssueUrl is not null)return;
        try
        {
            var remote=await ProcessRunner.Run("git",["remote","get-url","origin"],null,root,TimeSpan.FromSeconds(15));
            if(remote.ExitCode!=0)throw new IOException("Cannot identify origin for publishing");
            var repository=CorpusPolicy.Repository(remote.Output);
            var issue=await ProcessRunner.Run("gh",["issue","create","--repo",repository,"--title",$"ngspice corpus {report.RunId}: {CorpusPolicy.Remaining(report.Rows)}/{report.Rows.Count} remaining","--body-file",bodyPath],null,root,TimeSpan.FromSeconds(60));
            Atomic(Path.Combine(report.RawDirectory,"publisher.stdout"),issue.Output);Atomic(Path.Combine(report.RawDirectory,"publisher.stderr"),issue.Error);
            var url=issue.Output.Trim();
            if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith('/'+repository+"/issues/",StringComparison.Ordinal))
                throw new IOException("No verified issue URL returned; inspect publisher logs before retrying to avoid duplicates");
            report.IssueUrl=url;report.IssueState=issue.ExitCode==0?"created":"created_with_publisher_error";
            if(issue.ExitCode!=0)report.IssueError=CorpusPolicy.Clip(issue.Error);
        }
        catch(Exception e){report.IssueState="publish_failed";report.IssueError=CorpusPolicy.Clip(e.GetType().Name+": "+e.Message);}
    }
    private static IEnumerable<string> Discover(string root)
    {
        var directories=new Stack<string>();directories.Push(root);
        while(directories.TryPop(out var directory))
        {
            foreach(var path in Directory.EnumerateFileSystemEntries(directory))
            {var a=File.GetAttributes(path);if((a&FileAttributes.ReparsePoint)!=0||Path.GetFileName(path)==".git")continue;
                if((a&FileAttributes.Directory)!=0)directories.Push(path);else yield return path;}
        }
    }
    private static void Ensure(string path)
    {if(Directory.Exists(path))return;var parent=Path.GetDirectoryName(path);if(parent is null)throw new DirectoryNotFoundException(path);Ensure(parent);Directory.CreateDirectory(path);}
    private static void Atomic(string path,string text)
    {if(!Directory.Exists(Path.GetDirectoryName(path)))throw new DirectoryNotFoundException();var temporary=path+".tmp";File.WriteAllText(temporary,text,new UTF8Encoding(false));File.Move(temporary,path,true);}
}
