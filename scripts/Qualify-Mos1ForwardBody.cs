#:property PublishAot=false
#:project ../dotnet/Simulator.RequestedQualification/Simulator.RequestedQualification.csproj
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;
using Simulator.RequestedQualification;

// Channel-isolation fixtures suppress junction conduction in the independent
// reference. They qualify the channel/body convention, not a complete foundry model.
if(args.Length!=3)throw new ArgumentException("Pass existing engine, ngspice, and a fresh E: output directory.");
var engine=Path.GetFullPath(args[0]);var oracle=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);
foreach(var file in new[]{engine,oracle})if(!File.Exists(file))throw new FileNotFoundException(file);
if(!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException("Missing output parent.");
if(Directory.Exists(output)||File.Exists(output))throw new IOException("Output must be fresh.");
Directory.CreateDirectory(output);
var root=Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var hashes=new[]{"src/mos1.rs","src/newton.rs","src/api.rs","src/audit.rs","src/main.rs","tests/mos1_channel.rs","scripts/Qualify-Mos1ForwardBody.cs"}
    .Select(p=>Path.Combine(root,p)).Concat([engine,oracle]).ToDictionary(p=>p,Hash,StringComparer.OrdinalIgnoreCase);
var client=new SimulationClient(engine);
var rows=new List<Check>();
var cases=new List<Fixture>();
foreach(var p in new[]{false,true}){
    foreach(var (name,d,b) in new[]{("linear",.5,.1),("saturation",2.0,.2),("inverse",-.5,.1),("clamped",.5,1.3)})
        foreach(var mode in new[]{"op","ac"})cases.Add(new(name,p,d,b,mode,false));
    cases.Add(new("meyer",p,.5,.1,"ac",true));
    cases.Add(new("meyer-inverse",p,-.5,.1,"ac",true));
    cases.Add(new("body-ramp",p,.5,-.2,"tran",false));
}
foreach(var fixture in cases){
    var folder=Path.Combine(output,(fixture.P?"p-":"n-")+fixture.Name+"-"+fixture.Mode);
    Directory.CreateDirectory(folder);
    var sign=fixture.P?-1.0:1.0;
    var drain=sign*fixture.D;var gate=sign*1.5;var bulk=sign*fixture.B;
    var cox=fixture.Meyer?3.453133246992e-16:0.0;
    var model=new Mos1Model(fixture.P,sign*.7,2e-4,.4,.6,.03,cox);
    var circuit=new Circuit(["0","d","g","b"],[new Voltage("VD",1,0,drain),new Voltage("VG",2,0,gate),
        new Voltage("VB",3,0,bulk),new Mos1Channel("M",1,0,2,3,model)]);
    Analysis analysis=fixture.Mode switch{
        "op"=>new OperatingPoint(),
        "ac"=>new Ac([fixture.Meyer?1e6:1],[new("VD",0,0),new("VG",fixture.Meyer?1:0,0),new("VB",fixture.Meyer?0:1,0)]),
        _=>new TransientConfigured(new(10e-9,.5e-9,Sources:[new("VB",new Pwl([
            new(0,-.2*sign),new(1e-9,-.2*sign),new(3e-9,.2*sign),new(5e-9,.2*sign),new(7e-9,-.2*sign),new(10e-9,-.2*sign)]))]))};
    string F(double x)=>x.ToString("G17",CultureInfo.InvariantCulture);
    var bodyWave=fixture.Mode=="tran"?$" PWL(0 {F(-.2*sign)} 1n {F(-.2*sign)} 3n {F(.2*sign)} 5n {F(.2*sign)} 7n {F(-.2*sign)} 10n {F(-.2*sign)})":"";
    var command=fixture.Mode switch{"op"=>"op","ac"=>$"ac lin 1 {(fixture.Meyer?"1Meg":"1")} {(fixture.Meyer?"1Meg":"1")}",_=>"tran .5n 10n 0 .02n"};
    var text=$"Forward body channel isolation\n.options gmin=0 reltol=1e-10 abstol=1e-15 vntol=1e-12\n"+
        $"VD d 0 {F(drain)} AC 0\nVG g 0 {F(gate)} AC {(fixture.Meyer?1:0)}\nVB b 0 {F(bulk)} AC {(fixture.Meyer?0:1)}{bodyWave}\n"+
        $"M d g 0 b MOD W=1u L=1u\n.model MOD {(fixture.P?"pmos":"nmos")} (level=1 vto={F(sign*.7)} kp=2e-4 gamma=.4 phi=.6 lambda=.03 is=1e-300 tox={(fixture.Meyer?"100n":"1m")})\n"+
        $".control\n{command}\nwrite native.raw all\nquit\n.endc\n.end\n";
    var deck=Path.Combine(folder,"reference.cir");await File.WriteAllTextAsync(deck,text);
    await RunOracle(deck,folder);
    var native=AsciiRaw.Read(await File.ReadAllTextAsync(Path.Combine(folder,"native.raw")));
    var frequency=fixture.Meyer?1e6:1;
    if(fixture.Mode=="ac" && (!native.Complex||native.PlotName!="AC Analysis"||native.Points.Length!=1||native.Points[0][0].Real!=frequency))
        throw new InvalidDataException("Wrong independent AC identity/grid.");
    if(fixture.Mode=="op" && (native.Complex||native.PlotName!="Operating Point"||native.Points.Length!=1))
        throw new InvalidDataException("Wrong independent DC identity.");
    if(fixture.Mode=="tran" && (native.Complex||native.PlotName!="Transient Analysis"||
        native.Points.Zip(native.Points.Skip(1)).Any(p=>p.Second[0].Real<=p.First[0].Real)))
        throw new InvalidDataException("Wrong independent transient grid.");
    foreach(var backend in new[]{"lu","faer"}){
        var request=new SimulationRequest(Path.GetFileName(folder)+"-"+backend,circuit,analysis,backend);
        await File.WriteAllTextAsync(Path.Combine(folder,backend+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var response=await client.RunAsync(request,deadline.Token);
        await File.WriteAllTextAsync(Path.Combine(folder,backend+".response.json"),response.Payload.GetRawText());
        if(!response.Succeeded){rows.Add(new(request.CaseId,backend,fixture.Mode,"engine_failed",0,null,false));continue;}
        var manifest=response.ReadManifest();
        if(manifest.CaseId!=request.CaseId||manifest.Backend.Gpu||manifest.Backend.Placement!="cpu"||
            manifest.Backend.Id!=(backend=="lu"?"nalgebra_dense_lu_reference":"faer_sparse_lu"))
            throw new InvalidDataException("Engine case/provider identity mismatch.");
        if(fixture.Mode=="ac" && (manifest.AcSamples.Length!=1||manifest.AcSamples[0].FrequencyHz!=frequency))
            throw new InvalidDataException("Wrong engine AC grid.");
        foreach(var signal in fixture.Meyer?new[]{"i(VD)","i(VG)"}:new[]{"i(VD)"}){
            var ei=Array.FindIndex(manifest.Signals,s=>s.Id==signal&&s.Unit=="A");
            var ni=Enumerable.Range(0,native.Variables.Length).Single(i=>native.Variables[i].Name.Equals(signal,StringComparison.OrdinalIgnoreCase)&&native.Variables[i].Dimension=="current");
            if(ei<0)throw new InvalidDataException("Missing engine current.");
            var maximum=0.0;var agreed=true;var count=0;
            if(fixture.Mode=="ac"){
                var actual=manifest.AcSamples.Single().Values[ei];var expected=native.Points.Single()[ni];
                Compare(actual.Real,expected.Real,1e-12);Compare(actual.Imag,expected.Imag,1e-12);
            }else foreach(var sample in manifest.Samples){
                var expected=fixture.Mode=="op"?native.Points.Single()[ni].Real:Interpolate(native,ni,sample.TimeS);
                Compare(sample.Values[ei],expected,fixture.Mode=="tran"?1e-9:1e-12);
            }
            rows.Add(new(request.CaseId,backend,fixture.Mode,signal,count,maximum,agreed));
            void Compare(double actual,double expected,double absolute){
                if(!double.IsFinite(actual)||!double.IsFinite(expected))throw new InvalidDataException("Nonfinite comparison.");
                var error=Math.Abs(actual-expected);maximum=Math.Max(maximum,error);count++;
                agreed &= error<=absolute+1e-7*Math.Max(Math.Abs(actual),Math.Abs(expected));
            }
        }
    }
}
var provenanceValid=hashes.All(p=>Hash(p.Key)==p.Value);
await File.WriteAllTextAsync(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{
    scope="MOS1 channel forward-body DC/transient exact derivatives and separate ngspice AC body convention; selected intrinsic Meyer AC",references=cases.Count,
    comparisons=rows.Count,passed=rows.Count(r=>r.Agreed),failed=rows.Count(r=>!r.Agreed),rows,
    provenance_valid=provenanceValid,source_and_binary_sha256=hashes,
    provenance_scope="Stable checkout/source and executable bytes during this campaign; retain external build logs linking the binary to source.",
    tolerances=new{dc_ac_current_absolute=1e-12,transient_current_absolute=1e-9,relative=1e-7,endpoint_seconds=1e-20},
    limitations=new[]{"Channel-isolation references use IS=1e-300; not full packaged-MOS qualification.","No original corpus deck completion is established by these fixtures.","Transient reference interpolation is linear without extrapolation."}},new JsonSerializerOptions{WriteIndented=true}));
return !provenanceValid?3:rows.All(r=>r.Agreed)?0:1;

static string SourcePath([CallerFilePath]string path="")=>path;
static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(input));}

static double Interpolate(NativeRaw raw,int column,double time){
    if(raw.Complex||raw.PlotName!="Transient Analysis")throw new InvalidDataException("Wrong native transient shape.");
    // Independently integrated clocks may differ at an endpoint by a few ulps.
    // Snap only sub-1e-20-second endpoint roundoff; never extend the physical grid.
    if(Math.Abs(raw.Points[0][0].Real-time)<=1e-20)return raw.Points[0][column].Real;
    if(Math.Abs(raw.Points[^1][0].Real-time)<=1e-20)return raw.Points[^1][column].Real;
    var index=0;while(index<raw.Points.Length&&raw.Points[index][0].Real<time)index++;
    if(index<raw.Points.Length&&Math.Abs(raw.Points[index][0].Real-time)<=1e-20)return raw.Points[index][column].Real;
    if(index==0||index==raw.Points.Length)throw new InvalidDataException("Reference extrapolation refused.");
    var left=raw.Points[index-1];var right=raw.Points[index];
    return left[column].Real+(right[column].Real-left[column].Real)*(time-left[0].Real)/(right[0].Real-left[0].Real);
}
async Task RunOracle(string deck,string folder){
    var info=new ProcessStartInfo(oracle){UseShellExecute=false,WorkingDirectory=folder,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    foreach(var arg in new[]{"-n","-D","filetype=ascii","-b",deck})info.ArgumentList.Add(arg);
    using var process=Process.Start(info)??throw new IOException("Cannot start ngspice.");
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try{await process.WaitForExitAsync(deadline.Token);}catch{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw;}
    await File.WriteAllTextAsync(Path.Combine(folder,"ngspice.stdout.log"),await stdout);
    await File.WriteAllTextAsync(Path.Combine(folder,"ngspice.stderr.log"),await stderr);
    if(process.ExitCode!=0)throw new InvalidDataException("ngspice failed.");
}
sealed record Fixture(string Name,bool P,double D,double B,string Mode,bool Meyer);
sealed record Check(string Case,string Backend,string Analysis,string Signal,int Points,double? MaximumAbsoluteError,bool Agreed);
