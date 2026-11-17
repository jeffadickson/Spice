#:property PublishAot=false
#:project ../dotnet/Simulator.Frontend/Simulator.Frontend.csproj
// ANTLR frontend trimming/AOT is not qualified. Compile incrementally once;
// this helper measures a warmed public .NET workflow, not helper startup.
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;

if(args.Length is <5 or >7)throw new ArgumentException("Expected corpus root, frozen inventory, engine, fresh output JSON, repetitions (1..5), optional warmups (0..20), optional recognition mode (reuse/fresh).");
var corpus=Path.GetFullPath(args[0]);var inventory=Path.GetFullPath(args[1]);var engine=Path.GetFullPath(args[2]);var output=Path.GetFullPath(args[3]);
var repetitions=int.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture);
if(repetitions is <1 or >5)throw new ArgumentOutOfRangeException(nameof(repetitions));
var warmupCount=args.Length>5?int.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture):3;
if(warmupCount is <0 or >20)throw new ArgumentOutOfRangeException(nameof(warmupCount));
var recognitionMode=args.Length>6?args[6]:"reuse";
if(recognitionMode is not ("reuse" or "fresh"))throw new ArgumentException("Recognition mode must be reuse or fresh.");
foreach(var dir in new[]{corpus,Path.GetDirectoryName(output)!})if(!Directory.Exists(dir))throw new DirectoryNotFoundException(dir);
foreach(var file in new[]{inventory,engine})if(!File.Exists(file))throw new FileNotFoundException(file);
if(File.Exists(output)||Directory.Exists(output))throw new IOException("Fresh output required.");
using var document=JsonDocument.Parse(await File.ReadAllTextAsync(inventory));
var entries=document.RootElement.GetProperty("rows").EnumerateArray().ToArray();
if(entries.Length is <1 or >32)throw new InvalidDataException("Bounded 1..32 input inventory required.");
var rows=new List<object>();long syntheticPreparations=0;long deckPreparations=0;
using var listener=new MeterListener();
listener.InstrumentPublished=(instrument,sink)=>{
    if(instrument.Meter.Name=="Simulator.Syntax.Parameters"&&instrument.Name=="parameter.synthetic_expression_preparations")sink.EnableMeasurementEvents(instrument);
    if(instrument.Meter.Name=="Simulator.Syntax.Recognition"&&instrument.Name=="syntax.deck_preparations")sink.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<long>((instrument,value,_,_)=>{
    if(instrument.Name=="syntax.deck_preparations")Interlocked.Add(ref deckPreparations,value);
    else Interlocked.Add(ref syntheticPreparations,value);
});
listener.Start();
foreach(var entry in entries){
    if(entry.GetProperty("scope").GetString()!="ngspice")throw new InvalidDataException("This benchmark expects original corpus inputs.");
    var relative=entry.GetProperty("path").GetString()!;var path=Path.GetFullPath(relative,corpus);
    var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
    if(!path.StartsWith(Path.TrimEndingDirectorySeparator(corpus)+Path.DirectorySeparatorChar,comparison))throw new InvalidDataException("Corpus path escape.");
    var expected=entry.GetProperty("result").GetProperty("Sha256").GetString();
    if(Hash(path)!=expected)throw new InvalidDataException("Original hash changed.");
    var samples=new List<object>();var warmups=new List<object>();
    for(int repetition=-warmupCount;repetition<repetitions;repetition++){
        var preparationsBefore=Interlocked.Read(ref syntheticPreparations);
        var decksBefore=Interlocked.Read(ref deckPreparations);
        var total=Stopwatch.StartNew();var phase=Stopwatch.StartNew();
        var resources=ResourceResolver.Resolve(path,new([corpus]));
        if(!resources.Success)throw new InvalidDataException("Resource resolution failed.");
        var resolve=phase.Elapsed.TotalMilliseconds;phase.Restart();
        var syntax=recognitionMode=="reuse"?resources.Parse(collectDeclarations:true):DeckParser.Parse(resources.Text!,collectDeclarations:true);
        var parse=phase.Elapsed.TotalMilliseconds;phase.Restart();
        var plan=RequestedAnalysis.Prepare(syntax,maxPoints:100000,backend:"faer");
        if(!plan.Success)throw new InvalidDataException(string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var prepare=phase.Elapsed.TotalMilliseconds;phase.Restart();
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var results=await RequestedAnalysis.ExecuteAsync(plan,new SimulationClient(engine),cancellation.Token);
        var execute=phase.Elapsed.TotalMilliseconds;total.Stop();
        if(results.Length!=plan.Operations.Length||results.Any(r=>!r.Success))throw new InvalidDataException("Incomplete original analysis execution.");
        var sample=new{resolve_ms=resolve,parse_ms=parse,prepare_ms=prepare,execute_ms=execute,total_ms=total.Elapsed.TotalMilliseconds,operations=results.Length,
            synthetic_expression_preparations=Interlocked.Read(ref syntheticPreparations)-preparationsBefore,
            complete_deck_preparations=Interlocked.Read(ref deckPreparations)-decksBefore};
        if(repetition<0)warmups.Add(sample);else samples.Add(sample);
    }
    rows.Add(new{path=relative,input_sha256=expected,warmups,samples});
}
await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{
    state="completed",repetitions,warmups_per_case=warmupCount,recognition_mode=recognitionMode,engine_sha256=Hash(engine),inventory_sha256=Hash(inventory),
    frontend_sha256=Hash(typeof(RequestedAnalysis).Assembly.Location),runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    scope="Public .NET resource resolution + generated parsing + requested preparation + native child execution/reporting. Recorded unmeasured warmups, then samples; no guarantee of fully steady tiered-JIT state. Fresh mode forces duplicate resolved-text recognition as a same-build baseline. Helper compilation/startup and independent oracle comparison excluded. Not equivalent to cold CLI startup or prepared-native-only timing.",rows
},new JsonSerializerOptions{WriteIndented=true}));
static string Hash(string file)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
