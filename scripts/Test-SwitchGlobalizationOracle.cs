#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Simulator.Client;

if(args is ["--self-test"]){OracleNumber.CheckContracts();return;}
if(args.Length!=3)throw new ArgumentException("Expected engine, ngspice and fresh output directory.");
var engine=Path.GetFullPath(args[0]);var native=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
foreach(var file in new[]{engine,native})if(!File.Exists(file))throw new FileNotFoundException(file);
if(!Directory.Exists(Path.GetDirectoryName(output))||Directory.Exists(output)||File.Exists(output))throw new IOException("Existing parent and fresh output required.");
Directory.CreateDirectory(output);
using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
var hashes=new Dictionary<string,string>{{engine,Hash(engine)},{native,Hash(native)}};
// Freeze the selected new engine once so both providers exercise identical bits.
var frozen=Path.Combine(output,"circuit-simulator"+(OperatingSystem.IsWindows()?".exe":""));File.Copy(engine,frozen);
if(Hash(frozen)!=hashes[engine]||Hash(engine)!=hashes[engine])throw new IOException("Engine changed during snapshot.");
var version=await RunNative(["--version"],"ngspice-version");
if(!version.Contains("ngspice-47+",StringComparison.Ordinal))throw new InvalidDataException("Expected the declared ngspice47+ oracle.");
const double drive=1.5822841689813791,load=100,ron=10,roff=10000;
var deck=Path.Combine(output,"original-vswitch.cir");
File.WriteAllText(deck,FormattableString.Invariant($"""
VSWITCH reversed-threshold fixed Newton counterexample
VDRIVE in 0 {drive:R}
VCONTROL control 0 0.5
S1 in out control 0 sw
.model sw VSWITCH(RON=10 ROFF=10000 VON=0 VOFF=1)
RLOAD out 0 100
.control
set numdgt=17
op
let target=v(out)
let branch=i(vdrive)
print target branch
quit
.endc
.end

"""));
var oracle=await RunNative(["-n","-D","ngbehavior=psa","-b",deck],"ngspice");
var nativeVoltage=OracleNumber.Read(oracle,"target");var nativeCurrent=OracleNumber.Read(oracle,"branch");
// At the exact midpoint the smooth logarithmic model has geometric-mean
// resistance. A passive divider and KCL independently determine volts/amps.
var midpointResistance=Math.Sqrt(ron*roff);var expectedVoltage=drive*load/(midpointResistance+load);var expectedCurrent=-expectedVoltage/load;
var rows=new List<object>();var allPassed=Close(nativeVoltage,expectedVoltage,1e-9)&&Close(nativeCurrent,expectedCurrent,1e-12);
foreach(var backend in new[]{"lu","faer"}) {
    var request=new SimulationRequest("switch-globalization-"+backend,new Circuit(["0","in","out","control"],
        [new Voltage("VDRIVE",1,0,drive),new Voltage("VCONTROL",3,0,.5),new PspiceSwitch("S1",1,2,3,0,ron,roff,0,1),new Resistor("RLOAD",2,0,load)]),new OperatingPoint(),backend);
    File.WriteAllText(Path.Combine(output,backend+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
    try {
        var response=await new SimulationClient(frozen).RunAsync(request,deadline.Token);
        File.WriteAllText(Path.Combine(output,backend+".response.json"),response.Payload.GetRawText());
        if(!response.Succeeded)throw new InvalidDataException("Engine refused: "+response.Error?.Code+": "+response.Error?.Message);
        var manifest=response.ReadManifest();var provider=backend=="lu"?"nalgebra_dense_lu_reference":"faer_sparse_lu";
        if(manifest.CaseId!=request.CaseId||manifest.Backend.Id!=provider||manifest.Backend.Gpu)throw new InvalidDataException("Provider/case identity differs.");
        var voltage=ReadValue(manifest,"v(out)","V");var current=ReadValue(manifest,"i(VDRIVE)","A");
        var passed=Close(voltage,nativeVoltage,1e-9)&&Close(current,nativeCurrent,1e-12)&&Close(voltage,expectedVoltage,1e-9)&&Close(current,expectedCurrent,1e-12);
        allPassed&=passed;rows.Add(new{backend,status="executed",voltage_v=voltage,source_current_a=current,passed,newton=manifest.Newton});
    } catch(Exception error) {allPassed=false;rows.Add(new{backend,status="failed",error=error.ToString()});}
}
foreach(var pair in hashes)if(Hash(pair.Key)!=pair.Value)throw new IOException("Oracle input changed during execution.");
if(Hash(frozen)!=hashes[engine])throw new IOException("Frozen engine changed during execution.");
var json=new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{status=allPassed?"passed":"failed",input_hashes=hashes,
    deck_sha256=Hash(deck),helper_assembly_sha256=Hash(System.Reflection.Assembly.GetExecutingAssembly().Location),
    analytical=new{midpoint_resistance_ohms=midpointResistance,voltage_v=expectedVoltage,source_current_a=expectedCurrent},
    ngspice=new{voltage_v=nativeVoltage,source_current_a=nativeCurrent},rows,
    bounds=new{voltage_absolute_v=1e-9,current_absolute_a=1e-12,relative=1e-8},
    limits="One fixed original VSWITCH OP plus typed public requests; no parser, corpus23/9, waveform, whole-model or production qualification."},json));
Console.WriteLine(Path.Combine(output,"summary.json"));Environment.ExitCode=allPassed?0:1;

static bool Close(double actual,double expected,double absolute)=>double.IsFinite(actual)&&double.IsFinite(expected)&&Math.Abs(actual-expected)<=absolute+1e-8*Math.Abs(expected);
static double ReadValue(ResultManifest manifest,string id,string unit) {
    var matches=manifest.Signals.Select((signal,index)=>(signal,index)).Where(p=>p.signal.Id==id).ToArray();
    if(matches.Length!=1||matches[0].signal.Unit!=unit||manifest.Samples.Length!=1)throw new InvalidDataException("Missing/duplicate/wrong-unit OP observation: "+id);
    var value=manifest.Samples.Single().Values[matches[0].index];
    return double.IsFinite(value)?value:throw new InvalidDataException("Nonfinite OP observation: "+id);
}
static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexStringLower(SHA256.HashData(stream));}
async Task<string> RunNative(string[] arguments,string tag) {
    deadline.Token.ThrowIfCancellationRequested();
    var start=new ProcessStartInfo(native){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var argument in arguments)start.ArgumentList.Add(argument);start.Environment["LC_ALL"]="C";
    using var process=Process.Start(start)??throw new IOException("Oracle did not start.");
    var stdout=process.StandardOutput.ReadToEndAsync(deadline.Token);var stderr=process.StandardError.ReadToEndAsync(deadline.Token);
    try {
        await process.WaitForExitAsync(deadline.Token);await Task.WhenAll(stdout,stderr);
        File.WriteAllText(Path.Combine(output,tag+".stdout.log"),stdout.Result);File.WriteAllText(Path.Combine(output,tag+".stderr.log"),stderr.Result);
        var text=stdout.Result+"\n"+stderr.Result;
        if(process.ExitCode!=0||Regex.IsMatch(text,@"(?im)^\s*(?:error\b|fatal\b|syntax error\b)"))throw new InvalidDataException("Native oracle failed; inspect raw logs.");
        return text;
    } finally {if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));}}
}

/// Strict scalar extraction for these two named ngspice OP observations only.
static class OracleNumber {
    public static double Read(string text,string name) {
        // Count all assignments first: a malformed duplicate must not disappear
        // merely because it fails the subsequent finite-number parse.
        var matches=Regex.Matches(text,@"(?m)^[ \t]*"+Regex.Escape(name)+@"[ \t]*=[ \t]*([^\r\n]*)\r?$");
        if(matches.Count!=1||!double.TryParse(matches[0].Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value))
            throw new InvalidDataException("Missing, duplicate or nonfinite oracle scalar: "+name);
        return value;
    }
    public static void CheckContracts() {
        if(Read("target = 3.801486345140583e-1\n","target")!=.3801486345140583||Read("target = 0\n","target")!=0)
            throw new InvalidDataException("Valid scalar parse failed.");
        foreach(var invalid in new[]{"","branch = 1\n","target = NaN\n","target = Infinity\n","target = 1e999\n","target = 1 junk\n","target = 1\ntarget = 2\n","target = 1,2\n"}) {
            try{Read(invalid,"target");throw new InvalidOperationException("Malformed scalar accepted.");}
            catch(InvalidDataException){ }
        }
        Console.WriteLine("Strict oracle scalar contracts passed: 2 valid, 8 malformed/missing/duplicate cases.");
    }
}
