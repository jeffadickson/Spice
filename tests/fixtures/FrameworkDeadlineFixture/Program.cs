using System.Diagnostics;

// An owned process/pipe fixture, never a numerical reference. PID files let the
// test verify actual creation and reaping independently of supervisor messages.
var parentMarker=Environment.GetEnvironmentVariable("SPICE_PREFLIGHT_PROBE_PID")!;
var childMarker=Environment.GetEnvironmentVariable("SPICE_PREFLIGHT_PROBE_CHILD_PID")!;
if(args is ["--holder"]) {
    await File.WriteAllTextAsync(childMarker,Environment.ProcessId.ToString());
    await Task.Delay(TimeSpan.FromSeconds(30));return;
}
await File.WriteAllTextAsync(parentMarker,Environment.ProcessId.ToString());
await Console.In.ReadToEndAsync();
if(Environment.GetEnvironmentVariable("SPICE_PREFLIGHT_PROBE_MODE")=="hold") {
    var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
    start.ArgumentList.Add("--holder");
    using var child=Process.Start(start)??throw new IOException("Holder did not start.");
    using var startup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while(!File.Exists(childMarker))await Task.Delay(10,startup.Token);
    // Exiting the parent leaves the holder owning inherited stdout/stderr.
    // The supervisor must bound drainage and reap the already-orphaned holder.
} else await Task.Delay(TimeSpan.FromSeconds(30));
Console.Write("spice-fixture-preflight");
