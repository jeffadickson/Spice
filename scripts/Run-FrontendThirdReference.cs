#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:project ../dotnet/Simulator.Syntax/Simulator.Syntax.csproj
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Syntax;

// Independent parser/model reference over the exact original-source projection.
// The input census is evidence/data, never a source of executable instructions.
if(args is ["--self-test"]){ReferencePrecision.CheckContracts();Console.WriteLine("Reference precision contracts passed");return;}
if(args.Length is not (6 or 7))throw new ArgumentException("Expected checkout, completed OP/AC runs, worker DLL, patch manifest or '-', fresh output, optional budget seconds");
var root=Path.GetFullPath(args[0]);var op=Path.GetFullPath(args[1]);var ac=Path.GetFullPath(args[2]);var worker=Path.GetFullPath(args[3]);var patch=args[4]=="-"?null:Path.GetFullPath(args[4]);var output=Path.GetFullPath(args[5]);
foreach(var p in new[]{root,op,ac,Path.GetDirectoryName(output)!})if(!Directory.Exists(p))throw new DirectoryNotFoundException(p);
if(!File.Exists(worker)||patch!=null&&!File.Exists(patch)||Directory.Exists(output))throw new IOException("Existing reference inputs and fresh output required");
var budget=args.Length==7?int.Parse(args[6],CultureInfo.InvariantCulture):900;if(budget is <1 or >3600)throw new ArgumentOutOfRangeException(nameof(budget));
using var opDoc=JsonDocument.Parse(File.ReadAllText(Path.Combine(op,"summary.json")));using var acDoc=JsonDocument.Parse(File.ReadAllText(Path.Combine(ac,"summary.json")));
if(opDoc.RootElement.GetProperty("state").GetString()!="completed_with_results"||acDoc.RootElement.GetProperty("state").GetString()!="completed_with_results")throw new InvalidDataException("Completed source censuses required");
Directory.CreateDirectory(output);var json=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,WriteIndented=true};
var acRows=acDoc.RootElement.GetProperty("rows").EnumerateArray().ToDictionary(r=>r.GetProperty("scope").GetString()+"/"+r.GetProperty("path").GetString());
var sources=Directory.GetFiles(Path.Combine(root,"evaluation/SpiceSharp.ThirdReference")).Where(p=>p.EndsWith(".cs",StringComparison.Ordinal)||p.EndsWith(".csproj",StringComparison.Ordinal)||p.EndsWith("packages.lock.json",StringComparison.Ordinal)).Append(Path.Combine(root,"scripts/Run-FrontendThirdReference.cs")).ToArray();
var hashes=sources.ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash);var workerHash=Hash(worker);var patchHash=patch==null?null:Hash(patch);
Dictionary<string,string>? referenceAssemblies=null;string? referenceFlavor=null;var rows=new List<ThirdRow>();var clock=Stopwatch.StartNew();var index=-1;
foreach(var input in opDoc.RootElement.GetProperty("rows").EnumerateArray())
{
 index++;var row=new ThirdRow{Scope=input.GetProperty("scope").GetString()!,Path=input.GetProperty("path").GetString()!,Status="not_in_dc_compared_cohort"};rows.Add(row);
 if(input.GetProperty("status").GetString()!="compared_pass")continue;
 if(clock.Elapsed.TotalSeconds>=budget){row.Status="not_admitted_budget";continue;}
 try{
  var prior=Path.Combine(op,"cases",index.ToString("D4",CultureInfo.InvariantCulture));var expanded=Path.Combine(prior,"expanded.cir");var selectedPath=Path.Combine(prior,"selected.cir");
  if(Hash(expanded)!=input.GetProperty("expanded_sha256").GetString())throw new InvalidDataException("Expanded source identity differs");
  var original=File.ReadAllText(expanded).ToCharArray();var previousEnd=0;
  foreach(var omission in input.GetProperty("omissions").EnumerateArray().Where(o=>o.GetProperty("kind").GetString() is not ("source_ac" or "source_transient")).OrderBy(o=>o.GetProperty("range").GetProperty("start").GetInt32())){
   var range=omission.GetProperty("range");var start=range.GetProperty("start").GetInt32();var length=range.GetProperty("length").GetInt32();
   if(start<previousEnd||length<0||start>original.Length-length)throw new InvalidDataException("Bad source omission range");previousEnd=start+length;
   for(var i=start;i<previousEnd;i++)if(original[i] is not ('\r' or '\n'))original[i]=' ';
  }
  var text=new string(original);if(text!=File.ReadAllText(selectedPath))throw new InvalidDataException("Original-source projection differs");row.SelectedSha256=Hash(selectedPath);
  // Parser3.4.1 can stall on long runs of blank physical lines from omitted
  // control blocks. Blank lines have no SPICE card semantics. Preserve the title
  // and every nonblank physical line verbatim (including continuation prefixes),
  // but omit blank lines for this independent parser only. Raw selected input,
  // normalized base and each final analysis deck have separate fingerprints.
  using(var reader=new StringReader(text)){
   var normalized=new System.Text.StringBuilder();var first=true;string? line;
   while((line=reader.ReadLine())!=null){if(first||!string.IsNullOrWhiteSpace(line))normalized.AppendLine(line);first=false;}
   text=normalized.ToString();
  }
  row.NormalizedBaseSha256=Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
  var syntax=DeckParser.Parse(text,new ParserLimits(4_194_304,2_000_000),collectDeclarations:true);if(syntax.Status!="accepted_structure"||syntax.Declarations==null)throw new InvalidDataException("Projection structure unavailable");
  var end=syntax.Declarations.Cards.FirstOrDefault(c=>c.ScopeId==0&&c.Kind=="endCard")?.Range.Start??text.Length;
  var nativeChecks=input.GetProperty("comparisons").EnumerateArray().ToArray();if(nativeChecks.Length==0||nativeChecks.Any(c=>!c.GetProperty("passed").GetBoolean()))throw new InvalidDataException("Source OP evidence is not qualified");
  foreach(var group in nativeChecks.GroupBy(c=>c.GetProperty("node").GetString(),StringComparer.OrdinalIgnoreCase)){
   var paired=group.ToArray();if(paired.Length!=2||string.Join(",",paired.Select(c=>c.GetProperty("backend").GetString()).Order())!="faer,lu"||paired[0].GetProperty("ngspice_volts").GetDouble()!=paired[1].GetProperty("ngspice_volts").GetDouble())throw new InvalidDataException("Incomplete/inconsistent paired OP evidence");
  }
  var names=nativeChecks.Select(c=>c.GetProperty("node").GetString()!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
  var directory=Path.Combine(output,"cases",index.ToString("D4",CultureInfo.InvariantCulture));Directory.CreateDirectory(directory);
  var probes=names.Select((n,i)=>new ThirdProbe("p"+i,"voltage",n)).ToArray();
  var opValues=await Reference("op",0,".op\n","op");
  if(opValues!=null){
   for(var n=0;n<names.Length;n++){
    var sharp=opValues[n];foreach(var c in nativeChecks.Where(c=>c.GetProperty("node").GetString()!.Equals(names[n],StringComparison.OrdinalIgnoreCase))){
     Compare("op",0,names[n],"rust_"+c.GetProperty("backend").GetString(),sharp,new(c.GetProperty("rust_volts").GetDouble(),0));
    }
    var reference=nativeChecks.First(c=>c.GetProperty("node").GetString()!.Equals(names[n],StringComparison.OrdinalIgnoreCase));Compare("op",0,names[n],"ngspice",sharp,new(reference.GetProperty("ngspice_volts").GetDouble(),0));
   }
   row.OpState=row.Checks.Where(c=>c.Mode=="op").All(c=>c.Passed)?"compared_pass":"numerical_mismatch";
  }
  if(acRows.TryGetValue(row.Scope+"/"+row.Path,out var priorAc)&&priorAc.GetProperty("status").GetString()=="compared_pass"){
   if(priorAc.GetProperty("selected_sha256").GetString()!=row.SelectedSha256)throw new InvalidDataException("AC source identity differs");
   var checks=priorAc.GetProperty("checks").EnumerateArray().ToArray();if(checks.Length==0||checks.Any(c=>!c.GetProperty("passed").GetBoolean()))throw new InvalidDataException("AC evidence is not qualified");
   var frequencies=checks.Select(c=>c.GetProperty("frequency_hz").GetDouble()).Distinct().Order().ToArray();var completed=0;
   foreach(var f in frequencies){
    if(!double.IsFinite(f)||f<=0)throw new InvalidDataException("Bad reference frequency");
    var label=f.ToString("R",CultureInfo.InvariantCulture);var values=await Reference("ac",f,".ac lin 1 "+label+" "+label+"\n","ac-"+label);if(values==null)continue;completed++;
    for(var n=0;n<names.Length;n++){
     var matching=checks.Where(c=>c.GetProperty("frequency_hz").GetDouble()==f&&c.GetProperty("node").GetString()!.Equals(names[n],StringComparison.OrdinalIgnoreCase)).ToArray();if(matching.Length!=2||string.Join(",",matching.Select(c=>c.GetProperty("backend").GetString()).Order())!="faer,lu")throw new InvalidDataException("Incomplete paired AC observations");
     foreach(var c in matching)Compare("ac",f,names[n],"rust_"+c.GetProperty("backend").GetString(),values[n],new(c.GetProperty("actual_real").GetDouble(),c.GetProperty("actual_imag").GetDouble()));
     Compare("ac",f,names[n],"ngspice",values[n],new(matching[0].GetProperty("expected_real").GetDouble(),matching[0].GetProperty("expected_imag").GetDouble()));
    }
   }
   row.AcState=completed!=frequencies.Length?"reference_unavailable":row.Checks.Where(c=>c.Mode=="ac").All(c=>c.Passed)?"compared_pass":"numerical_mismatch";
  }
  row.Status=row.OpState=="numerical_mismatch"||row.AcState=="numerical_mismatch"?"numerical_mismatch":row.BudgetExhausted?"not_admitted_budget":row.Errors.Count>0?"reference_unavailable":row.AcState=="compared_pass"?"three_way_op_ac":"three_way_op_only";
  async Task<Complex[]?> Reference(string mode,double coordinate,string analysis,string label){
   if(clock.Elapsed.TotalSeconds>=budget){row.BudgetExhausted=true;row.Errors.Add(label+": budget exhausted");return null;}
   var deck=Path.Combine(directory,label+".cir");File.WriteAllText(deck,end==text.Length?text+analysis+".end\n":text.Insert(end,analysis));
   var job=Path.Combine(directory,label+".job.json");var destination=Path.Combine(directory,label+".result.json");var caseId="third_"+index+"_"+label;
   File.WriteAllText(job,JsonSerializer.Serialize(new{case_id=caseId,mode,deck_path=deck,probes,patch_manifest=patch},json));
   var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{worker,job,destination})start.ArgumentList.Add(arg);
   using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));
   try{await process.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();row.Errors.Add(label+": reference timeout");return null;}
   File.WriteAllText(Path.Combine(directory,label+".stdout"),await stdout);File.WriteAllText(Path.Combine(directory,label+".stderr"),await stderr);
   if(process.ExitCode!=0||!File.Exists(destination)||new FileInfo(destination).Length>4_194_304){row.Errors.Add(label+": worker failed/missing output");return null;}
   using var result=JsonDocument.Parse(File.ReadAllText(destination));var data=result.RootElement;
   if(data.GetProperty("status").GetString()!="executed"){row.Errors.Add(label+": "+string.Join(';',data.GetProperty("diagnostics").EnumerateArray().Select(v=>v.GetString())));return null;}
   if(data.GetProperty("schema_version").GetInt32()!=1||data.GetProperty("case_id").GetString()!=caseId||data.GetProperty("mode").GetString()!=mode||data.GetProperty("deck_sha256").GetString()!=Hash(deck)||data.GetProperty("coordinate_unit").GetString()!=(mode=="ac"?"Hz":"op_index"))throw new InvalidDataException("Reference identity/unit mismatch");
   var assemblies=data.GetProperty("assembly_hashes").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!);
   if(assemblies.Count!=3||new[]{"SpiceSharp","SpiceSharpParser","SpiceSharpBehavioral"}.Any(n=>!assemblies.TryGetValue(n,out var h)||h.Length!=64||h.Any(c=>!Uri.IsHexDigit(c))))throw new InvalidDataException("Reference assembly identities unavailable");
   if(data.GetProperty("patch_manifest_sha256").GetString()!=patchHash)throw new InvalidDataException("Reference patch identity differs");
   var flavor=data.GetProperty("flavor").GetString();if(string.IsNullOrEmpty(flavor))throw new InvalidDataException("Reference flavor unavailable");
   if(referenceAssemblies==null){referenceAssemblies=assemblies;referenceFlavor=flavor;}
   else if(referenceAssemblies.Any(p=>assemblies[p.Key]!=p.Value)||flavor!=referenceFlavor)throw new InvalidDataException("Reference dependencies changed between jobs");
   var descriptors=data.GetProperty("probes").EnumerateArray().ToArray();if(descriptors.Length!=probes.Length||descriptors.Select(p=>p.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count()!=probes.Length)throw new InvalidDataException("Reference probe descriptors differ");
   for(var n=0;n<probes.Length;n++){var p=descriptors.Single(d=>d.GetProperty("id").GetString()==probes[n].Id);if(p.GetProperty("kind").GetString()!="voltage"||p.GetProperty("target").GetString()!=probes[n].Target||p.GetProperty("unit").GetString()!="V")throw new InvalidDataException("Reference observable differs");}
   var samples=data.GetProperty("samples");if(samples.GetArrayLength()!=1)throw new InvalidDataException("Reference sample count differs");
   // AC computes frequency through angular frequency, producing one-ulp roundoff
   // (1000Hz can export999.9999999999999Hz). Bound identity roundoff separately
   // from voltage accuracy; a different probe frequency is still a protocol error.
   var actualCoordinate=samples[0].GetProperty("coordinate").GetDouble();
   if(!ReferencePrecision.Matches(actualCoordinate,coordinate,mode=="ac"))throw new InvalidDataException("Reference frequency differs beyond four representable-double steps");
   var vector=samples[0].GetProperty("values");if(vector.EnumerateObject().Count()!=probes.Length||vector.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=probes.Length)throw new InvalidDataException("Reference vector shape differs");
   var values=probes.Select(p=>{var v=vector.GetProperty(p.Id);return new Complex(v.GetProperty("real").GetDouble(),v.GetProperty("imag").GetDouble());}).ToArray();if(values.Any(v=>!double.IsFinite(v.Real)||!double.IsFinite(v.Imaginary)))throw new InvalidDataException("Nonfinite reference voltage");
   row.CompletedWorkers++;row.ReferenceFlavor=data.GetProperty("flavor").GetString();return values;
  }
  void Compare(string mode,double coordinate,string node,string provider,Complex value,Complex expected){
   var magnitude=Complex.Abs(expected);if(!double.IsFinite(magnitude))throw new InvalidDataException("Nonfinite reference magnitude");var error=Complex.Abs(value-expected);var tolerance=1e-9+1e-8*magnitude;row.Checks.Add(new(mode,coordinate,node,provider,value.Real,value.Imaginary,expected.Real,expected.Imaginary,error,tolerance,double.IsFinite(error)&&error<=tolerance));
  }
 }catch(Exception e){row.Status="protocol_or_projection_failure";row.Errors.Add(e.GetType().Name+": "+e.Message[..Math.Min(e.Message.Length,1024)]);}
 if(index%50==0)Write("in_progress");
}
if(sources.Any(p=>Hash(p)!=hashes[Path.GetRelativePath(root,p).Replace('\\','/')])||Hash(worker)!=workerHash||patch!=null&&Hash(patch)!=patchHash)throw new InvalidDataException("Reference source/binary changed during campaign");
Write("completed_with_results");Console.WriteLine(output);
Environment.ExitCode=rows.Any(r=>r.Status is "numerical_mismatch" or "protocol_or_projection_failure")?1:rows.Any(r=>r.Status=="not_admitted_budget")?2:rows.Any(r=>r.Status=="reference_unavailable")?3:0;
void Write(string state)=>File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{schema_version=1,state,elapsed_seconds=clock.Elapsed.TotalSeconds,budget_seconds=budget,op_summary_sha256=Hash(Path.Combine(op,"summary.json")),ac_summary_sha256=Hash(Path.Combine(ac,"summary.json")),worker_sha256=workerHash,patch_manifest_sha256=patchHash,reference_assembly_sha256=referenceAssemblies,reference_flavor=referenceFlavor,source_sha256=hashes,rows,limits=new[]{"Only native-compared OP cohort and its selected AC probes; full original programs remain unqualified.","Each SpiceSharp observation is compared separately to native ngspice and both stored Rust-provider observations.","Reference unavailable/timeouts and protocol failures are not numerical passes.","Patched-reference flavor is explicit: IL branch widths only; no library numerical formulas changed.","Windows/.NET managed runs only; no new performance, GPU, internal upstream coverage or production qualification."}},json));
string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
sealed record ThirdProbe(string Id,string Kind,string Target);
sealed record ThirdCheck(string Mode,double Coordinate,string Node,string Provider,double SharpReal,double SharpImag,double ReferenceReal,double ReferenceImag,double Error,double Tolerance,bool Passed);
sealed class ThirdRow{
 public string Scope{get;set;}="";public string Path{get;set;}="";public string Status{get;set;}="";public string OpState{get;set;}="reference_unavailable";public string AcState{get;set;}="no_qualified_input_ac";public string? SelectedSha256{get;set;}public string? NormalizedBaseSha256{get;set;}public string? ReferenceFlavor{get;set;}public bool BudgetExhausted{get;set;}public int CompletedWorkers{get;set;}public List<ThirdCheck> Checks{get;}=[];public List<string> Errors{get;}=[];
}
static class ReferencePrecision{
 // Angular frequency conversion is allowed at most four adjacent binary64
 // values in either direction; this is identity roundoff, not a sweep tolerance.
 public static bool Matches(double actual,double expected,bool ac){
  if(!double.IsFinite(actual)||!double.IsFinite(expected))return false;
  if(!ac)return actual==expected;
  var lower=expected;var upper=expected;for(var i=0;i<4;i++){lower=Math.BitDecrement(lower);upper=Math.BitIncrement(upper);}return actual>=lower&&actual<=upper;
 }
 public static void CheckContracts(){
  var fourth=1000d;for(var i=0;i<4;i++)fourth=Math.BitDecrement(fourth);
  if(!Matches(1000,1000,true)||!Matches(Math.BitDecrement(1000),1000,true)||!Matches(fourth,1000,true)||Matches(Math.BitDecrement(fourth),1000,true)||Matches(1001,1000,true)||Matches(double.NaN,1000,true)||Matches(double.PositiveInfinity,1000,true)||Matches(Math.BitIncrement(1),1,false))throw new Exception("Frequency identity boundary failed");
 }
}
