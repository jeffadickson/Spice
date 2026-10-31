#:property PublishAot=false
using System.Diagnostics;
using System.Text;
using System.Text.Json;

// Real CLI regression tests. Work only in a unique ignored scratch directory;
// the intentional provenance fault changes an unused copied DLL, not source or
// the active worker binaries of another campaign.
if(args is not [var repository] || !Directory.Exists(repository) || !Directory.Exists(Path.Combine(repository,"work")))
    throw new ArgumentException("Usage: <existing repository with work directory>");
repository=Path.GetFullPath(repository);
var binaries=Path.Combine(repository,"dotnet/Simulator.Syntax.Corpus/bin/Debug/net10.0");
if(!Directory.Exists(binaries))throw new DirectoryNotFoundException("Build Simulator.Syntax.Corpus first");
var scratch=Path.Combine(repository,"work","syntax-cli-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var worker=Path.Combine(scratch,"worker");Directory.CreateDirectory(worker);
foreach(var file in Directory.EnumerateFiles(binaries).Where(p=>p.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".json",StringComparison.OrdinalIgnoreCase)))
    File.Copy(file,Path.Combine(worker,Path.GetFileName(file)));
var marker=Path.Combine(worker,"UnusedFixture.dll");File.WriteAllText(marker,"initial fixture bytes");
var valid=Path.Combine(scratch,"valid.cir");File.WriteAllText(valid,"Title\n.model modern NMOS LEVEL=54\nM1 d g s b modern\n.end\n");
var invalid=Path.Combine(scratch,"invalid.cir");File.WriteAllText(invalid,"Title\nR1 1 0 {1+}\n");
var encoding=Path.Combine(scratch,"encoding.cir");File.WriteAllBytes(encoding,[0xc3,0x28]);
var oversized=Path.Combine(scratch,"oversized.cir");File.WriteAllText(oversized,new string('t',1_048_577));
var hostile=Path.Combine(scratch,"hostile.cir");File.WriteAllText(hostile,"Title\nB1 1 0 v={"+new string('!',4096)+"1}\n");
var mixed=Path.Combine(scratch,"mixed.cir");File.WriteAllText(mixed,"Title\nB1 1 0 v={"+
    string.Concat(Enumerable.Repeat(new string('!',100)+"(",8))+"1"+new string(')',8)+"}\n");
var missing=Path.Combine(scratch,"missing.cir");
var paths=new[]{valid,invalid,encoding,oversized,hostile,mixed,missing};
var inventory=Path.Combine(scratch,"inventory.json");
File.WriteAllText(inventory,JsonSerializer.Serialize(new{corpus_root=repository,corpus_commit=new string('0',40),rows=paths.Select(p=>new{path=Path.GetRelativePath(repository,p)})}));
var normal=Path.Combine(scratch,"normal.json");
var code=await Run(normal,false);
using(var report=JsonDocument.Parse(File.ReadAllText(normal)))
{
    Require(code==0,"normal CLI exit");
    Require(report.RootElement.GetProperty("state").GetString()=="completed_with_results","completion state");
    Require(report.RootElement.GetProperty("corpus_commit").GetString()!=new string('0',40),"actual revision is not copied from fixture inventory");
    var cases=report.RootElement.GetProperty("rows").EnumerateArray().Where(r=>r.GetProperty("scope").GetString()=="ngspice").ToArray();
    var expected=new[]{"accepted_structure","syntax_rejected","encoding_rejected","resource_rejected","resource_rejected","resource_rejected","input_unavailable"};
    Require(cases.Length==expected.Length,"every fixture attempted");
    for(var i=0;i<cases.Length;i++)Require(cases[i].GetProperty("result").GetProperty("Status").GetString()==expected[i],$"fixture {i}: {expected[i]}");
}
var fault=Path.Combine(scratch,"binary-changed.json");
code=await Run(fault,true);
using(var report=JsonDocument.Parse(File.ReadAllText(fault)))
{
    Require(code!=0,"changed binary rejects qualification exit");
    Require(report.RootElement.GetProperty("state").GetString()=="provenance_changed","changed binary recorded explicitly");
}
Console.WriteLine(JsonSerializer.Serialize(new{status="passed",fixture_cases=7,campaign_cases=2,scratch,
    limitations=new[]{"Copied unused DLL exercises hash-change detection; this is not a sandbox or malicious transient-tampering proof."}}));

async Task<int> Run(string output,bool changeBinary)
{
    var start=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
    foreach(var argument in new[]{Path.Combine(worker,"Simulator.Syntax.Corpus.dll"),inventory,repository,output})start.ArgumentList.Add(argument);
    using var process=Process.Start(start)!;
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
    if(changeBinary)
    {
        while(!File.Exists(output) && !process.HasExited)await Task.Delay(10,timeout.Token);
        Require(File.Exists(output),"initial atomic checkpoint exists");
        File.AppendAllText(marker," changed bytes");
    }
    try { await process.WaitForExitAsync(timeout.Token); }
    catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw new TimeoutException("CLI fixture campaign");}
    File.WriteAllText(output+".stdout.log",await stdout);
    File.WriteAllText(output+".stderr.log",await stderr);
    return process.ExitCode;
}
static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
