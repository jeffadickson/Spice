#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Simulator.Syntax;
using Simulator.Frontend;
using Simulator.Client;

// Selected frequency probes, not execution of input sweeps or control scripts.
// Reuse a completed OP census and its original-source projection. The current
// frontend must reproduce the exact DC wire request before any AC is attempted.
if(args.Length!=5)throw new ArgumentException("Expected checkout, completed OP run, Rust, ngspice and fresh output");
var root=Path.GetFullPath(args[0]);var op=Path.GetFullPath(args[1]);var engine=Path.GetFullPath(args[2]);var native=Path.GetFullPath(args[3]);var output=Path.GetFullPath(args[4]);
foreach(var p in new[]{root,op,Path.GetDirectoryName(output)!})if(!Directory.Exists(p))throw new DirectoryNotFoundException(p);
foreach(var p in new[]{engine,native})if(!File.Exists(p))throw new FileNotFoundException(p);
if(Directory.Exists(output))throw new IOException("Fresh AC output required");
using var inventory=JsonDocument.Parse(File.ReadAllText(Path.Combine(op,"summary.json")));
if(inventory.RootElement.GetProperty("state").GetString()!="completed_with_results")throw new InvalidDataException("Completed OP census required");
Directory.CreateDirectory(output);
var json=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
var sourceFiles=new[]{"Simulator.Syntax","Simulator.Frontend","Simulator.Client"}.SelectMany(p=>Directory.GetFiles(Path.Combine(root,"dotnet",p)).Where(f=>f.EndsWith(".cs",StringComparison.Ordinal)||f.EndsWith(".csproj",StringComparison.Ordinal)||f.EndsWith("packages.lock.json",StringComparison.Ordinal)))
 .Concat(Directory.GetFiles(Path.Combine(root,"dotnet/Simulator.Syntax/Grammar"),"*.g4")).Append(Path.Combine(root,"scripts/Run-FrontendAcCorpus.cs")).Append(Path.Combine(root,"global.json")).ToArray();
var hashes=sourceFiles.ToDictionary(p=>Path.GetRelativePath(root,p).Replace('\\','/'),Hash);
var rows=new List<AcRow>();var clock=Stopwatch.StartNew();var frequencies=new[]{1.0,100.0,1000.0};var index=-1;
foreach(var input in inventory.RootElement.GetProperty("rows").EnumerateArray())
{
 index++;var row=new AcRow{Scope=input.GetProperty("scope").GetString()!,Path=input.GetProperty("path").GetString()!,Status="not_in_dc_compared_cohort"};rows.Add(row);
 if(input.GetProperty("status").GetString()!="compared_pass")continue;
 if(clock.Elapsed.TotalSeconds>900){row.Status="not_admitted_budget";continue;}
 try
 {
  var prior=Path.Combine(op,"cases",index.ToString("D4",CultureInfo.InvariantCulture));var expanded=Path.Combine(prior,"expanded.cir");
  if(Hash(expanded)!=input.GetProperty("expanded_sha256").GetString())throw new InvalidDataException("Expanded input changed");
  var syntax=DeckParser.Parse(File.ReadAllText(expanded),new ParserLimits(4_194_304,2_000_000),collectDeclarations:true);
  var selected=OperatingPointSelection.Select(syntax);var prepared=selected.Prepared;
  if(!selected.Success)throw new InvalidDataException("Previously solved preparation now refused");
  if(!JsonNode.DeepEquals(JsonNode.Parse(JsonSerializer.Serialize(prepared.Request,json)),JsonNode.Parse(File.ReadAllText(Path.Combine(prior,"lu.request.json"))))
      )throw new InvalidDataException("DC wire request differs from the completed census");
  if(prepared.ExplicitAcSources==0){row.Status="no_explicit_ac_excitation";continue;}
  if(prepared.AcSources.Length>128){row.Status="ac_source_budget";continue;}
  var names=prepared.RootNodes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
  if(names.Length==0||names.Any(n=>!Regex.IsMatch(n,@"\A[a-zA-Z0-9_.:+/\[\]-]+\z"))){row.Status="oracle_unavailable";continue;}
  var directory=Path.Combine(output,"cases",index.ToString("D4",CultureInfo.InvariantCulture));Directory.CreateDirectory(directory);
  var selectedPath=Path.Combine(prior,"selected.cir");var text=File.ReadAllText(selectedPath);row.SelectedSha256=Hash(selectedPath);
  var original=File.ReadAllText(expanded).ToCharArray();
  foreach(var omission in selected.Omissions.Where(o=>o.Kind!="source_ac"))
   for(var i=omission.Range.Start;i<omission.Range.Start+omission.Range.Length;i++)if(original[i] is not ('\r' or '\n'))original[i]=' ';
  if(new string(original)!=text)throw new InvalidDataException("Original-source projection differs");
  var projected=DeckParser.Parse(text,collectDeclarations:true);
  if(projected.Declarations==null||projected.Declarations.Cards.Any(c=>c.Kind=="control"))throw new InvalidDataException("Unprocessed input control");
  var end=projected.Declarations.Cards.FirstOrDefault(c=>c.ScopeId==0&&c.Kind=="endCard")?.Range.Start??text.Length;
  var references=new Dictionary<(double,string),Complex>();
  foreach(var frequency in frequencies)
  {
   var label=frequency.ToString("R",CultureInfo.InvariantCulture);
   var control="\n.control\nset numdgt=17\nac lin 1 "+label+" "+label+"\n"+string.Concat(names.Select((name,i)=>$"let probe_r_{i} = real(v(\"{name}\"))\nlet probe_i_{i} = imag(v(\"{name}\"))\nprint probe_r_{i}\nprint probe_i_{i}\n"))+"quit\n.endc\n";
   var deck=Path.Combine(directory,"reference-"+label+".cir");var log=Path.Combine(directory,"reference-"+label+".log");File.WriteAllText(deck,text.Insert(end,control));
   await RunNative(deck,log,prepared.Request!.Circuit.Elements.Any(e=>e is PspiceSwitch));
   var data=File.ReadAllText(log);
   for(var n=0;n<names.Length;n++)
   {
    var value=new Complex(Read("r",n),Read("i",n));
    if(!double.IsFinite(Complex.Abs(value)))throw new InvalidDataException("Native AC reference magnitude overflow");
    references[(frequency,names[n])]=value;
   }
   double Read(string component,int n)
   {
    var matches=Regex.Matches(data,@"(?m)^\s*probe_"+component+"_"+n+@"\s*=\s*([^\s]+)\s*$");
    if(matches.Count!=1||!double.TryParse(matches[0].Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value))throw new InvalidDataException("Missing/nonfinite/duplicate native AC probe");
    return value;
   }
  }
  row.Status="compared_pass";
  foreach(var backend in new[]{"lu","faer"})
  {
   try
   {
   var request=prepared.Request! with{CaseId="frontend_ac_probe",Backend=backend,Analysis=new Ac(frequencies,prepared.AcSources.ToArray())};
   var requestText=JsonSerializer.Serialize(request,json);File.WriteAllText(Path.Combine(directory,backend+".request.json"),requestText);
   using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));
   var response=await new SimulationClient(engine).RunJsonAsync(requestText,limit.Token);
   File.WriteAllText(Path.Combine(directory,backend+".response.json"),response.Payload.GetRawText());
   // Shared Client validation owns vector/descriptor dimensions, including
   // null components. Reject nonfinite states before recording JSON evidence.
   var manifest=response.ReadManifest();
   if(manifest.AcSamples.Any(s=>!double.IsFinite(s.FrequencyHz)||s.Values.Any(v=>!double.IsFinite(v.Real)||!double.IsFinite(v.Imag))))
       throw new InvalidDataException("Nonfinite AC result state");
   var payload=response.Payload;
   if(!response.Succeeded||payload.GetProperty("case_id").GetString()!=request.CaseId||payload.GetProperty("backend").GetProperty("id").GetString()!=(backend=="lu"?"nalgebra_dense_lu_reference":"faer_sparse_lu")
      ||payload.GetProperty("backend").GetProperty("placement").GetString()!="cpu"||payload.GetProperty("backend").GetProperty("gpu").GetBoolean())throw new InvalidDataException("AC result identity differs");
   var signals=payload.GetProperty("signals").EnumerateArray().ToArray();var samples=payload.GetProperty("ac_samples").EnumerateArray().ToArray();
   if(signals.Select(s=>s.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count()!=signals.Length)throw new InvalidDataException("Duplicate AC signal identity");
   if(samples.Length!=frequencies.Length)throw new InvalidDataException("Incomplete AC frequency grid");
   for(var f=0;f<frequencies.Length;f++)
   {
    if(samples[f].GetProperty("frequency_hz").GetDouble()!=frequencies[f])throw new InvalidDataException("AC frequency coordinate differs");
    foreach(var name in names)
    {
     var signal="v("+prepared.RootNodes[name]+")";var indices=Enumerable.Range(0,signals.Length).Where(i=>signals[i].GetProperty("id").GetString()==signal).ToArray();
     if(indices.Length!=1||signals[indices[0]].GetProperty("unit").GetString()!="V"||signals[indices[0]].GetProperty("quantity").GetString()!="voltage")throw new InvalidDataException("Missing/duplicate/non-voltage AC signal");
     var value=samples[f].GetProperty("values")[indices[0]];var actual=new Complex(value.GetProperty("real").GetDouble(),value.GetProperty("imag").GetDouble());
     var expected=references[(frequencies[f],name)];var error=Complex.Abs(actual-expected);var passed=double.IsFinite(error)&&error<=1e-9+1e-8*Complex.Abs(expected);
     row.Checks.Add(new(backend,name,frequencies[f],expected.Real,expected.Imaginary,actual.Real,actual.Imaginary,passed));if(!passed)row.Status="numerical_mismatch";
    }
   }
   row.CompletedProviders++;
   }
   catch(Exception e)when(e is IOException or JsonException or InvalidDataException or InvalidOperationException or OperationCanceledException or KeyNotFoundException)
   {row.Status="provider_failure";row.ProviderErrors.Add(backend+":"+e.GetType().Name+":"+e.Message[..Math.Min(512,e.Message.Length)]);}
  }
 }
 catch(Exception e)when(e is IOException or JsonException or InvalidDataException or InvalidOperationException or OperationCanceledException or KeyNotFoundException)
 {row.Status="execution_or_oracle_failure";row.Error=e.GetType().Name+":"+e.Message[..Math.Min(512,e.Message.Length)];}
}
foreach(var file in sourceFiles)if(Hash(file)!=hashes[Path.GetRelativePath(root,file).Replace('\\','/')])throw new IOException("AC source changed during qualification");
var report=new{state=rows.Any(r=>r.Status=="not_admitted_budget")?"budget_exhausted":"completed_with_results",profile="selected_linear_ac_probes",frequencies_hz=frequencies,source_sha256=hashes,
 harness_assembly_sha256=Hash(System.Reflection.Assembly.GetExecutingAssembly().Location),
 op_summary_sha256=Hash(Path.Combine(op,"summary.json")),engine_sha256=Hash(engine),ngspice_sha256=Hash(native),elapsed_seconds=clock.Elapsed.TotalSeconds,
 counts=rows.GroupBy(r=>r.Status).ToDictionary(g=>g.Key,g=>g.Count()),rows,absolute_volts=1e-9,relative=1e-8,
 limitations=new[]{"Only previously OP-compared files with explicit AC excitation; other original corpus rows not AC-qualified.","Selected1/100/1000Hz complex root-voltage probes, not original sweeps/control programs/transients/internal currents.","Two CPU providers; original-source ngspice projection; debug Rust, no performance or GPU qualification."}};
var path=Path.Combine(output,"summary.json");File.WriteAllText(path,JsonSerializer.Serialize(report,json));Console.WriteLine(path);
Environment.ExitCode=rows.Any(r=>r.Status is "numerical_mismatch" or "execution_or_oracle_failure" or "provider_failure")?1:rows.Any(r=>r.Status=="not_admitted_budget")?2:0;
string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
async Task RunNative(string deck,string log,bool pspice)
{
 var start=new ProcessStartInfo(native){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 if(pspice){start.ArgumentList.Add("-D");start.ArgumentList.Add("ngbehavior=psa");}
 foreach(var arg in new[]{"-n","-b","-o",log,deck})start.ArgumentList.Add(arg);start.Environment["LC_ALL"]="C";
 using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
 using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));
 try{await process.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}
 File.WriteAllText(log+".stdout",await stdout);File.WriteAllText(log+".stderr",await stderr);
 if(process.ExitCode!=0||!File.Exists(log)||new FileInfo(log).Length>4_194_304)throw new IOException("Native AC oracle failed");
}
sealed class AcRow
{
 public string Scope {get;set;}="";public string Path {get;set;}="";public string Status {get;set;}="";
 public string? Error {get;set;} public string? SelectedSha256 {get;set;}
 public List<AcCheck> Checks {get;}=[];
 public int CompletedProviders {get;set;}
 public List<string> ProviderErrors {get;}=[];
}
sealed record AcCheck(string Backend,string Node,double FrequencyHz,double ExpectedReal,double ExpectedImag,double ActualReal,double ActualImag,bool Passed);
