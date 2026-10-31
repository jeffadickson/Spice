using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Simulator.Benchmarks;

// Observations are appended only to original tiny fixtures owned by the trial.
// This is intentionally not a netlist transformation tool for arbitrary inputs.
if(args is not [var reportPath,var ngspice,var output] || !File.Exists(ngspice))
    throw new ArgumentException("Usage: <evaluation JSON> <ngspice executable> <output JSON>");
if(!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))throw new DirectoryNotFoundException("Output parent missing");
using var report=JsonDocument.Parse(File.ReadAllText(reportPath));
var raw=report.RootElement.GetProperty("raw_directory").GetString()!;
if(!Directory.Exists(raw))throw new DirectoryNotFoundException(raw);
var results=new List<object>();
foreach(var test in report.RootElement.GetProperty("cases").EnumerateArray())
{
    var id=test.GetProperty("id").GetString()!;
    if(!Regex.IsMatch(id,@"\A[a-z0-9-]+\z"))throw new InvalidDataException("Not an owned case identifier");
    if(test.GetProperty("result").GetProperty("Status").GetString()!="executed")
    {results.Add(new{id,state="not_numerically_executed",checks=Array.Empty<object>()});continue;}
    var checks=test.GetProperty("checks").EnumerateArray().ToArray();
    string Kind(int index)=>id=="multiple-analyses"?new[]{"op","ac","transient"}[index]:id.EndsWith("-ac",StringComparison.Ordinal)?"ac":id=="rc-transient"?"transient":"op";
    var text=File.ReadAllLines(Path.Combine(raw,id+".cir"));
    var cards=text.Where(line=>Regex.IsMatch(line,@"(?i)^\s*\.(op|ac|tran)\b")).ToArray();
    // Run every declared owned analysis in its own oracle deck. Ngspice batch
    // print tables for the combined deck were interleaved/unavailable; preserve
    // that first raw attempt rather than misclassifying it as a candidate fault.
    foreach(var group in checks.GroupBy(c=>c.GetProperty("analysis").GetInt32()))
    {
    var analysis=group.Key;
    var deck=string.Join('\n',text.Where(line=>!Regex.IsMatch(line,@"(?i)^\s*\.(save|end|op|ac|tran)\b")))+"\n"+cards[analysis]+"\n";
    foreach(var signal in group.Select(c=>c.GetProperty("Signal").GetString()!).Where(s=>s!="$time").Select(BaseSignal).Distinct())
        deck+=$".print {(Kind(analysis)=="transient"?"tran":Kind(analysis))} {signal}\n";    var path=Path.Combine(raw,id+"."+analysis+".observed.cir");File.WriteAllText(path,deck+".end\n");
    var log=Path.Combine(raw,id+"."+analysis+".ngspice.log");
    var start=new ProcessStartInfo(ngspice){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var arg in new[]{"-n","-b","-o",log,path})start.ArgumentList.Add(arg);
    using var child=Process.Start(start)??throw new IOException("Reference did not start");
    var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try{await child.WaitForExitAsync(deadline.Token);}
    catch(OperationCanceledException){child.Kill(true);await child.WaitForExitAsync();results.Add(new{id,analysis,state="timeout"});continue;}
    File.WriteAllText(Path.Combine(raw,id+"."+analysis+".ngspice.stdout"),await stdout);
    File.WriteAllText(Path.Combine(raw,id+"."+analysis+".ngspice.stderr"),await stderr);
    if(child.ExitCode!=0){results.Add(new{id,analysis,state="oracle_failed",exit_code=child.ExitCode});continue;}
    var reference=File.ReadAllText(log);var comparisons=new List<object>();var pass=true;
    foreach(var check in group)
    {
        var signal=check.GetProperty("Signal").GetString()!;
        var index=check.GetProperty("analysis").GetInt32();var kind=Kind(index);
        if(signal=="$time")continue; // Time is checked analytically in the candidate record.
        try
        {
            var point=Tables.Parse(reference,kind,BaseSignal(signal))[^1];
            var imaginary=Regex.IsMatch(signal,@"(?i)^[VI]I\(");
            var expected=imaginary?point.Imag:point.Real;
            var actual=check.GetProperty("actual").GetDouble();
            // Human-readable ngspice tables round; these bounds are separate
            // from the much tighter analytical checks in the candidate harness.
            var absolute=signal.StartsWith('I')?1e-8:1e-5;
            var bound=absolute+1e-4*Math.Abs(expected);
            var ok=double.IsFinite(actual)&&Math.Abs(actual-expected)<=bound;
            pass &= ok;
            comparisons.Add(new{analysis=index,signal,actual,reference=expected,absolute,relative=1e-4,passed=ok});
        }
        catch(InvalidDataException e){pass=false;comparisons.Add(new{signal,error=e.Message});}
    }
    results.Add(new{id,analysis,state=pass?"compared_pass":"comparison_failed",observed_sha256=Hash(path),log_sha256=Hash(log),checks=comparisons});
    }
}
File.WriteAllText(output,JsonSerializer.Serialize(new{candidate_report_sha256=Hash(reportPath),ngspice_sha256=Hash(ngspice),results,
    limitations=new[]{"Named owned-case endpoints only; no broad ngspice or model conformance claim.","Print-table comparison bounds account for rounded human-readable output; independent analytical checks remain separate."}},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Recorded {results.Count} reference dispositions in {output}");

static string BaseSignal(string signal)
{
    var match=Regex.Match(signal,@"(?i)\A([VI])[RI]?\(([^)]+)\)\z");
    if(!match.Success)throw new InvalidDataException("Not an owned observable");
    return match.Groups[1].Value.ToLowerInvariant()+"("+match.Groups[2].Value+")";
}
static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
