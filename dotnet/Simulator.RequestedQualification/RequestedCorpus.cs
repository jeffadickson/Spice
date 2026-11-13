using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
namespace Simulator.RequestedQualification;
/// <summary>Qualification-only corpus coordinator; it never dispatches unmodeled input controls or synthesizes selected analyses as requested analyses.</summary>
public static class RequestedCorpus
{
 private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
 /// <summary>Runs a frozen inventory against existing executables with per-child deadlines, between-job admission budget and a final immutable-source gate.</summary>
 public static async Task<int> RunAsync(string[] args)
 {
  if(args.Length is <6 or >8)throw new ArgumentException("Expected checkout, corpus, engine, ngspice, inventory JSON, fresh output, optional admission seconds and optional CPU backend (lu/faer).");
  var root=Path.GetFullPath(args[0]);var corpus=Path.GetFullPath(args[1]);var engine=Path.GetFullPath(args[2]);var ngspice=Path.GetFullPath(args[3]);var inventory=Path.GetFullPath(args[4]);var output=Path.GetFullPath(args[5]);
  var seconds=args.Length>=7?int.Parse(args[6],NumberStyles.None,CultureInfo.InvariantCulture):1800;if(seconds is <1 or >21600)throw new ArgumentOutOfRangeException(nameof(args));
  var backend=args.Length==8?args[7]:"lu";
  if(backend is not ("lu" or "faer"))throw new ArgumentException("Supported qualification CPU backends are lu and faer.");
  foreach(var path in new[]{root,corpus,Path.GetDirectoryName(output)!})if(!Directory.Exists(path))throw new DirectoryNotFoundException(path);
  foreach(var path in new[]{engine,ngspice,inventory})if(!File.Exists(path))throw new FileNotFoundException(path);
  var compiledRoot=Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.Parent!.FullName;
  if(!Path.GetFullPath(compiledRoot).Equals(root,PathComparison))throw new InvalidDataException("Checkout differs from the compiled qualification source path.");
  if(Directory.Exists(output)||File.Exists(output))throw new IOException("Fresh output required.");
  if(new FileInfo(inventory).Length>128*1024*1024)throw new InvalidDataException("inventory_budget");
  using var document=JsonDocument.Parse(await File.ReadAllTextAsync(inventory));var identities=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);var rows=new List<CorpusRow>();
  foreach(var item in document.RootElement.GetProperty("rows").EnumerateArray()){
   var scope=item.GetProperty("scope").GetString()??throw new InvalidDataException("missing_scope");var relative=item.GetProperty("path").GetString()??throw new InvalidDataException("missing_path");var expected=item.GetProperty("result").GetProperty("Sha256").GetString()??"";
   if(expected.Length!=64||expected.Any(c=>!char.IsAsciiHexDigit(c))||!identities.Add(scope+"/"+relative))throw new InvalidDataException("invalid_or_duplicate_inventory_identity");
   var basePath=scope=="repository"?root:scope=="ngspice"?corpus:throw new InvalidDataException("unknown_scope");var path=Path.GetFullPath(relative,basePath);
   if(!path.StartsWith(Path.TrimEndingDirectorySeparator(basePath)+Path.DirectorySeparatorChar,PathComparison))throw new InvalidDataException("inventory_path_escape");
   var validation=item.TryGetProperty("validation",out var contract)?DeckValidationContract.Read(contract):null;
   rows.Add(new(){Validation=validation,Scope=scope,Path=relative,InputPath=path,ExpectedSha256=expected.ToLowerInvariant(),Id=scope+"/"+relative+"@"+expected.ToLowerInvariant()});
  }
  if(rows.Count==0)throw new InvalidDataException("empty_inventory");
  Directory.CreateDirectory(output);var snapshot=SourcePaths(root).Concat(new[]{inventory,engine,ngspice,typeof(RequestedCorpus).Assembly.Location,typeof(RequestedAnalysis).Assembly.Location,typeof(DeckParser).Assembly.Location,typeof(SimulationClient).Assembly.Location}).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p=>p,Hash,StringComparer.OrdinalIgnoreCase);
  var report=new CorpusReport{State="running",StartedUtc=DateTimeOffset.UtcNow,InventorySha256=Hash(inventory),SourceHashes=snapshot,Rows=rows,AdmissionSeconds=seconds,Backend=backend};
  report.SourceCommit=(await Child("git",["-C",root,"rev-parse","HEAD"],output,null,"source-git")).Output.Trim();
  report.CorpusCommit=(await Child("git",["-C",corpus,"rev-parse","HEAD"],output,null,"corpus-git")).Output.Trim();
  var version=await Child(ngspice,["-n","--version"],output,null,"native-version");report.NativeVersion=version.Output+version.Error;
  var exclusions=Exclusions(root);var watch=Stopwatch.StartNew();report.ProgressClock=watch;var client=new SimulationClient(engine);var limits=new ParserLimits(4_194_304,2_000_000);WriteProgress(report,output);
  for(var index=0;index<rows.Count;index++){
   var row=rows[index];if(watch.Elapsed.TotalSeconds>=seconds){row.Status="unattempted_budget";continue;}
   var folder=Path.Combine(output,"cases",index.ToString("D4",CultureInfo.InvariantCulture));Directory.CreateDirectory(folder);
   try{
    row.InputSha256=Hash(row.InputPath);if(!row.InputSha256.Equals(row.ExpectedSha256,StringComparison.OrdinalIgnoreCase)){row.Status="input_changed";continue;}
    if(new FileInfo(row.InputPath).Length>8*1024*1024){row.Status="input_resource_budget";continue;}
    var text=await File.ReadAllTextAsync(row.InputPath,new UTF8Encoding(false,true));var original=DeckParser.Parse(text,limits,collectDeclarations:true);row.SyntaxAccepted=original.Status=="accepted_structure";row.SyntaxStatus=original.Status;
    if(exclusions.TryGetValue(row.Scope+"/"+row.Path,out var excluded)&&excluded.Equals(row.InputSha256,StringComparison.OrdinalIgnoreCase)){row.Status="excluded_hash_pinned";continue;}
    var resources=ResourceResolver.Resolve(row.InputPath,new([corpus,root]){ParsingLimits=limits});row.ResourceStatus=resources.Success?"resolved":"refused";row.Sources=resources.Sources.Select(s=>new LoadedSource(s.Path,s.Section,s.Sha256)).ToArray();
    if(!resources.Success){row.Status="resource_refused";row.Diagnostics=resources.Diagnostics.Select(d=>d.Code).ToArray();continue;}
    if(!row.Sources.Any(s=>s.Section==null&&s.Path.Equals(row.InputPath,PathComparison)&&s.Sha256.Equals(row.InputSha256,StringComparison.OrdinalIgnoreCase))){row.Status="input_changed_during_resolution";continue;}
    var expanded=resources.Text!;await File.WriteAllTextAsync(Path.Combine(folder,"expanded.cir"),expanded);row.ExpandedSha256=Hash(Path.Combine(folder,"expanded.cir"));
    var syntax=DeckParser.Parse(expanded,limits,collectDeclarations:true);row.ExpandedSyntaxStatus=syntax.Status;
    if(syntax.Status!="accepted_structure"){row.Status="expanded_syntax_refused";continue;}
    var pzCard=syntax.Declarations?.Cards.FirstOrDefault(c=>c.Name.Equals(".pz",StringComparison.OrdinalIgnoreCase));
    var pz=pzCard is null?null:PoleZeroSelection.Analyze(syntax);
    if(pz is { Success:false }){
     row.Status="requested_preparation_refused";row.RequestedPrepared=false;row.RequestedExecuted=false;row.Diagnostics=[pz.Diagnostic!.Code];continue;
    }
    var ordinaryText=expanded;
    var ordinarySyntax=syntax;
    ProjectionOmission? omitPz=null;
    if(pzCard is not null){
     omitPz=new("pz","other_requested_analysis_not_in_this_oracle_job",pzCard.Range);
     ordinaryText=PoleZeroQualification.Mask(expanded,[omitPz]);
     ordinarySyntax=DeckParser.Parse(ordinaryText,limits,collectDeclarations:true);
    }
    var pzOnly=pz is not null&&ordinarySyntax.Declarations?.Analyses.IsEmpty==true;
    var plan=pzOnly?new RequestedAnalysisPlan([],[]):RequestedAnalysis.Prepare(ordinarySyntax,backend:backend);
    var circuit=plan.CircuitPreparation??pz?.CircuitPreparation??OperatingPointSelection.Select(ordinarySyntax).Prepared;row.CircuitPrepared=circuit.Success;
    if(!circuit.Success)row.Diagnostics=circuit.Diagnostics.Select(d=>d.Code).ToArray();
    row.RequestedPrepared=plan.Success||pzOnly;row.ControlProfile=plan.ControlProfile;row.ProgramOmissions=plan.ProgramOmissions.ToArray();
    if(!plan.Success&&!pzOnly){row.Status="requested_preparation_refused";row.RequestedExecuted=false;row.Diagnostics=row.Diagnostics.Concat(plan.Diagnostics.Select(d=>d.Code)).Distinct().ToArray();continue;}
    if(!circuit.Success){row.Status="circuit_preparation_inconsistent";row.RequestedExecuted=false;continue;}
    row.Status="prepared";
    var controlResults=new List<RequestedOperationResult>();
    foreach(var operation in plan.Operations){
     var result=new OperationRow{Id=row.Id+"/"+operation.Id,Ordinal=row.Operations.Count,Kind=operation.Kind,Range=operation.Range,CoordinateUnit=operation.CoordinateUnit,Arguments=operation.Arguments.ToArray(),RequestedCoordinates=operation.Coordinates.ToArray(),NestedDc=operation.NestedDc,OutputStartS=operation.OutputStartS,UsesUic=operation.UsesUic,InitialConditions=operation.InitialConditions.ToArray(),ControlOrigin=operation.ControlOrigin};row.Operations.Add(result);
     var operationFolder=Path.Combine(folder,operation.Id);Directory.CreateDirectory(operationFolder);var responses=new List<SimulationResponse>();var allExecuted=true;var failed=false;
     PoleZeroResult? controlRoots=null;
     if(operation.PoleZero is {} requestedRoots){
      controlRoots=requestedRoots.Execute();failed=!controlRoots.Success;
      result.Points.Add(new(0,failed?"analysis_failed":"completed",controlRoots.Diagnostic?.Code));
      await File.WriteAllTextAsync(Path.Combine(operationFolder,"roots.json"),JsonSerializer.Serialize(controlRoots,Json));
     }
     for(var point=0;point<operation.Requests.Length;point++){
      if(watch.Elapsed.TotalSeconds>=seconds){result.Points.Add(new(point,"unattempted_budget",null));allExecuted=false;continue;}
      var request=operation.Requests[point];var requestPath=Path.Combine(operationFolder,point.ToString("D5",CultureInfo.InvariantCulture)+".request.json");await File.WriteAllTextAsync(requestPath,JsonSerializer.Serialize(request,WireFormat.Options));
      using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
      try{
       if(operation.InitializationRequest is {} initialization)await File.WriteAllTextAsync(Path.Combine(operationFolder,"initialization.request.json"),JsonSerializer.Serialize(initialization,WireFormat.Options));
       var execution=await RequestedAnalysis.PrepareExecutionAsync(operation,point,client,deadline.Token);
       if(execution.InitializationResponse is {} initialResponse)await File.WriteAllTextAsync(Path.Combine(operationFolder,"initialization.response.json"),initialResponse.Payload.GetRawText());
       if(!execution.Success){result.Points.Add(new(point,"initialization_failed",execution.Failure));failed=true;continue;}
       if(watch.Elapsed.TotalSeconds>=seconds){result.Points.Add(new(point,"unattempted_budget_after_initialization",null));allExecuted=false;continue;}
       request=execution.Request!;await File.WriteAllTextAsync(requestPath,JsonSerializer.Serialize(request,WireFormat.Options));
       var response=await client.RunAsync(request,deadline.Token);await File.WriteAllTextAsync(Path.Combine(operationFolder,point.ToString("D5",CultureInfo.InvariantCulture)+".response.json"),response.Payload.GetRawText());
       result.Points.Add(new(point,response.Succeeded?"completed":"engine_failed",response.Error?.Code));responses.Add(response);failed|=!response.Succeeded;
      }catch(Exception e)when(e is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or JsonException){result.Points.Add(new(point,e is OperationCanceledException?"timeout":"transport_failed",e.GetType().Name));failed=true;}
     }
     result.Executed=failed?false:allExecuted?true:null;
     if(result.Executed!=true){result.Status=failed?"engine_operation_failed_atomically":"unattempted_budget";continue;}
     controlResults.Add(new(operation.Id,true,responses.Select((response,i)=>new RequestedPointResult(i,response,null)).ToImmutableArray()){PoleZero=controlRoots});
     if(operation==plan.Operations[^1]&&row.ProgramOmissions.Any(o=>o.Reason is "conditional_reporting_not_executed" or "prefix_reporting_executed_separately" or "redirect_captured_separately" or "source_parameter_alter_executed_separately")){
      try{
       var reporting=RequestedAnalysis.ExecuteTerminalReporting(syntax,plan,responses);
       await File.WriteAllTextAsync(Path.Combine(folder,"control-report.json"),JsonSerializer.Serialize(reporting,Json));
       row.ControlReportingStatus=reporting.Diagnostics.IsEmpty&&reporting.ExitCode is null or 0?"executed":"failed_assertions";
      }catch(InvalidDataException e){row.ControlReportingStatus=e.Message;}
     }
     if(row.Validation?.Mode=="execution_only"){result.Status="native_not_requested_by_contract";continue;}
     if(watch.Elapsed.TotalSeconds>=seconds){result.Status="native_unattempted_budget";continue;}
     var projection=OriginalProjection.Create(ordinaryText,ordinarySyntax,operation,plan);result.Omissions=omitPz is null?projection.Omissions.ToArray():projection.Omissions.Add(omitPz).OrderBy(x=>x.Range.Start).ToArray();result.NativeInstrumentation=projection.Instrumentation.ToArray();var nativePath=Path.Combine(operationFolder,"original-analysis.cir");await File.WriteAllTextAsync(nativePath,projection.Text);result.NativeProjectionSha256=Hash(nativePath);
     var rawPath=Path.Combine(operationFolder,"native.raw");var nativeArgs=new List<string>{"-n","-D","filetype=ascii"};
     if(circuit.GroundAliases==GroundAliasPolicy.Pspice||circuit.Request!.Circuit.Elements.Any(e=>e is PspiceSwitch)){nativeArgs.AddRange(["-D","ngbehavior=psa"]);result.NativeDialect="psa";}
     if(circuit.GroundAliases==GroundAliasPolicy.Disabled){nativeArgs.AddRange(["-D","no_auto_gnd"]);result.NativeDialect+=";no_auto_gnd";}
     nativeArgs.Add("-b");if(!projection.RequiresControlWrite)nativeArgs.AddRange(["-r",rawPath]);nativeArgs.AddRange(["-o",Path.Combine(operationFolder,"native.log"),nativePath]);
     try{
      var native=await Child(ngspice,nativeArgs.ToArray(),operationFolder,null,"native");result.NativeExit=native.Exit;
      if(native.Exit!=0){result.Status="native_failed";continue;}
      if(!File.Exists(rawPath)||new FileInfo(rawPath).Length>64*1024*1024){result.Status="native_raw_missing_or_oversized";continue;}
      result.NativeRawSha256=Hash(rawPath);var raw=AsciiRaw.Read(await File.ReadAllTextAsync(rawPath));result.NativePoints=raw.Points.Length;
      result.Comparison=controlRoots==null?RequestedComparison.Compare(operation,circuit,responses,raw,row.ExpandedSha256):PoleZeroQualification.Compare(controlRoots,raw);result.Agreed=result.Comparison.Agreed;result.Status=result.Comparison.Status;
     }catch(Exception e)when(e is IOException or InvalidDataException or TimeoutException){result.Status=e is TimeoutException?"native_timeout":"native_result_refused";result.Detail=e.Message;}
    }
    if(plan.ControlProfile=="ordered_direct_commands"){
     try{
      var reporting=RequestedAnalysis.ExecuteOrderedReporting(syntax,plan,controlResults);
      await File.WriteAllTextAsync(Path.Combine(folder,"control-report.json"),JsonSerializer.Serialize(reporting,Json));
      row.ControlReportingStatus=reporting.Diagnostics.IsEmpty&&reporting.ExitCode is null or 0?"executed":"failed_assertions";
     }catch(InvalidDataException error){row.ControlReportingStatus=error.Message;}
    }
    if(pz is not null&&pzCard is not null){
     var result=new OperationRow{Id=row.Id+"/requested_pz",Kind="pz",Range=pzCard.Range,CoordinateUnit="1/s"};
     row.Operations.Add(result);
     var pzFolder=Path.Combine(folder,"requested_pz");Directory.CreateDirectory(pzFolder);
     await File.WriteAllTextAsync(Path.Combine(pzFolder,"roots.json"),JsonSerializer.Serialize(pz,Json));
     result.Executed=true;result.Points.Add(new(0,"completed",null));
     if(row.Validation?.Mode=="execution_only")result.Status="native_not_requested_by_contract";
     else if(watch.Elapsed.TotalSeconds>=seconds)result.Status="native_unattempted_budget";
     else {
      result.Omissions=OperatingPointSelection.Select(syntax).Omissions
       .Where(x=>x.Range!=pzCard.Range&&(x.Kind=="op"||x.Reason=="observation_not_selected"))
       .Select(x=>new ProjectionOmission(x.Kind,"not_in_this_pz_oracle_job",x.Range)).ToArray();
      var path=Path.Combine(pzFolder,"original-analysis.cir");
      await File.WriteAllTextAsync(path,PoleZeroQualification.Mask(expanded,result.Omissions));
      result.NativeProjectionSha256=Hash(path);
      var rawPath=Path.Combine(pzFolder,"native.raw");
      try{
       var native=await Child(ngspice,["-n","-D","filetype=ascii","-b","-r",rawPath,"-o",Path.Combine(pzFolder,"native.log"),path],pzFolder,null,"native");
       result.NativeExit=native.Exit;
       if(native.Exit!=0)result.Status="native_failed";
       else if(!File.Exists(rawPath)||new FileInfo(rawPath).Length>65536)result.Status="native_raw_missing_or_oversized";
       else {
        result.NativeRawSha256=Hash(rawPath);
        var raw=AsciiRaw.Read(await File.ReadAllTextAsync(rawPath));result.NativePoints=raw.Points.Length;
        result.Comparison=PoleZeroQualification.Compare(pz,raw);
        result.Agreed=result.Comparison.Agreed;result.Status=result.Comparison.Status;
       }
      }catch(Exception e)when(e is IOException or InvalidDataException or TimeoutException){result.Status=e is TimeoutException?"native_timeout":"native_result_refused";result.Detail=e.Message;}
     }
     row.Operations.Sort((a,b)=>a.Range!.Start.CompareTo(b.Range!.Start));
     for(var i=0;i<row.Operations.Count;i++)row.Operations[i].Ordinal=i;
    }
    row.RequestedExecuted=row.Operations.Any(o=>o.Executed==false)?false:row.Operations.All(o=>o.Executed==true)?true:null;
    row.NativeAgreed=row.Operations.Any(o=>o.Agreed==false)?false:row.Operations.All(o=>o.Agreed==true)?true:null;
    row.Status=row.NativeAgreed==true?"all_individual_requested_analyses_agreed":row.RequestedExecuted==true?"requested_executed_not_fully_qualified":row.RequestedExecuted==false?"requested_execution_failed":"partially_unattempted_budget";
    if((plan.ControlProfile=="ordered_direct_commands"||row.ControlReportingStatus!=null)&&row.ControlReportingStatus!="executed"){
     row.Status="requested_control_execution_failed";row.RequestedExecuted=false;row.NativeAgreed=false;
    }
   }catch(Exception e)when(e is IOException or InvalidDataException or DecoderFallbackException or UnauthorizedAccessException or JsonException or ArgumentException){row.Status="input_or_qualification_failure";row.Diagnostics=[e.GetType().Name+":"+e.Message];}
   finally{WriteProgress(report,output);}
  }
  var changed=snapshot.Where(p=>!Matches(p.Key,p.Value)).Select(p=>p.Key).ToList();
  foreach(var row in rows){
   if(row.InputSha256!=null&&(!Matches(row.InputPath,row.InputSha256)))changed.Add(row.InputPath);
   foreach(var source in row.Sources)if(!Matches(source.Path,source.Sha256))changed.Add(source.Path);
  }
  report.ChangedPaths=changed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();report.ProvenanceValid=changed.Count==0;report.ElapsedSeconds=watch.Elapsed.TotalSeconds;report.State=changed.Count>0?"provenance_changed":rows.Any(r=>r.Status.Contains("budget",StringComparison.Ordinal)||r.Operations.Any(o=>o.Status.Contains("budget",StringComparison.Ordinal)))?"budget_exhausted":"completed_with_results";WriteProgress(report,output);
  return report.ProvenanceValid==false?3:report.State=="budget_exhausted"?2:rows.Any(r=>r.Validation!=null&&r.ValidationSatisfied!=true)?4:0;
 }
 private static StringComparison PathComparison=>OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
 private static string SourcePath([CallerFilePath]string value="")=>value;
 private static IEnumerable<string> SourcePaths(string root)=>new[]{"Simulator.Syntax","Simulator.Client","Simulator.Frontend","Simulator.RequestedQualification"}.SelectMany(p=>Directory.GetFiles(Path.Combine(root,"dotnet",p)).Where(f=>Path.GetExtension(f) is ".cs" or ".csproj" or ".json")).Concat(Directory.GetFiles(Path.Combine(root,"dotnet/Simulator.Syntax/Grammar"),"*.g4")).Concat(Directory.GetFiles(Path.Combine(root,"src"),"*.rs",SearchOption.AllDirectories)).Concat(new[]{"Cargo.toml","Cargo.lock","build.rs","rust-toolchain.toml"}.Select(p=>Path.Combine(root,p)).Where(File.Exists)).Concat(new[]{Path.Combine(root,"scripts/Run-RequestedCorpus.cs"),Path.Combine(root,"global.json")});
 private static Dictionary<string,string> Exclusions(string root){using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"compat/ngspice/parser-input-adjudications.json")));return document.RootElement.GetProperty("entries").EnumerateArray().Where(e=>e.GetProperty("disposition").GetString() is "non_deck_document" or "invalid_syntax_fixture").ToDictionary(e=>e.GetProperty("scope").GetString()+"/"+e.GetProperty("path").GetString(),e=>e.GetProperty("input_sha256").GetString()!);}
 private static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
 // A disappearance or lock race at the end gate is changed provenance, never a successful report.
 private static bool Matches(string path,string expected){try{return File.Exists(path)&&Hash(path).Equals(expected,StringComparison.OrdinalIgnoreCase);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){return false;}}
 private static void Atomic(string path,string text){var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,text);File.Move(temporary,path,true);}
 private static void WriteProgress(CorpusReport report,string output){
  report.UpdatedUtc=DateTimeOffset.UtcNow;report.ElapsedSeconds=Math.Max(report.ElapsedSeconds,report.ProgressClock?.Elapsed.TotalSeconds??report.ElapsedSeconds);var valid=report.ProvenanceValid!=false;
  report.Stages=[StageBurndown.Count("Syntax accepted",report.Rows.Select(r=>r.SyntaxAccepted),valid),StageBurndown.Count("Circuit prepared",report.Rows.Select(r=>r.CircuitPrepared),valid),StageBurndown.Count("Requested analyses executed",report.Rows.Select(r=>r.RequestedExecuted),valid),StageBurndown.Count("Native numerical agreement",report.Rows.Select(r=>r.NativeAgreed),report.ProvenanceValid==true)];
  Atomic(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(report,Json));Atomic(Path.Combine(output,"burndown.svg"),StageBurndown.Svg(report.Stages));
 }
 private static async Task<ChildResult> Child(string executable,string[] arguments,string directory,string? input,string prefix){
  var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=input!=null};foreach(var argument in arguments)start.ArgumentList.Add(argument);start.Environment["LC_ALL"]="C";
  using var child=Process.Start(start)??throw new IOException("child_start_failed");var stdout=Bounded(child.StandardOutput);var stderr=Bounded(child.StandardError);using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
  try{if(input!=null){await child.StandardInput.WriteAsync(input.AsMemory(),deadline.Token);child.StandardInput.Close();}var exit=child.WaitForExitAsync(deadline.Token);var pending=new List<Task>{stdout,stderr,exit};while(pending.Count>0){var done=await Task.WhenAny(pending);await done.WaitAsync(deadline.Token);pending.Remove(done);}}
  catch(Exception e){if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();try{await Task.WhenAll(stdout,stderr);}catch(IOException){}if(e is OperationCanceledException)throw new TimeoutException("child_deadline",e);throw;}
  var output=await stdout;var error=await stderr;await File.WriteAllTextAsync(Path.Combine(directory,prefix+".stdout"),output);await File.WriteAllTextAsync(Path.Combine(directory,prefix+".stderr"),error);return new(child.ExitCode,output,error);
 }
 private static async Task<string> Bounded(StreamReader reader){var result=new StringBuilder();var buffer=new char[4096];int count;while((count=await reader.ReadAsync(buffer))>0){if(result.Length>8_388_608-count)throw new IOException("child_output_budget");result.Append(buffer,0,count);}return result.ToString();}
 private sealed record ChildResult(int Exit,string Output,string Error);
 private sealed record LoadedSource(string Path,string? Section,string Sha256);
 private sealed record PointRow(int Index,string Status,string? Detail);
 private sealed class OperationRow{
  public string Id{get;set;}="";public int Ordinal{get;set;}public string Kind{get;set;}="";public SourceRange? Range{get;set;}public string? CoordinateUnit{get;set;}public double[] Arguments{get;set;}=[];public double[] RequestedCoordinates{get;set;}=[];public NestedDcSweep? NestedDc{get;set;}public double OutputStartS{get;set;}public bool UsesUic{get;set;}public PreparedNodeInitialCondition[] InitialConditions{get;set;}=[];public RequestedControlOrigin? ControlOrigin{get;set;}public string[] NativeInstrumentation{get;set;}=[];public bool? Executed{get;set;}public bool? Agreed{get;set;}public string Status{get;set;}="pending";public string? Detail{get;set;}public List<PointRow> Points{get;set;}=[];public ProjectionOmission[] Omissions{get;set;}=[];public string? NativeProjectionSha256{get;set;}public string? NativeRawSha256{get;set;}public int? NativeExit{get;set;}public int? NativePoints{get;set;}public string NativeDialect{get;set;}="default";public ComparisonResult? Comparison{get;set;}
 }
 private sealed class CorpusRow{
  public DeckValidationContract? Validation{get;init;}
  public bool? ValidationSatisfied=>Validation?.Evaluate(RequestedExecuted,NativeAgreed,Status,Diagnostics);
  public string Id{get;set;}="";public string Scope{get;set;}="";public string Path{get;set;}="";public string InputPath{get;set;}="";public string ExpectedSha256{get;set;}="";public string? InputSha256{get;set;}public string Status{get;set;}="unattempted";public bool? SyntaxAccepted{get;set;}public string? SyntaxStatus{get;set;}public string? ExpandedSyntaxStatus{get;set;}public string? ResourceStatus{get;set;}public bool? CircuitPrepared{get;set;}public string? ControlProfile{get;set;}public string? ControlReportingStatus{get;set;}public OperatingPointOmission[] ProgramOmissions{get;set;}=[];public bool? RequestedPrepared{get;set;}public bool? RequestedExecuted{get;set;}public bool? NativeAgreed{get;set;}public string[] Diagnostics{get;set;}=[];public LoadedSource[] Sources{get;set;}=[];public string? ExpandedSha256{get;set;}public List<OperationRow> Operations{get;set;}=[];
 }
 private sealed class CorpusReport{
  // Same monotonic admission clock as final duration; internal state is not serialized.
  internal Stopwatch? ProgressClock{get;set;}
  public string Backend{get;set;}="lu";public string State{get;set;}="";public DateTimeOffset StartedUtc{get;set;}public DateTimeOffset UpdatedUtc{get;set;}public string SourceCommit{get;set;}="";public string CorpusCommit{get;set;}="";public string InventorySha256{get;set;}="";public string NativeVersion{get;set;}="";public Dictionary<string,string> SourceHashes{get;set;}=[];public bool? ProvenanceValid{get;set;}public string[] ChangedPaths{get;set;}=[];public double ElapsedSeconds{get;set;}public int AdmissionSeconds{get;set;}public StageCount[] Stages{get;set;}=[];public List<CorpusRow> Rows{get;set;}=[];public string Scope{get;set;}="Individual ordinary requested analyses; not input-control-program parity. No hardcoded selected OP counts as requested execution. Full mapped root voltage and supported branch-current grids; declared transient oracle interpolation.";
 }
}
