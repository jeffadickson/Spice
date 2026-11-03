#:property PublishAot=false
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
// Owned, finite native witnesses for literal command spelling. Original input
// programs are never accepted here; each command and output name is fixed below.
if(args.Length!=2||!File.Exists(args[0]))throw new ArgumentException("Expected existing ngspice and fresh output path.");
var output=Path.GetFullPath(args[1]);var parent=Path.GetDirectoryName(output)!;
if(!Directory.Exists(parent)||Directory.Exists(output))throw new IOException("Existing parent and fresh output required.");
Directory.CreateDirectory(output);
var fixtures=new[]{("embedded-variable","V$curplot a 0 0\n","dc V$curplot 1 0 -1"),("separated-sign","V1 a 0 0\n","dc V1 1 0 - 1"),("attached-sign","V1 a 0 0\n","dc V1 1 0 -1"),("adjacent-start-stop","V1 a 0 0\n","dc V1 1-1 -1"),("adjacent-stop-step","V1 a 0 0\n","dc V1 1 -1-1"),("adjacent-all","V1 a 0 0\n","dc V1 1-1-1"),("signed-flag","V1 a 0 0\n","set -noaskquit\nop"),("signed-setting","V1 a 0 0\n","set filetype=-ascii\nop")};
var results=new List<object>();
foreach(var (name,source,command) in fixtures){
 var folder=Path.Combine(output,name);Directory.CreateDirectory(folder);
 var input=Path.Combine(folder,"input.cir");var text="owned control admission\n"+source+"R1 a 0 1k\n.control\n"+command+"\nwrite native.raw all\nquit\n.endc\n.end\n";
 await File.WriteAllTextAsync(input,text);
 var start=new ProcessStartInfo(Path.GetFullPath(args[0])){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=folder};
 foreach(var argument in new[]{"-n","-D","filetype=ascii","-b",input})start.ArgumentList.Add(argument);
 using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(30));
 try{await child.WaitForExitAsync(limit.Token);}catch{if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();throw;}
 var log=await stdout;var error=await stderr;await File.WriteAllTextAsync(Path.Combine(folder,"stdout.log"),log);await File.WriteAllTextAsync(Path.Combine(folder,"stderr.log"),error);
 var raw=Path.Combine(folder,"native.raw");results.Add(new{name,command,exit=child.ExitCode,raw_exists=File.Exists(raw),raw_sha256=File.Exists(raw)?Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(raw))):null,stdout=log,stderr=error});
}
await File.WriteAllTextAsync(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{native_sha256=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(args[0]))),fixtures=results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(Path.Combine(output,"summary.json"));
