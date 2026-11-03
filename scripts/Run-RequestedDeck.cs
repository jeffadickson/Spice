#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;

// Thin public-API execution adapter. No oracle, domain equations or input parser.
if(args is ["--help"]){Console.WriteLine("Run-RequestedDeck <input.cir> <engine> <fresh-output> [additional-allowed-root ...]");return 0;}
if(args.Length<3){Console.Error.WriteLine("Expected input, engine, fresh output and optional additional allowed directories.");return 2;}
string input,engine,output;string[] allowed;
try{
 input=Path.GetFullPath(args[0]);engine=Path.GetFullPath(args[1]);output=Path.GetFullPath(args[2]);
 if(!File.Exists(input)||!File.Exists(engine))throw new FileNotFoundException("Input and engine must be existing files.");
 var parent=Path.GetDirectoryName(output)!;if(!Directory.Exists(parent))throw new DirectoryNotFoundException(parent);
 if(Directory.Exists(output)||File.Exists(output))throw new IOException("Output must be fresh; existing output is never overwritten.");
 allowed=new[]{Path.GetDirectoryName(input)!}.Concat(args.Skip(3).Select(Path.GetFullPath)).Distinct(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal).ToArray();
 foreach(var root in allowed)if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
 Directory.CreateDirectory(output);
 // CreateNew artifacts and a non-overwriting final rename protect existing work
 // if another process races directory creation. This is not a filesystem sandbox.
 using(File.Open(Path.Combine(output,"owner.marker"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
}catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){Console.Error.WriteLine(e.Message);return 2;}

const int wholeJobSeconds=120,pointSeconds=30,maxTotalPoints=1000;
var watch=Stopwatch.StartNew();using var job=new CancellationTokenSource(TimeSpan.FromSeconds(wholeJobSeconds));
ConsoleCancelEventHandler cancel=(_,eventArgs)=>{eventArgs.Cancel=true;job.Cancel();};Console.CancelKeyPress+=cancel;
var json=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
var status="failed";string? failure=null;string? engineHash=null;ResourceResolution? resources=null;RequestedAnalysisPlan? plan=null;
var operations=new List<OperationArchive>();var sourceChanges=new List<string>();
try{
 job.Token.ThrowIfCancellationRequested();engineHash=Hash(engine);
 resources=ResourceResolver.Resolve(input,new(allowed.ToImmutableArray()));job.Token.ThrowIfCancellationRequested();
 await Save("resources.json",new{allowed_roots=allowed,sources=resources.Sources.Select(s=>new{s.Path,s.Section,s.Sha256}),resources.Segments,resources.FragmentTerminators,resources.Diagnostics},job.Token);
 if(!resources.Success){status="refused";failure="resource_resolution_refused";}
 else{
  await SaveText("expanded.cir",resources.Text!,job.Token);
  var syntax=DeckParser.Parse(resources.Text!,collectDeclarations:true);
  plan=syntax.Declarations?.Analyses.Length>32?new RequestedAnalysisPlan([],[new("job_operation_budget",null)]):RequestedAnalysis.Prepare(syntax,maxPoints:maxTotalPoints);
  job.Token.ThrowIfCancellationRequested();
  if(!plan.Success){status="refused";failure="requested_analysis_refused";}
  else if(plan.Operations.Length>32||plan.Operations.Sum(o=>(long)o.Requests.Length)>maxTotalPoints){status="refused";failure="job_operation_or_point_budget";}
  else{
   await Save("plan.json",plan,job.Token); // Full original plan, source ranges, observations and coordinates.
   foreach(var operation in plan.Operations){
    var ordinal=operations.Count;var archive=new OperationArchive(operation.Id,operation.Kind,ordinal,operation.Requests.Length);operations.Add(archive);
    var folder="operation_"+ordinal.ToString("D4");CreateFolder(folder);
    for(var point=0;point<operation.Requests.Length;point++){
     var original=operation.Requests[point];var stem=folder+"/point_"+point.ToString("D5");
     if(job.IsCancellationRequested){archive.Points.Add(new(point,original.CaseId,"unattempted_job_cancelled",null,null,null,null,null));continue;}
     using var deadline=CancellationTokenSource.CreateLinkedTokenSource(job.Token);deadline.CancelAfter(TimeSpan.FromSeconds(pointSeconds));
     string? initialization=null;string? materialized=null;string? raw=null;string? reported=null;
     try{
      await Save(stem+".prepared.request.json",original,deadline.Token,wire:true);
      if(operation.InitializationRequest is {} bias)await Save(stem+".initialization.request.json",bias,deadline.Token,wire:true);
      // Materialize IC once, through the public API, and persist its response
      // before a later transient cancellation can discard the returned evidence.
      var execution=await RequestedAnalysis.PrepareExecutionAsync(operation,point,new SimulationClient(engine),deadline.Token);
      if(execution.InitializationResponse is {} biasResponse){initialization=stem+".initialization.response.json";await SaveText(initialization,biasResponse.Payload.GetRawText(),CancellationToken.None);}
      if(!execution.Success){archive.Points.Add(new(point,original.CaseId,"failed",execution.Failure,null,initialization,null,null));continue;}
      materialized=stem+".request.json";await Save(materialized,execution.Request!,deadline.Token,wire:true);
      var coordinates=operation.Kind=="dc"?[operation.Coordinates[point]]:operation.Coordinates;
      var view=operation with{Requests=[execution.Request!],Coordinates=coordinates,InitializationRequest=null};
      var single=plan with{Operations=[view]};
      var result=(await RequestedAnalysis.ExecuteAsync(single,new SimulationClient(engine),deadline.Token)).Single();var outcome=result.Points.Single();
      if(outcome.Response is {} response){raw=stem+".response.json";await SaveText(raw,response.Payload.GetRawText(),CancellationToken.None);}
      if(outcome.ReportedManifest is {} manifest){reported=stem+".reported.json";await Save(reported,manifest,CancellationToken.None,wire:true);}
      archive.Points.Add(new(point,original.CaseId,result.Success?"executed":"failed",outcome.TransportError??outcome.Response?.Error?.Code,materialized,initialization,raw,reported));
     }catch(OperationCanceledException){archive.Points.Add(new(point,original.CaseId,job.IsCancellationRequested?"cancelled":"timeout",job.IsCancellationRequested?"job_cancelled":"point_deadline",materialized,initialization,raw,reported));}
      catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or JsonException){archive.Points.Add(new(point,original.CaseId,"failed",e.GetType().Name+": "+e.Message,materialized,initialization,raw,reported));}
    }
   }
   status=operations.All(o=>o.Status=="executed")?"executed":job.IsCancellationRequested?"cancelled":"failed";
   if(status!="executed")failure="one_or_more_original_points_not_executed";
  }
 }
}catch(OperationCanceledException){status="cancelled";failure="job_cancelled_during_preparation_or_archival";}
 catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or JsonException){status="failed";failure=e.GetType().Name+": "+e.Message;}
finally{Console.CancelKeyPress-=cancel;}

// Cancellation or an archival failure can interrupt the outer loop. Preserve
// every known original identity without suggesting that unattempted work ran.
if(plan?.Success==true&&status!="executed")for(var ordinal=0;ordinal<plan.Operations.Length;ordinal++){
 var operation=plan.Operations[ordinal];var archive=operations.SingleOrDefault(o=>o.OriginalOperationOrdinal==ordinal);
 if(archive==null){archive=new(operation.Id,operation.Kind,ordinal,operation.Requests.Length);operations.Add(archive);}
 for(var point=0;point<operation.Requests.Length;point++)if(!archive.Points.Any(p=>p.OriginalPointOrdinal==point))archive.Points.Add(new(point,operation.Requests[point].CaseId,job.IsCancellationRequested?"unattempted_job_cancelled":"unattempted_job_failed_or_refused",failure,null,null,null,null));
}

// A completed process is not evidence that its inputs stayed immutable.
try{
 if(engineHash!=null&&Hash(engine)!=engineHash)sourceChanges.Add(engine);
 if(resources?.Success==true)foreach(var source in resources.Sources.DistinctBy(s=>s.Path))if(Hash(source.Path)!=source.Sha256)sourceChanges.Add(source.Path);
}catch(Exception e)when(e is IOException or UnauthorizedAccessException){sourceChanges.Add(e.GetType().Name+": "+e.Message);}
if(sourceChanges.Count>0){status="failed";failure="source_or_engine_changed";}
// The final archival/hash phase belongs to the job budget as well; completed
// engine points cannot override a cancellation observed before publication.
if(job.IsCancellationRequested&&status=="executed"){status="cancelled";failure="job_cancelled_before_summary_publication";}
try{
 var summary=new{schema_version="1",status,qualification="not_performed",input,engine,engine_sha256=engineHash,output,allowed_roots=allowed,elapsed_seconds=watch.Elapsed.TotalSeconds,
  limits=new{whole_job_seconds=wholeJobSeconds,point_seconds=pointSeconds,max_total_points=maxTotalPoints,max_operations=32,resource_max_files=128,resource_max_total_bytes=8_388_608,resource_max_depth=32},
  deadline_policy="Cooperative deadline checked before/after bounded synchronous preparation and observed during public-client async execution; no hard deadline for OS-blocked filesystem calls.",
  failure,source_changes=sourceChanges,resource_diagnostics=resources?.Diagnostics,preparation_diagnostics=plan?.Diagnostics,
  sources=resources?.Sources.Select(s=>new{s.Path,s.Section,s.Sha256}),operations,
  archive_policy="Original plan/prepared requests retained. IC materialized once by public API. Single-point ExecuteAsync views preserve returned payloads; aggregate failure withholds an executed label. Cancellation can leave no returned response; unattempted points are explicit. No native oracle or numerical qualification."};
 var temporary=Path.Combine(output,"summary."+Guid.NewGuid().ToString("N")+".tmp");
 await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(summary,json));File.Move(temporary,Path.Combine(output,"summary.json"));
}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){Console.Error.WriteLine("Summary publication failed: "+e.Message);return 2;}
Console.WriteLine(status+": "+Path.Combine(output,"summary.json"));return status=="executed"?0:1;

static string Hash(string path){using var file=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(file));}
void CreateFolder(string relative){var path=Path.Combine(output,relative);if(!Directory.Exists(Path.GetDirectoryName(path)))throw new DirectoryNotFoundException();Directory.CreateDirectory(path);}
async Task SaveText(string relative,string text,CancellationToken token){var path=Path.Combine(output,relative);if(!Directory.Exists(Path.GetDirectoryName(path)))throw new DirectoryNotFoundException();await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);await using var writer=new StreamWriter(stream);await writer.WriteAsync(text.AsMemory(),token);}
async Task Save<T>(string relative,T value,CancellationToken token,bool wire=false){var path=Path.Combine(output,relative);if(!Directory.Exists(Path.GetDirectoryName(path)))throw new DirectoryNotFoundException();await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);await JsonSerializer.SerializeAsync(stream,value,wire?WireFormat.Options:json,token);}
/// <summary>Original operation identity; only complete original point sets receive an executed label.</summary>
sealed record OperationArchive(string Id,string Kind,int OriginalOperationOrdinal,int RequestedPoints){public List<PointArchive> Points{get;}=[];public string Status=>Points.Count==RequestedPoints&&Points.All(p=>p.Status=="executed")?"executed":"failed";}
/// <summary>Original point ordinal/case ID with independently archived raw and reporting-window artifacts.</summary>
sealed record PointArchive(int OriginalPointOrdinal,string CaseId,string Status,string? Failure,string? MaterializedRequest,string? InitializationResponse,string? RawResponse,string? ReportedManifest);
