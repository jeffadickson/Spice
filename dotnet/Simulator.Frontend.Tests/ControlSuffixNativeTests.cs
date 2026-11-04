using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class ControlSuffixNativeTests
{
    [GroundOracleTheory]
    [InlineData("lu")][InlineData("faer")]
    public async Task Owned_native_suffix_computes_reporting_values_without_changing_physical_OP(string backend)
    {
        var parent=Environment.GetEnvironmentVariable("SPICE_GROUND_ARTIFACT_ROOT")!;
        Assert.True(Directory.Exists(parent));
        var folder=Path.Combine(parent,"control-suffix-native-"+backend+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        const string suffix="let twice=2*v(out)\ndefine triple(x) 3*x\nlet report_value=triple(v(out))\nunlet twice\ndisplay\nlisting";
        const string circuit="Owned suffix oracle\nV1 supply 0 1\nR1 supply out 1k\nR2 out 0 1k\n.control\nset xbrushwidth=2\nop\n";
        var original=circuit+suffix+"\n.endc\n.end\n";
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(original,collectDeclarations:true),backend:backend);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var operation=Assert.Single(plan.Operations);var request=Assert.Single(operation.Requests);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_GROUND_ENGINE")!).RunAsync(request,deadline.Token);
        Assert.True(response.Succeeded,response.Payload.GetRawText());
        var voltage=response.FinalValue("v("+plan.CircuitPreparation!.RootNodes["out"]+")");
        var source=Assert.Single(plan.CircuitPreparation.Origins,o=>o.InstancePath.IsEmpty&&o.LocalName.Equals("V1",StringComparison.OrdinalIgnoreCase));
        var current=response.FinalValue("i("+source.WireId+")");
        Assert.InRange(Math.Abs(voltage-0.5),0,1e-12);
        Assert.InRange(Math.Abs(current-(-0.0005)),0,1e-12);
        // Execute only this owned finite fixture in ngspice. The public compiler
        // still records the reporting expressions as unexecuted omissions.
        var input=Path.Combine(folder,"native.cir");await File.WriteAllTextAsync(input,circuit+suffix+"\nwrite native.raw v(out) i(V1) report_value\nquit\n.endc\n.end\n");
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_GROUND_NGSPICE")!){WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"-n","-D","filetype=ascii","-b",input})start.ArgumentList.Add(arg);
        using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
        try{await child.WaitForExitAsync(deadline.Token);}catch(OperationCanceledException){if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();throw;}
        await File.WriteAllTextAsync(Path.Combine(folder,"native.stdout.log"),await stdout);var error=await stderr;await File.WriteAllTextAsync(Path.Combine(folder,"native.stderr.log"),error);
        Assert.Equal(0,child.ExitCode);Assert.Equal("",error);
        var raw=AsciiRaw.Read(await File.ReadAllTextAsync(Path.Combine(folder,"native.raw")));
        Assert.Equal("Operating Point",raw.PlotName);var values=Assert.Single(raw.Points);
        double Read(string name)=>values[Assert.Single(Enumerable.Range(0,raw.Variables.Length),i=>raw.Variables[i].Name==name)].Real;
        Assert.InRange(Math.Abs(Read("v(out)")-voltage),0,1e-12);
        Assert.InRange(Math.Abs(Read("i(v1)")-current),0,1e-12);
        Assert.InRange(Math.Abs(Read("report_value")-1.5),0,1e-12);
        await File.WriteAllTextAsync(Path.Combine(folder,"request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
        await File.WriteAllTextAsync(Path.Combine(folder,"response.json"),response.Payload.GetRawText());
        await File.WriteAllTextAsync(Path.Combine(folder,"evidence.json"),JsonSerializer.Serialize(new{backend,input_sha256=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(input))),physical_voltage=voltage,physical_current_amps=current,native_reporting_value=Read("report_value"),omissions=plan.ProgramOmissions}));
    }
}
