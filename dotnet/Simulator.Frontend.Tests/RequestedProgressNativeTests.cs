using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Simulator.RequestedQualification;
using Xunit;
namespace Simulator.Frontend.Tests;
public sealed class RequestedProgressNativeTests
{
 [RequestedNativeFact] public async Task Running_checkpoints_report_positive_monotonic_admission_elapsed_time(){
  var parent=Environment.GetEnvironmentVariable("SPICE_REQUESTED_ARTIFACTS")!;var folder=Path.Combine(parent,"progress-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  var names=new[]{"one.cir","two.cir","three.cir"};foreach(var name in names)await File.WriteAllTextAsync(Path.Combine(folder,name),"progress\nV1 a 0 0\nR1 a 0 1k\n.dc V1 0 1 .1\n.end\n");
  var inventory=Path.Combine(folder,"inventory.json");await File.WriteAllTextAsync(inventory,JsonSerializer.Serialize(new{rows=names.Select(name=>new{scope="ngspice",path=name,result=new{Sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name))))}})}));
  var output=Path.Combine(folder,"output");var summary=Path.Combine(output,"summary.json");
  // Coalescing wakeups is safe: the reader always reloads authoritative atomic checkpoints.
  var notifications=Channel.CreateBounded<bool>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest});
  using var watcher=new FileSystemWatcher(folder,"summary.json"){IncludeSubdirectories=true};
  void Signal(object? sender,FileSystemEventArgs change){if(change.FullPath.Equals(summary,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))notifications.Writer.TryWrite(true);}
  watcher.Created+=Signal;watcher.Changed+=Signal;watcher.Renamed+=(sender,change)=>Signal(sender,change);watcher.EnableRaisingEvents=true;
  var root=Directory.GetParent(Path.GetDirectoryName(SourceFile())!)!.Parent!.FullName;
  var run=RequestedCorpus.RunAsync([root,folder,Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,inventory,output,"120"]);
  async Task Complete(){try{await run;}finally{notifications.Writer.TryComplete();}}
  var completion=Complete();var observed=new List<(bool Running,double Elapsed)>();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(180));
  await foreach(var _ in notifications.Reader.ReadAllAsync(deadline.Token)){
   try{
    // Readers permit replacement while holding the old checkpoint handle on Windows.
    await using var stream=new FileStream(summary,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);using var report=JsonDocument.Parse(await reader.ReadToEndAsync(deadline.Token));observed.Add((report.RootElement.GetProperty("state").GetString()=="running",report.RootElement.GetProperty("elapsed_seconds").GetDouble()));
   }catch(IOException){}
  }
  await completion;await File.WriteAllTextAsync(Path.Combine(folder,"observed-checkpoints.json"),JsonSerializer.Serialize(observed.Select(s=>new{running=s.Running,elapsed_seconds=s.Elapsed})));Assert.Equal(0,await run);Assert.True(observed.Count>=2);Assert.Contains(observed,s=>s.Running&&s.Elapsed>0);Assert.All(observed,s=>Assert.True(double.IsFinite(s.Elapsed)&&s.Elapsed>0));Assert.All(observed.Zip(observed.Skip(1)),pair=>Assert.True(pair.Second.Elapsed>=pair.First.Elapsed));
 }
 private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;
}
