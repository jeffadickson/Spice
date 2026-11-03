using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Opt-in original A-card oracle. Paths are explicit so an unavailable native executable never becomes a pass.</summary>
public sealed class XspiceGainOracleFactAttribute : FactAttribute
{
    public XspiceGainOracleFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE"))||!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE"))||!Directory.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")))
            Skip="Set SPICE_CORPUS_ENGINE, SPICE_CORPUS_NGSPICE and existing SPICE_CORPUS_TEST_ROOT for native gain checks.";
    }
}
public sealed class XspiceGainOracleIntegration
{
    [XspiceGainOracleFact]
    public async Task Original_A_gain_matches_typed_relation_in_DC_AC_and_ramp_transient()
    {
        foreach(var profile in new[]{"positive","negative-differential","units-case"}) await Check(profile);
    }
    private static async Task Check(string profile)
    {
        var folder=Path.Combine(Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!,"gain-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var model=profile switch {"negative-differential"=>"VREF ref 0 .5\nVLOW low 0 .2\nA1 %vd(in ref) %vd(out low) amp\n.model amp gain(in_offset=-.5 gain=-2 out_offset=.1)\nR1 out 0 1k\n","units-case"=>"A1 %v(in) %v(out) AMP\n.model AMP GaIn(IN_OFFSET=250m GAIN=4k OUT_OFFSET=-1k)\nR1 out 0 1k\n",_=>"A1 in out amp\n.model amp gain(in_offset=.25 gain=4 out_offset=.5)\nR1 out 0 1k\n"};
        var parsed=DeckParser.Parse("gain\nV1 in 0 2\n"+model+".end\n",collectDeclarations:true);var catalog=parsed.Declarations!;
        var prepared=XspiceGainLowering.Prepare(catalog,Assert.Single(catalog.XspiceInstances),ParameterEvaluator.Evaluate(catalog));Assert.True(prepared.Success);
        var gain=prepared.Gain!;var differential=profile=="negative-differential";var baseline=differential?.2-.5*gain.Gain:0;var checks=new List<object>();
        foreach(var mode in new[]{"dc","ac","tran"})
        {
            var source=mode=="tran"?"V1 in 0 PWL(0 0 1m 1)":"V1 in 0 DC 2 AC 1";
            var command=mode switch{"dc"=>"op", "ac"=>"ac lin 1 100 100",_=>"tran .1m 1m\nlinearize v(out)"};
            var path=Path.Combine(folder,mode+".cir");var data=Path.Combine(folder,mode+".dat");
            await File.WriteAllTextAsync(path,"Original XSPICE gain\n"+source+"\n"+model+".control\nset numdgt=15\n"+command+"\nwrdata "+data.Replace('\\','/')+" v(out)\nquit\n.endc\n.end\n");
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=folder};
            foreach(var arg in new[]{"-n","-b",path})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)!)
            {
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}
                await File.WriteAllTextAsync(Path.Combine(folder,mode+".stdout"),await output);await File.WriteAllTextAsync(Path.Combine(folder,mode+".stderr"),await error);Assert.Equal(0,process.ExitCode);
            }
            var native=(await File.ReadAllLinesAsync(data)).Where(line=>!string.IsNullOrWhiteSpace(line)).Select(line=>line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray()).ToArray();
            var circuit=new Circuit(["0","in","mid","out","ref","low"],[new Voltage("drive",1,0,mode=="tran"?0:2),new Voltage("reference",4,0,differential?.5:0),new Voltage("low",5,0,differential?.2:0),new Voltage("offset",2,differential?5:0,gain.Offset),new Vcvs("gain",3,2,1,differential?4:0,gain.Gain),new Resistor("load",3,0,1000)]);
            foreach(var backend in new[]{"lu","faer"})
            {
                Analysis analysis=mode switch{"dc"=>new OperatingPoint(),"ac"=>new Ac([100],[new("drive",1,0),new("offset",0,0),new("reference",0,0),new("low",0,0)]),_=>new TransientConfigured(new(.001,.0001,100,Sources:[new("drive",new Pwl([new(0,0),new(.001,1)]))]))};
                var request=new SimulationRequest("xspice-gain-"+mode,circuit,analysis,backend);
                await File.WriteAllTextAsync(Path.Combine(folder,mode+"-"+backend+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
                var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!).RunAsync(request);
                await File.WriteAllTextAsync(Path.Combine(folder,mode+"-"+backend+".response.json"),response.Payload.ToString());
                Assert.True(response.Succeeded,response.Payload.ToString());
                var manifest=response.ReadManifest();var index=Array.FindIndex(manifest.Signals,s=>s.Id=="v(out)");Assert.True(index>=0);
                if(mode=="ac")
                {
                    var actual=Assert.Single(manifest.AcSamples).Values[index];Assert.InRange(Math.Abs(actual.Real-native[0][1]),0,1e-9);Assert.InRange(Math.Abs(actual.Imag-native[0][2]),0,1e-9);
                    Assert.InRange(Math.Abs(actual.Real-gain.Gain),0,1e-9);checks.Add(new{mode,backend,real=actual.Real,imag=actual.Imag,native=native[0]});
                }
                else foreach(var sample in manifest.Samples)
                {
                    var reference=mode=="dc"?native[0]:native.MinBy(row=>Math.Abs(row[0]-sample.TimeS))!;
                    if(mode=="tran")Assert.InRange(Math.Abs(reference[0]-sample.TimeS),0,1e-12);
                    var actual=sample.Values[index];Assert.InRange(Math.Abs(actual-reference[1]),0,1e-9);
                    Assert.InRange(Math.Abs(actual-(mode=="dc"?baseline+2*gain.Gain+gain.Offset:baseline+gain.Offset+gain.Gain*1000*sample.TimeS)),0,1e-9);
                    checks.Add(new{mode,backend,time=sample.TimeS,actual,native=reference[1]});
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(folder,"summary.json"),JsonSerializer.Serialize(new{profile,scope="original scalar voltage gain with nonzero input/output offset",checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}
