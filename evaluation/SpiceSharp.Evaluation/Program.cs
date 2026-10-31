using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpiceSharp.Simulations;
using SpiceSharpParser;
using SpiceSharpParser.Common;

// An isolated assessment harness, not a new simulator or a production adapter.
// Corpus parsing disables every preprocessor and never invokes the reader.
if(args.Length==2 && args[0] is "--parse" or "--run")
{
    var path=args[1];
    Console.WriteLine(JsonSerializer.Serialize(Probe(path,args[0]=="--run")));
    return;
}
if(args is not [var inventoryPath,var outputPath] || !File.Exists(inventoryPath))
    throw new ArgumentException("Usage: <existing corpus inventory JSON> <output JSON>; or --parse/--run <owned file>");
var outputParent=Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
if(!Directory.Exists(outputParent))throw new DirectoryNotFoundException(outputParent);
using var inventory=JsonDocument.Parse(File.ReadAllText(inventoryPath));
var corpusRoot=Path.GetFullPath(inventory.RootElement.GetProperty("corpus_root").GetString()!);
if(!Directory.Exists(corpusRoot))throw new DirectoryNotFoundException(corpusRoot);
var raw=Path.Combine(outputParent,"spicesharp-evaluation-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
Directory.CreateDirectory(raw);
var watch=Stopwatch.StartNew();
var corpus=new List<object>();
foreach(var row in inventory.RootElement.GetProperty("rows").EnumerateArray())
{
    var relative=row.GetProperty("path").GetString()!;
    var path=Path.GetFullPath(relative,corpusRoot);
    if(!path.StartsWith(Path.TrimEndingDirectorySeparator(corpusRoot)+Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidDataException("Corpus path escaped root");
    var result=watch.Elapsed.TotalMinutes<8?await Child("--parse",path,TimeSpan.FromSeconds(3)):new ProbeResult("budget_unattempted",[],[]);
    corpus.Add(new{path=relative,sha256=File.Exists(path)?Hash(path):null,result});
}
var cases=new List<object>();
foreach(var test in Cases.All())
{
    var path=Path.Combine(raw,test.Id+".cir");File.WriteAllText(path,test.Deck,new UTF8Encoding(false));
    var result=await Child("--run",path,TimeSpan.FromSeconds(15));
    var checks=new List<object>();var pass=result.Status==test.ExpectedStatus;
    if(test.ExpectedStatus=="executed")
    {
        pass &= result.Analyses.Length==test.Expected.Length;
        for(var i=0;i<test.Expected.Length;i++)foreach(var expected in test.Expected[i])
        {
            double? actual=i<result.Analyses.Length && result.Analyses[i].Samples.LastOrDefault()?.Values.TryGetValue(expected.Signal,out var v)==true?v:null;
            var ok=actual.HasValue && Within(actual.Value,expected.Value,expected.Absolute,expected.Relative);
            checks.Add(new{analysis=i,expected.Signal,expected.Value,expected.Absolute,expected.Relative,actual,passed=ok});pass &= ok;
        }
    }
    cases.Add(new{id=test.Id,sha256=Hash(path),passed=pass,result,checks});
    File.WriteAllText(outputPath,Report());
}
File.WriteAllText(outputPath,Report());
Console.WriteLine($"Recorded {corpus.Count} parser-only decks and {cases.Count} owned cases in {outputPath}");
string Report()=>JsonSerializer.Serialize(new{
    engine="SpiceSharp 3.2.3",parser="SpiceSharp-Parser 3.4.1",runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,inventory_sha256=Hash(inventoryPath),
    source_sha256=Hash(Path.Combine(AppContext.BaseDirectory,"SpiceSharp.Evaluation.dll")),elapsed_seconds=watch.Elapsed.TotalSeconds,
    raw_directory=raw,corpus,cases,
    limitations=new[]{"Corpus is structural parser acceptance with all processors disabled; no includes, elaboration, numerical execution or proof of ngspice conformance.",
        "Owned case numerical checks cover named points only; timeouts and unattempted decks are not passes.",
        "Windows/.NET managed execution only; no Linux, GPU, NativeAOT, large-circuit performance or upstream internal coverage/mutation qualification."}
},new JsonSerializerOptions{WriteIndented=true});

static ProbeResult Probe(string path,bool execute)
{
    try
    {
        if(!File.Exists(path)||new FileInfo(path).Length>1_048_576 || (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
            return new("input_unavailable",[],[]);
        var text=new UTF8Encoding(false,true).GetString(File.ReadAllBytes(path));
        var parser=new SpiceNetlistParser();
        if(!execute)parser.Processors.Clear();
        var parsed=parser.ParseNetlist(text,path);
        if(parsed.ValidationResult.HasError||parsed.FinalModel is null)
            return new("input_rejected",parsed.ValidationResult.Errors.Select(e=>e.Message).ToArray(),[]);
        if(!execute)return new("parsed",parsed.ValidationResult.Warnings.Select(e=>e.Message).ToArray(),[]);
        var model=new SpiceSharpReader().Read(parsed.FinalModel);
        if(model.ValidationResult.HasError)return new("input_rejected",model.ValidationResult.Errors.Select(e=>e.Message).ToArray(),[]);
        var analyses=new List<AnalysisResult>();
        foreach(var simulation in model.Simulations)
        {
            var samples=new List<Sample>();
            simulation.EventExportData+=(_,_)=>{
                if(samples.Count>=20000)throw new InvalidDataException("Owned sample budget exceeded");
                var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
                foreach(var export in model.Exports.Where(e=>ReferenceEquals(e.Simulation,simulation)))
                {
                    var value=export.Extract();if(!double.IsFinite(value))throw new InvalidDataException("Nonfinite output");
                    values[export.Name]=value;
                }
                if(simulation is IStateful<IIntegrationMethod> time)values["$time"]=time.State.Time;
                samples.Add(new(values));
            };
            simulation.Execute(model.Circuit);
            analyses.Add(new(simulation.GetType().Name,samples.ToArray()));
        }
        return new(analyses.Count>0?"executed":"no_analysis",model.ValidationResult.Warnings.Select(e=>e.Message).ToArray(),analyses.ToArray());
    }
    catch(DecoderFallbackException e){return new("encoding_rejected",[e.Message],[]);}
    catch(Exception e){return new("exception",[e.GetType().FullName+": "+e.Message],[]);}
}

static async Task<ProbeResult> Child(string mode,string path,TimeSpan timeout)
{
    var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add(mode);start.ArgumentList.Add(path);
    using var child=Process.Start(start)??throw new IOException("Worker did not start");
    var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
    using var deadline=new CancellationTokenSource(timeout);
    try{await child.WaitForExitAsync(deadline.Token);}
    catch(OperationCanceledException){child.Kill(entireProcessTree:true);await child.WaitForExitAsync();return new("timeout",[],[]);}
    var output=await stdout;var error=await stderr;
    try{return child.ExitCode==0?JsonSerializer.Deserialize<ProbeResult>(output)??new("worker_failed",["Empty result"],[]):new("worker_failed",[error],[]);}
    catch(JsonException){return new("worker_failed",[error+output[..Math.Min(output.Length,512)]],[]);}
}

static bool Within(double actual,double expected,double absolute,double relative)=>
    double.IsFinite(actual)&&Math.Abs(actual-expected)<=absolute+relative*Math.Abs(expected);
static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

internal sealed record ProbeResult(string Status,string[] Diagnostics,AnalysisResult[] Analyses);
internal sealed record AnalysisResult(string Kind,Sample[] Samples);
internal sealed record Sample(Dictionary<string,double> Values);
internal sealed record Expected(string Signal,double Value,double Absolute=1e-8,double Relative=0);
internal sealed record OwnedCase(string Id,string Deck,Expected[][] Expected,string ExpectedStatus="executed");
