#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Simulator.Syntax;
using Simulator.Frontend;
using Simulator.Client;

// Execution is an explicit qualification operation, separate from syntax-only
// collection. Input control programs are never dispatched. The oracle consumes
// expanded original source, not values copied back from the lowered request.
if(args.Length is not (6 or 7 or 8))throw new ArgumentException("Expected checkout, corpus, Rust executable, ngspice executable, syntax inventory JSON, fresh output, optional profile and admission-budget seconds");
var selected=args.Length>=7&&(args[6] is "selected-linear-op" or "selected-static-op");
if(args.Length>=7&&!selected&&args[6]!="strict-rvi-op")throw new ArgumentException("Unknown analysis selection");
var admissionSeconds=1800;
if(args.Length==8&&(!int.TryParse(args[7],NumberStyles.Integer,CultureInfo.InvariantCulture,out admissionSeconds)||admissionSeconds is <1 or >21600))
 throw new ArgumentOutOfRangeException(nameof(admissionSeconds),"Admission budget seconds must be 1..21600");
var root=Path.GetFullPath(args[0]);var corpus=Path.GetFullPath(args[1]);var engine=Path.GetFullPath(args[2]);var ngspice=Path.GetFullPath(args[3]);var inventory=Path.GetFullPath(args[4]);var output=Path.GetFullPath(args[5]);
foreach(var path in new[]{root,corpus,Path.GetDirectoryName(output)!})if(!Directory.Exists(path))throw new DirectoryNotFoundException(path);
foreach(var path in new[]{engine,ngspice,inventory})if(!File.Exists(path))throw new FileNotFoundException(path);
if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
if(new FileInfo(inventory).Length>128*1024*1024)throw new InvalidDataException("Inventory budget exceeded");
var json=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
var sourcePaths=new[]{"Simulator.Syntax","Simulator.Client","Simulator.Frontend"}.SelectMany(p=>Directory.GetFiles(Path.Combine(root,"dotnet",p)).Where(f=>f.EndsWith(".cs")||f.EndsWith(".csproj")||f.EndsWith(".json")))
 .Concat(Directory.GetFiles(Path.Combine(root,"dotnet/Simulator.Syntax/Grammar"),"*.g4")).Concat(new[]{Path.Combine(root,"scripts/Run-FrontendCorpus.cs"),Path.Combine(root,"global.json")});
var hashes=sourcePaths.ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash);
using var input=JsonDocument.Parse(File.ReadAllText(inventory));
using var adjudications=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"compat/ngspice/parser-input-adjudications.json")));
var excluded=adjudications.RootElement.GetProperty("entries").EnumerateArray().Where(e=>e.GetProperty("disposition").GetString() is "non_deck_document" or "invalid_syntax_fixture")
 .ToDictionary(e=>e.GetProperty("scope").GetString()+"/"+e.GetProperty("path").GetString(),e=>e.GetProperty("input_sha256").GetString()!);
var totalInputs=input.RootElement.GetProperty("rows").GetArrayLength();
var rows=new List<Row>();var client=new SimulationClient(engine);var startedUtc=DateTimeOffset.UtcNow;var watch=Stopwatch.StartNew();var stopped=false;
var limits=new ParserLimits(4_194_304,2_000_000);
var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
foreach(var item in input.RootElement.GetProperty("rows").EnumerateArray())
{
 var scope=item.GetProperty("scope").GetString()!;var relative=item.GetProperty("path").GetString()!;
 var row=new Row{Scope=scope,Path=relative,Status="pending"};rows.Add(row);
 if(stopped||watch.Elapsed>TimeSpan.FromSeconds(admissionSeconds)){stopped=true;row.Status="unattempted_budget";continue;}
 var basePath=scope=="repository"?root:scope=="ngspice"?corpus:throw new InvalidDataException("Unknown inventory scope");
 var path=Path.GetFullPath(relative,basePath);
 if(!path.StartsWith(Path.TrimEndingDirectorySeparator(basePath)+Path.DirectorySeparatorChar,comparison))throw new InvalidDataException("Inventory path escapes selected root");
 try
 {
  row.InputSha256=Hash(path);
  var expected=item.GetProperty("result").GetProperty("Sha256").GetString();
  if(!row.InputSha256.Equals(expected,StringComparison.OrdinalIgnoreCase)){row.Status="input_changed";continue;}
  if(excluded.TryGetValue(scope+"/"+relative,out var excludedHash)&&row.InputSha256.Equals(excludedHash,StringComparison.OrdinalIgnoreCase)){row.Status="excluded_hash_pinned";continue;}
  var resolved=ResourceResolver.Resolve(path,new([corpus,root]){ParsingLimits=limits});
  if(!resolved.Success){row.Status="resource_refused";row.Detail=string.Join(',',resolved.Diagnostics.Select(d=>d.Code));continue;}
  var loadedRoot=resolved.Sources.FirstOrDefault(s=>s.Section==null&&s.Path.Equals(path,comparison));
  if(loadedRoot==null||!loadedRoot.Sha256.Equals(row.InputSha256,StringComparison.OrdinalIgnoreCase))
  {row.Status="input_changed_during_resolution";row.Detail="root_graph_hash_differs_from_inventory_read";continue;}
  row.Sources=resolved.Sources.Select(s=>new SourceHash(s.Path,s.Section,s.Sha256)).ToArray();
  var parsed=resolved.Parse(limits,collectDeclarations:true);
  if(parsed.Status!="accepted_structure"){row.Status="syntax_refused";row.Detail=parsed.Status;continue;}
  var selection=selected?OperatingPointSelection.Select(parsed):null;
  var prepared=selection?.Prepared??OperatingPointBridge.Lower(parsed);
  if(selected){row.Readiness=CircuitReadiness.Audit(parsed);row.SourceWaveforms=prepared.SourceWaveforms.ToArray();}
  row.Omissions=selection?.Omissions.ToArray()??[];
  if(!prepared.Success)
  {
   row.Status="lowering_refused";var d=prepared.Diagnostics[0];row.Detail=d.Code;
   if(d.Range is {} range){row.Card=parsed.Declarations!.Cards.FirstOrDefault(c=>c.Range.Start==range.Start)?.Kind;row.Family=parsed.Declarations.Primitives.FirstOrDefault(p=>p.Range.Start==range.Start)?.Family;row.Excerpt=resolved.Text!.Substring(range.Start,Math.Min(range.Length,160));}
   continue;
  }
  var directory=Path.Combine(output,"cases",(rows.Count-1).ToString("D4",CultureInfo.InvariantCulture));Directory.CreateDirectory(directory);
  var expanded=Path.Combine(directory,"expanded.cir");File.WriteAllText(expanded,resolved.Text);row.ExpandedSha256=Hash(expanded);
  foreach(var provider in new[]{"lu","faer"})
  {
   var request=prepared.Request! with {Backend=provider};var requestText=JsonSerializer.Serialize(request,json);
   var requestPath=Path.Combine(directory,provider+".request.json");File.WriteAllText(requestPath,requestText);
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));var timer=Stopwatch.StartNew();
   try
   {
    var response=await client.RunJsonAsync(requestText,deadline.Token);File.WriteAllText(Path.Combine(directory,provider+".response.json"),response.Payload.GetRawText());
    var result=new Provider{Backend=provider,Status=response.Succeeded?"solver_completed":"solver_failed",ElapsedMs=timer.Elapsed.TotalMilliseconds,RequestSha256=Hash(requestPath),Error=response.Error?.Code};
    if(response.Succeeded)
    {
     var manifest=response.ReadManifest();
     var expectedProvider=provider=="lu"?"nalgebra_dense_lu_reference":"faer_sparse_lu";
     if(manifest.CaseId!=request.CaseId||manifest.Backend.Id!=expectedProvider||manifest.Backend.Placement!="cpu"||manifest.Backend.Gpu)
         throw new InvalidDataException("Response case/provider identity differs from the request");
     if(manifest.Samples.Length==0||manifest.Samples.Any(s=>!double.IsFinite(s.TimeS)||s.Values.Any(v=>!double.IsFinite(v)))||
        manifest.Signals.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=manifest.Signals.Length)
         throw new InvalidDataException("Missing/non-finite samples or duplicate signal identity");
     foreach(var node in prepared.RootNodes)
     {
      var id="v("+node.Value+")";var descriptors=manifest.Signals.Where(s=>s.Id==id).ToArray();
      if(descriptors.Length!=1||descriptors[0].Unit!="V"||descriptors[0].Quantity!="voltage")throw new InvalidDataException("Missing/ambiguous/non-voltage descriptor");
      var value=response.FinalValue(id);if(!double.IsFinite(value))throw new InvalidDataException("Non-finite root voltage");
      result.Values[node.Key]=value;
     }
    }
    row.Providers.Add(result);
   }
   catch(Exception e)when(e is IOException or InvalidDataException or OperationCanceledException or InvalidOperationException or KeyNotFoundException or JsonException){row.Providers.Add(new(){Backend=provider,Status="transport_or_result_failure",Error=e.GetType().Name+":"+e.Message[..Math.Min(512,e.Message.Length)],ElapsedMs=timer.Elapsed.TotalMilliseconds});}
  }
  if(row.Providers.Any(p=>p.Status!="solver_completed")){row.Status="engine_failure";continue;}
  if(prepared.RootNodes.Count==0){row.Status="solver_completed_no_root_observables";row.Detail="no_exposed_root_voltage_observables";continue;}
  // Avoid executing even unused input controls. Native query names have a
  // deliberately narrow printable alphabet; unknown spellings are unavailable.
  if((!selected&&parsed.Declarations!.Cards.Any(c=>c.Kind=="control"))||prepared.RootNodes.Keys.Any(n=>!Regex.IsMatch(n,@"\A[a-zA-Z0-9_.:+/\[\]-]+\z"))){row.Status="solver_completed_oracle_unavailable";row.Detail="input_controls_or_query_spelling";continue;}
  var names=prepared.RootNodes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
  var control="\n.control\nset numdgt=17\nop\n"+string.Concat(names.Select((name,i)=>$"let frontend_probe_{i} = v(\"{name}\")\nprint frontend_probe_{i}\n"))+"quit\n.endc\n";
  var end=parsed.Declarations!.Cards.FirstOrDefault(c=>c.ScopeId==0&&c.Kind=="endCard");
  // Explicit AST-located projection only: keep circuit values and physical
  // offsets intact while blanking requested nonexecuted instructions. This is
  // oracle preparation, never an alternative lexer/parser or silent lowering.
  var characters=resolved.Text!.ToCharArray();var previousEnd=0;
  // Source AC metadata records which excitation was not selected. Preserve the
  // original source card for the native OP oracle; only input instructions are masked.
  foreach(var omission in row.Omissions.Where(o=>o.Kind is not ("source_ac" or "source_transient")).OrderBy(o=>o.Range.Start))
  {
   var range=omission.Range;
   if(range.Start<previousEnd||range.Start<0||range.Length<0||range.Start>characters.Length-range.Length)throw new InvalidDataException("Invalid/overlapping omission range");
   previousEnd=range.Start+range.Length;
   for(var i=range.Start;i<previousEnd;i++)if(characters[i] is not ('\r' or '\n'))characters[i]=' ';
  }
  var projected=new string(characters);File.WriteAllText(Path.Combine(directory,"selected.cir"),projected);
  var nativeText=end==null?projected+control+".end\n":projected.Insert(end.Range.Start,control);
  var nativeInput=Path.Combine(directory,"reference.cir");File.WriteAllText(nativeInput,nativeText);row.NativeInputSha256=Hash(nativeInput);
  var log=Path.Combine(directory,"ngspice.log");var start=new ProcessStartInfo(ngspice){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=directory};
  // Expanded input has no include boundary; 'a' applies the requested PSpice
  // translation to the whole deck. Record this oracle-only dialect selection.
  if(prepared.Request!.Circuit.Elements.Any(e=>e is PspiceSwitch)){start.ArgumentList.Add("-D");start.ArgumentList.Add("ngbehavior=psa");row.NativeCompatibility="psa";}
  foreach(var argument in new[]{"-n","-b","-o",log,nativeInput})start.ArgumentList.Add(argument);start.Environment["LC_ALL"]="C";
  using var native=Process.Start(start)!;var stdout=native.StandardOutput.ReadToEndAsync();var stderr=native.StandardError.ReadToEndAsync();
  using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))try{await native.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){native.Kill(true);await native.WaitForExitAsync();row.Status="solver_completed_oracle_timeout";continue;}
  File.WriteAllText(Path.Combine(directory,"ngspice.stdout"),await stdout);File.WriteAllText(Path.Combine(directory,"ngspice.stderr"),await stderr);
  var logText=File.Exists(log)?File.ReadAllText(log):"";row.NativeExit=native.ExitCode;row.NativeLogSha256=File.Exists(log)?Hash(log):null;
  var values=Regex.Matches(logText,@"(?m)^frontend_probe_([0-9]+)\s*=\s*([-+0-9.eE]+)\s*$");
  if(native.ExitCode!=0||Regex.IsMatch(logText,@"(?im)^\s*(?:error\b|fatal\b|syntax error\b)")||values.Count!=names.Length){row.Status="solver_completed_oracle_unavailable";row.Detail="native_exit_errors_or_missing_values";continue;}
  var observed=new Dictionary<int,double>();var invalidOracle=false;
  foreach(Match match in values)
  {
   var i=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
   if(!double.TryParse(match.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||i>=names.Length||!observed.TryAdd(i,value)){invalidOracle=true;break;}
  }
  if(invalidOracle){row.Status="solver_completed_oracle_unavailable";row.Detail="non_finite_duplicate_or_out_of_range_oracle_data";continue;}
  foreach(var p in row.Providers)foreach(var pair in observed)
  {
   var name=names[pair.Key];var actual=p.Values[name];var expectedValue=pair.Value;var bound=1e-9+1e-8*Math.Abs(expectedValue);
   var pass=double.IsFinite(actual)&&double.IsFinite(expectedValue)&&Math.Abs(actual-expectedValue)<=bound;
   row.Comparisons.Add(new(p.Backend,name,expectedValue,actual,bound,pass));
  }
  row.Status=row.Comparisons.Count>0&&row.Comparisons.All(c=>c.Passed)?"compared_pass":"numerical_mismatch";
 }
 catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException){row.Status="input_or_harness_failure";row.Detail=e.GetType().Name+":"+e.Message;}
 finally{if(rows.Count%50==0){Save("running");Console.WriteLine(JsonSerializer.Serialize(new{completed=rows.Count,counts=rows.GroupBy(r=>r.Status).ToDictionary(g=>g.Key,g=>g.Count())}));}}
}
foreach(var pair in hashes)if(Hash(Path.Combine(root,pair.Key))!=pair.Value)throw new IOException("Source changed during execution");
Save(stopped?"budget_exhausted":"completed_with_results");
Console.WriteLine(JsonSerializer.Serialize(new{output,counts=rows.GroupBy(r=>r.Status).ToDictionary(g=>g.Key,g=>g.Count()),solver_completed_files=rows.Count(r=>r.Providers.Any(p=>p.Status=="solver_completed"))}));
if(stopped)Environment.ExitCode=2;else if(rows.Any(r=>r.Status is "engine_failure" or "numerical_mismatch" or "input_or_harness_failure" or "input_changed" or "input_changed_during_resolution"))Environment.ExitCode=1;
void Save(string state)
{
 var report=new{schema_version=1,state,started_utc=startedUtc,updated_utc=DateTimeOffset.UtcNow,harness_assembly_sha256=Hash(typeof(Row).Assembly.Location),profile=selected?"generated_frontend_selected_static_op":"generated_frontend_strict_rvi_op",total_inputs=totalInputs,elapsed_seconds=watch.Elapsed.TotalSeconds,inventory_sha256=Hash(inventory),engine_sha256=Hash(engine),ngspice_sha256=Hash(ngspice),source_sha256=hashes,
  assembly_sha256=new[]{typeof(DeckParser).Assembly,typeof(OperatingPointBridge).Assembly,typeof(SimulationClient).Assembly}.ToDictionary(a=>a.GetName().Name!,a=>Hash(a.Location)),
  counts=rows.GroupBy(r=>r.Status).ToDictionary(g=>g.Key,g=>g.Count()),rows,limits=new{max_characters=4_194_304,max_tokens=2_000_000,admission_budget_seconds=admissionSeconds,child_seconds=30,absolute_voltage=1e-9,relative=1e-8},
  limitations=new[]{"Explicit profile: strict R/V/I, or selected static R/C/L/V/I/E/G and bounded PSpice VSWITCH with recorded analysis/control/observation omissions. Never a complete original program run.","All exposed root-node voltages only; no source-current/internal-node/full-deck/AC/transient/model qualification.","Native oracle uses expanded original source with explicit OP control queries; original input controls are never executed.","Admission budget is checked between inputs; bounded current-row operations can finish after it. No hard whole-process deadline is promised.","Windows managed harness and explicit debug Rust executable; coverage is collected separately. No performance ranking, GPU or production qualification."}};
 var destination=Path.Combine(output,"summary.json");var temporary=destination+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(report,json));File.Move(temporary,destination,true);
}
static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
sealed class Row
{
 public CircuitReadinessReport? Readiness{get;set;}
 public string? NativeCompatibility{get;set;}
 public PreparedSourceWaveform[]? SourceWaveforms{get;set;}
 public string Scope{get;set;}="";public string Path{get;set;}="";public string Status{get;set;}="";public string? Detail{get;set;} public string? Card{get;set;} public string? Family{get;set;} public string? Excerpt{get;set;}
 public string? InputSha256{get;set;} public string? ExpandedSha256{get;set;} public string? NativeInputSha256{get;set;} public string? NativeLogSha256{get;set;} public int? NativeExit{get;set;}
 public OperatingPointOmission[] Omissions{get;set;}=[];public SourceHash[] Sources{get;set;}=[];public List<Provider> Providers{get;}=[];public List<Observation> Comparisons{get;}=[];
}
sealed class Provider
{
 public string Backend{get;set;}="";public string Status{get;set;}="";public double ElapsedMs{get;set;} public string? Error{get;set;} public string? RequestSha256{get;set;} public Dictionary<string,double> Values{get;}=new(StringComparer.OrdinalIgnoreCase);
}
sealed record Observation(string Backend,string Node,double NgspiceVolts,double RustVolts,double BoundVolts,bool Passed);
sealed record SourceHash(string Path,string? Section,string Sha256);
