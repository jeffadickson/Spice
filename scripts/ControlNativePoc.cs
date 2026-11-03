#:property PublishAot=false
#:project ../dotnet/Simulator.RequestedQualification/Simulator.RequestedQualification.csproj
using System.Diagnostics;
using System.Text.Json;
using Simulator.RequestedQualification;
// Owned fixtures qualify native control dispatch/projection before adding a bounded profile.
if(args.Length!=2||!File.Exists(args[0])||!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(args[1]))))throw new ArgumentException("Expected existing ngspice and fresh artifact directory under an existing parent.");
var output=Path.GetFullPath(args[1]);if(Directory.Exists(output))throw new IOException("Fresh output required.");Directory.CreateDirectory(output);
var fixtures=new[]{("direct-op",".tran .1u 1u\n","op"),("direct-ac","","ac dec 1 10 1k"),("direct-dc","","dc V1 1 -1 -.5"),("direct-tran","","tran .1u 1u"),("run-mixed",".tran .1u 1u\n.op\n.dc V1 1 -1 -.5\n.ac lin 3 10 30\n","run"),("run-mixed-reversed",".ac lin 3 10 30\n.dc V1 1 -1 -.5\n.op\n.tran .1u 1u\n","run"),("run-duplicate-ac",".ac lin 3 10 30\n.ac lin 5 10 50\n","run")};var rows=new List<object>();
foreach(var (name,analyses,command) in fixtures){
 var folder=Path.Combine(output,name);Directory.CreateDirectory(folder);var raw=Path.Combine(folder,"native.raw");var wire=raw.Replace('\\','/');if(wire.Any(c=>c is '"' or '\r' or '\n'))throw new InvalidDataException("Unsafe generated output spelling.");
 var text="owned control POC\nV1 a 0 DC 1 AC 1\nR1 a 0 1k\n"+analyses+".control\n"+command+"\nsetplot\nwrite native.raw all\nquit\n.endc\n.end\n";var input=Path.Combine(folder,"input.cir");await File.WriteAllTextAsync(input,text);
 var start=new ProcessStartInfo(Path.GetFullPath(args[0])){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=folder};foreach(var arg in new[]{"-n","-D","filetype=ascii","-b",input})start.ArgumentList.Add(arg);
 using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await child.WaitForExitAsync(deadline.Token);}catch{if(!child.HasExited)child.Kill(true);throw;}var log=await stdout;var error=await stderr;await File.WriteAllTextAsync(Path.Combine(folder,"stdout.log"),log);await File.WriteAllTextAsync(Path.Combine(folder,"stderr.log"),error);
 string status;string? plot=null;try{var result=AsciiRaw.Read(await File.ReadAllTextAsync(raw));status="raw_read";plot=result.PlotName;}catch(Exception e)when(e is IOException or InvalidDataException){status=e.Message;}
 rows.Add(new{name,command,exit=child.ExitCode,status,plot,log,error});
}
await File.WriteAllTextAsync(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(rows.Select(r=>r),new JsonSerializerOptions{WriteIndented=true}));
