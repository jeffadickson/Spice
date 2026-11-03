#:property PublishAot=false
#:project ../dotnet/Simulator.RequestedQualification/Simulator.RequestedQualification.csproj
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.RequestedQualification;

// Bounded oracle experiment, not a production projection or a waived numerical
// comparison. An isolated auxiliary PWL source tests whether native breakpoints
// can sample requested times without changing any original circuit equation.
if(args.Length!=2||!File.Exists(args[0]))throw new ArgumentException("Expected native executable and fresh output directory.");
var output=Path.GetFullPath(args[1]);
if(!Directory.Exists(Path.GetDirectoryName(output)))throw new DirectoryNotFoundException();
if(Directory.Exists(output)||File.Exists(output))throw new IOException("Fresh output required.");
Directory.CreateDirectory(output);
var times=Enumerable.Range(0,201).Select(i=>i*1e-5).ToArray();var rows=new List<object>();var qualified=true;
foreach(var clock in new[]{false,true}){
    var prefix=clock?"clock":"baseline";var input=Path.Combine(output,prefix+".cir");
    var source="Independent sine grid probe\nV1 out 0 SIN(0 1 1k)\nR1 out 0 1k\n";
    if(clock)source+="V__grid __grid 0 PWL("+string.Join(" ",times.Select((t,i)=>t.ToString("R",CultureInfo.InvariantCulture)+" "+(i%2)))+")\n";
    source+=".tran 10u 2m\n.end\n";await File.WriteAllTextAsync(input,source);
    var rawPath=Path.Combine(output,prefix+".raw");
    var start=new ProcessStartInfo(Path.GetFullPath(args[0])){WorkingDirectory=output,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var argument in new[]{"-n","-D","filetype=ascii","-b","-r",rawPath,"-o",Path.Combine(output,prefix+".log"),input})start.ArgumentList.Add(argument);
    using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
    using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try{await child.WaitForExitAsync(limit.Token);}catch{if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();throw;}
    await File.WriteAllTextAsync(Path.Combine(output,prefix+".stdout"),await stdout);await File.WriteAllTextAsync(Path.Combine(output,prefix+".stderr"),await stderr);
    if(child.ExitCode!=0)throw new InvalidDataException("Native probe failed: "+prefix);
    var raw=AsciiRaw.Read(await File.ReadAllTextAsync(rawPath));var variables=raw.Variables.ToArray();
    var timeColumn=Array.FindIndex(variables,v=>v.Name=="time");var voltageColumn=Array.FindIndex(variables,v=>v.Name=="v(out)");
    if(timeColumn<0||voltageColumn<0)throw new InvalidDataException("Native column missing.");
    var nativeTimes=raw.Points.Select(p=>p[timeColumn].Real).ToArray();var nativeValues=raw.Points.Select(p=>p[voltageColumn].Real).ToArray();
    var misses=0;double maximum=0;
    foreach(var t in times){
        var nearest=Array.BinarySearch(nativeTimes,t);double value;
        if(nearest>=0)value=nativeValues[nearest];
        else{
            var upper=~nearest;
            if(upper>0&&Math.Abs(nativeTimes[upper-1]-t)<=1e-12*Math.Abs(t))value=nativeValues[upper-1];
            else if(upper<nativeTimes.Length&&Math.Abs(nativeTimes[upper]-t)<=1e-12*Math.Abs(t))value=nativeValues[upper];
            else{
                misses++;if(upper==0||upper==nativeTimes.Length)throw new InvalidDataException("Native grid does not bracket request.");
                value=nativeValues[upper-1]+(nativeValues[upper]-nativeValues[upper-1])*(t-nativeTimes[upper-1])/(nativeTimes[upper]-nativeTimes[upper-1]);
            }
        }
        maximum=Math.Max(maximum,Math.Abs(value-Math.Sin(2*Math.PI*1000*t)));
    }
    rows.Add(new{clock,native_points=nativeTimes.Length,requested_points=times.Length,missing_exact_points=misses,maximum_sine_error_volts=maximum,input_sha256=Hash(input),raw_sha256=Hash(rawPath)});
    if(clock)qualified=misses==0&&maximum<1e-12;
}
await File.WriteAllTextAsync(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(new{state=qualified?"finite_probe_passed":"finite_probe_failed",native_sha256=Hash(args[0]),rows,
    limits=new[]{"Single analytical memoryless sine fixture; not arbitrary waveform/model or production-oracle qualification.","Any future projection must prove auxiliary-node isolation, preserve original analysis settings, retain instrumentation and reject unavailable exact-grid coverage."}},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(output,"probe.json")));
return qualified?0:1;
static string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
