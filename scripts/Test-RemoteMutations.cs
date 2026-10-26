#:property PublishAot=false
using System.Diagnostics;
using System.Text.Json;
// Compile failures are invalid, not detected behavioral defects. Working source is untouched.
var root=Directory.GetCurrentDirectory();
var original=File.ReadAllText("scripts/RemoteQualification.cs");
var directory=Path.Combine(root,"work","remote-mutants");Directory.CreateDirectory(directory);
var mutants=new[]{("source-revision","evidence.SourceCommit!=source","false"),("source-hash","Hash(relative)!=hash","false"),
    ("framework-summary-hash","Hash(frameworkPath)!=evidence.FrameworkSummarySha256","false"),
    ("framework-counts","value!=pair.Value","false")};
var results=new List<object>();var killed=0;
foreach(var (name,pattern,replacement) in mutants) {
    if(!original.Contains(pattern))throw new InvalidOperationException("Mutation target missing: "+name);
    var path=Path.Combine(directory,name+".cs");File.WriteAllText(path,original.Replace(pattern,replacement));
    var start=new ProcessStartInfo("pwsh"){WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    // Only publication contracts can detect these guards; avoid unrelated native
    // inventories/setup and launcher checks consuming each mutant's timeout.
    foreach(var arg in new[]{"-NoProfile","-File","scripts/Test-Remote.ps1","-PublicationOnly"})start.ArgumentList.Add(arg);
    start.Environment["SPICE_REMOTE_PUBLISHER"]=path;
    start.Environment["SPICE_REMOTE_GUARD"]=name;
    using var process=Process.Start(start)!;
    var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();var timeout=false;
    using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(4));
    try{await process.WaitForExitAsync(limit.Token);}catch(OperationCanceledException){timeout=true;process.Kill(true);await process.WaitForExitAsync();}
    var log=await output+await errors;File.WriteAllText(Path.Combine(directory,name+".log"),log);
    var status=timeout?"timed_out":log.Contains("The build failed")?"invalid":process.ExitCode==0?"survived":log.Contains("Failed: 0")?"invalid":log.Contains("Tests completed")?"killed":"invalid";
    if(status=="killed")killed++;
    results.Add(new{name,status,exit_code=process.ExitCode});Console.WriteLine(name+": "+status);
}
File.WriteAllText("evidence/remote-mutation.json",JsonSerializer.Serialize(new{strategy="four deliberate provenance/accounting mutations in copied C# scripts; separate from Stryker client mutations",source_sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original))).ToLowerInvariant(),killed,total=mutants.Length,mutants=results},new JsonSerializerOptions{WriteIndented=true}));
if(killed!=mutants.Length)Environment.ExitCode=1;
