using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.RequestedQualification;
/// <summary>Reproduces one pinned, stateless io_ordering stop/resume experiment.
/// This qualification helper never changes production comparison or corpus status.</summary>
public static class StopGridQualification
{
 internal const string ProjectionHash="4f8e293f78418ae9bdd815c47fc7bb5fd69d27abd933f2752dc546f46e6ed8aa";
 internal const string NativeHash="12bc57018aa5142d28065704d09bc6b06da2a3886f7864ca042ed4824ccf910f";
 internal const string RetainedHash="9a645dceff79c4454e6cb54d9f66af5b2dcdea22176285e66f41e5557cf54df2";
 private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
 private static readonly (string Engine,string Native,string Quantity,string Unit)[] Columns=[("v(n1)","v(1)","voltage","V"),("v(n2)","v(2)","voltage","V"),("i(e0)","i(v1)","current","A"),("i(e3)","i(a1)","current","A")];
 /// <summary>Arguments: pinned original projection, retained engine manifest JSON,
 /// pinned existing ngspice executable, fresh output under an existing parent.
 /// Native execution is limited to thirty seconds; success is supplemental only.</summary>
 public static async Task<int> RunAsync(string[] args)
 {
  if(args.Length!=4)throw new ArgumentException("Expected original projection, retained manifest, ngspice and fresh output.");
  var paths=args.Select(Path.GetFullPath).ToArray();foreach(var path in paths.Take(3))if(!File.Exists(path))throw new FileNotFoundException(path);
  if(!Directory.Exists(Path.GetDirectoryName(paths[3])))throw new DirectoryNotFoundException("Missing output parent.");
  if(File.Exists(paths[3])||Directory.Exists(paths[3]))throw new IOException("Fresh output required.");
  if(new FileInfo(paths[0]).Length>4096||new FileInfo(paths[1]).Length>1024*1024||new FileInfo(paths[2]).Length>128*1024*1024)throw new InvalidDataException("input_budget");
  var original=await File.ReadAllBytesAsync(paths[0]);var manifestBytes=await File.ReadAllBytesAsync(paths[1]);
  // This reproduces one retained result, not an admission path for a new cohort.
  if(HashBytes(manifestBytes)!=RetainedHash)throw new InvalidDataException("unpinned_retained_manifest");
  using var document=JsonDocument.Parse(manifestBytes);var engine=new SimulationResponse("1","succeeded",true,document.RootElement).ReadManifest();
  var plan=Prepare(original,engine);if(Hash(paths[2])!=NativeHash)throw new InvalidDataException("unpinned_native_executable");
  var immutable=paths.Take(3).Concat(new[]{typeof(StopGridQualification).Assembly.Location,typeof(DeckParser).Assembly.Location,typeof(SimulationResponse).Assembly.Location}).Distinct().ToDictionary(p=>p,Hash);
  // Freeze the actual bytes read, rather than silently mixing pre/post-edit inputs.
  if(HashBytes(original)!=immutable[paths[0]]||HashBytes(manifestBytes)!=immutable[paths[1]])throw new InvalidDataException("input_changed_before_dispatch");
  Directory.CreateDirectory(paths[3]);var input=Path.Combine(paths[3],"instrumented.cir");await File.WriteAllTextAsync(input,plan.Text,new UTF8Encoding(false));
  await File.WriteAllTextAsync(Path.Combine(paths[3],"instrumentation.txt"),plan.Instrumentation);
  await File.WriteAllBytesAsync(Path.Combine(paths[3],"original.cir"),original);await File.WriteAllBytesAsync(Path.Combine(paths[3],"retained-engine.json"),manifestBytes);
  var watch=Stopwatch.StartNew();
  try{
   var start=new ProcessStartInfo(paths[2]){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=paths[3]};foreach(var a in new[]{"-n","-D","filetype=ascii","-b",input})start.ArgumentList.Add(a);
   using var child=Process.Start(start)??throw new IOException("Native process did not start.");var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));var timedOut=false;
   try{await child.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){timedOut=true;if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();}
   var log=await stdout;var error=await stderr;await File.WriteAllTextAsync(Path.Combine(paths[3],"stdout.log"),log);await File.WriteAllTextAsync(Path.Combine(paths[3],"stderr.log"),error);
   if(timedOut||child.ExitCode!=0||log.Length+error.Length>1024*1024)throw new InvalidDataException("native_deadline_exit_or_log_budget");
   // Exactly the expected pause diagnostics are allowed, not arbitrary errors.
   var lines=error.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);var pauses=lines.Count(l=>l.Contains("condition met: stop  when time = ",StringComparison.Ordinal));
   if(pauses!=plan.Stops||lines.Any(l=>!l.Contains("condition met: stop  when time = ",StringComparison.Ordinal)&&l!="doAnalyses: pause requested"&&l!="run simulation interrupted"&&l!="simulation interrupted"))throw new InvalidDataException("unexpected_native_pause_or_diagnostic");
   var rawPath=Path.Combine(paths[3],"native.raw");if(!File.Exists(rawPath)||new FileInfo(rawPath).Length>8*1024*1024)throw new InvalidDataException("raw_missing_or_budget");
   var raw=AsciiRaw.Read(await File.ReadAllTextAsync(rawPath));var comparison=Compare(engine,raw);
   var changed=immutable.Where(p=>!File.Exists(p.Key)||Hash(p.Key)!=p.Value).Select(p=>p.Key).ToArray();
   var valid=changed.Length==0;var report=new{scope="pinned_stateless_stop_grid_poc_only",primary_statuses_changed=false,original_sha256=HashBytes(original),instrumented_sha256=Hash(input),raw_sha256=Hash(rawPath),input_hashes=immutable,provenance_valid=valid,changed_paths=changed,physical_and_analysis_bytes_preserved=plan.BytesPreserved,owned_stop_commands=plan.Stops,owned_resume_commands=plan.Stops,observed_pauses=pauses,elapsed_including_postprocessing_s=watch.Elapsed.TotalSeconds,comparison,status=valid&&comparison.Agreed?"supplemental_common_points_agreed":"supplemental_failed"};
   await Save(paths[3],"axes.json",new{engine=engine.Samples.Select(s=>s.TimeS),native=raw.Points.Select(p=>p[0].Real),comparison.Matches});await Save(paths[3],"summary.json",report);return valid&&comparison.Agreed?0:2;
  }catch(Exception e)when(e is IOException or InvalidDataException or JsonException){await Save(paths[3],"failure.json",new{status="supplemental_failed",reason=e.Message,primary_statuses_changed=false});return 2;}
 }
 internal sealed record Preparation(string Text,string Instrumentation,int Stops,bool BytesPreserved);
 internal static Preparation Prepare(byte[] original,ResultManifest engine){
  if(HashBytes(original)!=ProjectionHash)throw new InvalidDataException("unpinned_original_projection");ValidateEngine(engine);
  var text=new UTF8Encoding(false,true).GetString(original);var syntax=DeckParser.Parse(text,collectDeclarations:true);var c=syntax.Declarations;
  if(syntax.Status!="accepted_structure"||c==null||c.ControlBlocks.Length!=0||c.Resources.Length!=0||c.Analyses.Length!=1||c.Analyses[0].Kind!="tran")throw new InvalidDataException("pinned_structure_gap");
  var ends=c.Cards.Where(x=>x.ScopeId==0&&x.Kind=="endCard").ToArray();if(ends.Length!=1)throw new InvalidDataException("pinned_end_gap");
  var stops=engine.Samples.Skip(1).SkipLast(1).Select(s=>"stop when time = "+s.TimeS.ToString("R",CultureInfo.InvariantCulture)).ToArray();
  var controls=".control\n"+string.Join('\n',stops)+"\nrun\n"+string.Concat(Enumerable.Repeat("resume\n",stops.Length))+"write native.raw all\nquit\n.endc\n";
  var result=text.Insert(ends[0].Range.Start,controls);var recovered=Encoding.UTF8.GetBytes(result.Remove(ends[0].Range.Start,controls.Length));if(HashBytes(recovered)!=ProjectionHash)throw new InvalidDataException("physical_bytes_changed");return new(result,controls,stops.Length,true);
 }
 internal sealed record Match(double Requested,double Actual,int NativeIndex,double UlpDistance,bool Exact);
 internal sealed record ColumnResult(string Engine,string Native,string Unit,int Points,int Failed,double MaximumError);
 internal sealed record Comparison(bool Agreed,int EnginePoints,int NativePoints,int NativeExtraPoints,double UlpGuard,double MaximumUlp,ImmutableArray<Match> Matches,ImmutableArray<ColumnResult> Observables);
 internal static Comparison Compare(ResultManifest engine,NativeRaw raw){
  ValidateEngine(engine);
  if(raw.Complex||raw.PlotName!="Transient Analysis"||raw.Points.Length is <101 or >4096||raw.Variables.Length!=5||raw.Variables[0]!=new RawVariable("time","time"))throw new InvalidDataException("native_shape_or_identity");
  var axis=raw.Points.Select(p=>{if(p.Length!=5||p.Any(v=>!double.IsFinite(v.Real)||v.Imag!=0))throw new InvalidDataException("native_value_shape_or_finite");return p[0].Real;}).ToArray();
  if(axis[0]!=0||axis[^1]!=.001||axis.Zip(axis.Skip(1)).Any(p=>p.First>=p.Second))throw new InvalidDataException("native_axis_order_or_bounds");
  var matches=ImmutableArray.CreateBuilder<Match>();var last=-1;
  foreach(var sample in engine.Samples){var t=sample.TimeS;var index=Array.BinarySearch(axis,t);if(index<0){var right=~index;index=right==axis.Length?right-1:right==0?0:Math.Abs(axis[right]-t)<Math.Abs(axis[right-1]-t)?right:right-1;}
   var gap=Math.Abs(axis[index]-t);var ulp=t==0?0:Math.Abs(Math.BitIncrement(t)-t);if(index<=last||gap>32*ulp)throw new InvalidDataException("native_requested_row_gap");matches.Add(new(t,axis[index],index,t==0?0:gap/ulp,gap==0));last=index;
  }
  var columns=ImmutableArray.CreateBuilder<ColumnResult>();
  foreach(var column in Columns){var ei=Array.FindIndex(engine.Signals,s=>s.Id==column.Engine);var indices=Enumerable.Range(0,raw.Variables.Length).Where(i=>raw.Variables[i].Name==column.Native&&raw.Variables[i].Dimension==column.Quantity).ToArray();if(indices.Length!=1)throw new InvalidDataException("native_observable_identity_or_unit");var ni=indices[0];var failed=0;var maximum=0d;
   for(var i=0;i<matches.Count;i++){var expected=engine.Samples[i].Values[ei];var actual=raw.Points[matches[i].NativeIndex][ni].Real;var error=Math.Abs(expected-actual);maximum=Math.Max(maximum,error);if(!double.IsFinite(error)||error>(column.Unit=="V"?1e-6:1e-9)+1e-4*Math.Max(Math.Abs(expected),Math.Abs(actual)))failed++;}columns.Add(new(column.Engine,column.Native,column.Unit,matches.Count,failed,maximum));
  }
  return new(columns.All(c=>c.Failed==0),engine.Samples.Length,raw.Points.Length,raw.Points.Length-matches.Count,32,matches.Max(m=>m.UlpDistance),matches.ToImmutable(),columns.ToImmutable());
 }
 private static void ValidateEngine(ResultManifest e){
  if(e.SchemaVersion!="1"||e.Status!="succeeded"||!e.Complete||e.Signals.Length!=4||e.Samples.Length!=101||e.AcSamples.Length!=0||Columns.Any(c=>e.Signals.Count(s=>s.Id==c.Engine&&s.Quantity==c.Quantity&&s.Unit==c.Unit)!=1))throw new InvalidDataException("retained_manifest_identity_or_budget");
  if(e.Samples[0].TimeS!=0||e.Samples[^1].TimeS!=.001||e.Samples.Any(s=>!double.IsFinite(s.TimeS)||s.Values.Length!=4||s.Values.Any(v=>!double.IsFinite(v)))||e.Samples.Zip(e.Samples.Skip(1)).Any(p=>p.First.TimeS>=p.Second.TimeS))throw new InvalidDataException("retained_manifest_axis_or_values");
 }
 private static string Hash(string path)=>HashBytes(File.ReadAllBytes(path));
 private static string HashBytes(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 private static async Task Save(string folder,string name,object value){var path=Path.Combine(folder,name);await File.WriteAllTextAsync(path+".tmp",JsonSerializer.Serialize(value,Json));File.Move(path+".tmp",path,true);}
}
