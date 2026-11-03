using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.Client;
using Simulator.RequestedQualification;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Missing native prerequisites are skips, never passing numerical evidence.</summary>
public sealed class GroundOracleTheoryAttribute : TheoryAttribute
{
    public GroundOracleTheoryAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_GROUND_ENGINE"))||
           !File.Exists(Environment.GetEnvironmentVariable("SPICE_GROUND_NGSPICE"))||
           !Directory.Exists(Environment.GetEnvironmentVariable("SPICE_GROUND_ARTIFACT_ROOT")))
            Skip="Set SPICE_GROUND_ENGINE, SPICE_GROUND_NGSPICE and an existing SPICE_GROUND_ARTIFACT_ROOT.";
    }
}

public sealed class GroundAliasNativeTests
{
    [GroundOracleTheory]
    [InlineData("lu")][InlineData("faer")]
    public async Task Ground_modes_match_original_native_inputs_and_independent_dividers(string backend)
    {
        const string formal="V1 in 0 3\nR1 out 0 1k\nX1 out in cell\n.subckt cell p gnd\nR2 p gnd 1k\n.ends";
        var cases=new[]{
            ("root",GroundAliasPolicy.Ngspice,"V1 out GnD 1\nR1 out GnD 1k",1.0),
            ("formal-default",GroundAliasPolicy.Ngspice,formal,0.0),
            ("formal-ps",GroundAliasPolicy.Pspice,formal,1.5),
            ("formal-disabled",GroundAliasPolicy.Disabled,formal,1.5),
            ("switch-auto",GroundAliasPolicy.Automatic,formal+GroundAliasTests.Switch,1.5),
            ("switch-disabled",GroundAliasPolicy.Disabled,formal+GroundAliasTests.Switch,1.5),
            ("header-name",GroundAliasPolicy.Pspice,"V1 in 0 3\nR1 out 0 1k\nX1 out in gnd.cell\n.subckt gnd.cell p ret\nVlocal gnd 0 1\nR2 p gnd 1k\n.ends",0.5),
            ("header-parameter",GroundAliasPolicy.Pspice,".param gnd=2\nV1 in 0 3\nR1 out 0 1k\nX1 out in cell\n.subckt cell p ret params: unused=gnd\nVlocal gnd 0 1\nR2 p gnd 1k\n.ends",0.5),
            ("formal-zero",GroundAliasPolicy.Disabled,"R1 out 0 1k\nX1 out unused cell\n.subckt cell p 0\nR2 p 0 1k\n.ends",0.0),
            ("root-disabled",GroundAliasPolicy.Disabled,"Vref gnd 0 2\nV1 out gnd 1\nR1 out 0 1k",3.0),
            ("global-disabled",GroundAliasPolicy.Disabled,".global gnd\nVref gnd 0 2\nR1 out 0 1k\nX1 out cell\n.subckt cell p\nR2 p gnd 1k\n.ends",1.0),
            ("behavior",GroundAliasPolicy.Ngspice,"V1 in 0 1\nB1 out 0 V=v(gnd)+v(in)\nR1 out 0 1k",1.0),
            ("e-zero",GroundAliasPolicy.Ngspice,"I1 0 out 1m\nE1 out 0 gnd 0 2\nR1 out 0 1k",0.0),
            ("g-zero",GroundAliasPolicy.Ngspice,"I1 0 out 1m\nG1 out 0 gnd 0 2\nR1 out 0 1k",1.0),
            ("gain-zero",GroundAliasPolicy.Ngspice,"A1 gnd out amp\n.model amp gain(gain=5 out_offset=1)\nR1 out 0 1k",1.0),
            ("summer-zero",GroundAliasPolicy.Ngspice,"A1 [ gnd 0 ] out amp\n.model amp summer(in_gain=[2 3] out_offset=1)\nR1 out 0 1k",1.0),
            ("ac-e-zero",GroundAliasPolicy.Ngspice,"E1 out 0 gnd 0 2\nR1 out 0 1k",0.0),
            ("ac-g-zero",GroundAliasPolicy.Ngspice,"G1 out 0 gnd 0 2\nR1 out 0 1k",0.0),
            ("ac-gain-zero",GroundAliasPolicy.Ngspice,"A1 gnd out amp\n.model amp gain(gain=5 out_offset=1)\nR1 out 0 1k",0.0),
            ("ac-summer-zero",GroundAliasPolicy.Ngspice,"A1 [ gnd 0 ] out amp\n.model amp summer(in_gain=[2 3] out_offset=1)\nR1 out 0 1k",0.0),
            ("exact-name",GroundAliasPolicy.Ngspice,"Vref agnd 0 2\nV1 out gnd 1\nR1 out agnd 1k",1.0)
        };
        var parent=Environment.GetEnvironmentVariable("SPICE_GROUND_ARTIFACT_ROOT")!;
        Assert.True(Directory.Exists(parent));
        var group=Path.Combine(parent,"ground-native-"+backend+"-"+Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(group));Directory.CreateDirectory(group);
        await File.WriteAllTextAsync(Path.Combine(group,"provenance.json"),JsonSerializer.Serialize(new{
            backend,scenarios=cases.Length,
            engine_sha256=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("SPICE_GROUND_ENGINE")!))),
            native_sha256=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("SPICE_GROUND_NGSPICE")!)))
        }));
        var client=new SimulationClient(Environment.GetEnvironmentVariable("SPICE_GROUND_ENGINE")!);
        foreach(var (name,policy,body,expected) in cases){
            var folder=Path.Combine(group,name);Assert.False(Directory.Exists(folder));Directory.CreateDirectory(folder);
            var source="Ground policy\n"+body+"\n"+(name.StartsWith("ac-",StringComparison.Ordinal)?".ac lin 1 1k 1k":".op")+"\n.end\n";
            var syntax=Simulator.Syntax.DeckParser.Parse(source,collectDeclarations:true);var plan=RequestedAnalysis.Prepare(syntax,limits:new(){GroundAliases=policy},backend:backend);
            Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
            var operation=Assert.Single(plan.Operations);var circuit=plan.CircuitPreparation!;
            var input=Path.Combine(folder,"original.cir");await File.WriteAllTextAsync(input,source);
            var request=operation.Requests.Single();await File.WriteAllTextAsync(Path.Combine(folder,"request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response=await client.RunAsync(request,deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(folder,"response.json"),response.Payload.GetRawText());
            Assert.True(response.Succeeded,response.Payload.GetRawText());
            if(operation.Kind=="op")Assert.InRange(Math.Abs(response.FinalValue("v("+circuit.RootNodes["out"]+")")-expected),0,1e-10);
            var args=new List<string>{"-n","-D","filetype=ascii"};
            if(circuit.GroundAliases==GroundAliasPolicy.Pspice||circuit.Request!.Circuit.Elements.Any(e=>e is PspiceSwitch))args.AddRange(["-D","ngbehavior=psa"]);
            if(policy==GroundAliasPolicy.Disabled)args.AddRange(["-D","no_auto_gnd"]);
            var raw=Path.Combine(folder,"native.raw");args.AddRange(["-b","-r",raw,"-o",Path.Combine(folder,"native.log"),input]);
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_GROUND_NGSPICE")!){WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in args)start.ArgumentList.Add(arg);
            using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();
            try{await child.WaitForExitAsync(deadline.Token);}catch{if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();throw;}
            await File.WriteAllTextAsync(Path.Combine(folder,"native.stdout"),await stdout);await File.WriteAllTextAsync(Path.Combine(folder,"native.stderr"),await stderr);
            Assert.Equal(0,child.ExitCode);
            var comparison=RequestedComparison.Compare(operation,circuit,[response],AsciiRaw.Read(await File.ReadAllTextAsync(raw)));
            await File.WriteAllTextAsync(Path.Combine(folder,"comparison.json"),JsonSerializer.Serialize(new{backend,policy,input_sha256=Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(input))),arguments=args,comparison}));
            Assert.True(comparison.Agreed,JsonSerializer.Serialize(comparison));
        }
    }
}
