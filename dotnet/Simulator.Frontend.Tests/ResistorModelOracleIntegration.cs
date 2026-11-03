using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Original R/model cards compared with typed resistance and analytical divider solutions.</summary>
public sealed class ResistorModelOracleIntegration
{
    [XspiceGainOracleFact]
    public async Task Original_resistor_models_match_DC_AC_and_ramp_transient()
    {
        foreach(var profile in new[]{"constant","sheet","etched"}) await Check(profile);
    }
    private static async Task Check(string profile)
    {
        var folder=Path.Combine(Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!,"resistor-model-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var model=profile switch {"constant"=>"R1 in out rm\n.model rm R(R=500)\n", "sheet"=>"R1 in out rm L=12u W=2u\n.model rm R(RSH=100)\n",_=>"R1 in out rm L=14u W=2u\n.model rm R(RSH=100 SHORT=1u NARROW=.5u)\n"};model+="RLOAD out 0 1k\n";
        var catalog=DeckParser.Parse("resistor\n"+model+".end\n",collectDeclarations:true).Declarations!;
        var prepared=ResistorModelLowering.Prepare(catalog,catalog.Primitives.Single(p=>p.Name=="R1"),ParameterEvaluator.Evaluate(catalog));Assert.True(prepared.Success);var resistance=prepared.Resistor!;
        var expectedOhms=profile switch{"constant"=>500d,"sheet"=>600d,_=>1200d};Assert.InRange(Math.Abs(resistance.Ohms-expectedOhms),0,1e-9);var factor=1000/(expectedOhms+1000);var checks=new List<object>();
        foreach(var mode in new[]{"dc","ac","tran"})
        {
            var source=mode=="tran"?"V1 in 0 PWL(0 0 1m 1)":"V1 in 0 DC 2 AC 1";
            var command=mode switch{"dc"=>"op", "ac"=>"ac lin 1 100 100",_=>"tran .1m 1m\nlinearize v(out)"};
            var path=Path.Combine(folder,mode+".cir");var data=Path.Combine(folder,mode+".dat");
            await File.WriteAllTextAsync(path,"Original resistor model\n"+source+"\n"+model+".control\nset numdgt=15\n"+command+"\nwrdata "+data.Replace('\\','/')+" v(out)\nquit\n.endc\n.end\n");
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=folder};
            foreach(var arg in new[]{"-n","-b",path})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)!)
            {
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}
                await File.WriteAllTextAsync(Path.Combine(folder,mode+".stdout"),await output);await File.WriteAllTextAsync(Path.Combine(folder,mode+".stderr"),await error);Assert.Equal(0,process.ExitCode);
            }
            var native=(await File.ReadAllLinesAsync(data)).Where(line=>!string.IsNullOrWhiteSpace(line)).Select(line=>line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray()).ToArray();
            var circuit=new Circuit(["0","in","out"],[new Voltage("drive",1,0,mode=="tran"?0:2),new Resistor("model",1,2,resistance.Ohms),new Resistor("load",2,0,1000)]);
            foreach(var backend in new[]{"lu","faer"})
            {
                Analysis analysis=mode switch{"dc"=>new OperatingPoint(),"ac"=>new Ac([100],[new("drive",1,0)]),_=>new TransientConfigured(new(.001,.0001,100,Sources:[new("drive",new Pwl([new(0,0),new(.001,1)]))]))};
                var request=new SimulationRequest("resistor-model-"+mode,circuit,analysis,backend);
                await File.WriteAllTextAsync(Path.Combine(folder,mode+"-"+backend+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
                var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!).RunAsync(request);
                await File.WriteAllTextAsync(Path.Combine(folder,mode+"-"+backend+".response.json"),response.Payload.ToString());
                Assert.True(response.Succeeded,response.Payload.ToString());
                var manifest=response.ReadManifest();var index=Array.FindIndex(manifest.Signals,s=>s.Id=="v(out)");Assert.True(index>=0);
                if(mode=="ac")
                {
                    var actual=Assert.Single(manifest.AcSamples).Values[index];Assert.InRange(Math.Abs(actual.Real-native[0][1]),0,1e-9);Assert.InRange(Math.Abs(actual.Imag-native[0][2]),0,1e-9);
                    Assert.InRange(Math.Abs(actual.Real-factor),0,1e-9);checks.Add(new{mode,backend,real=actual.Real,imag=actual.Imag,native=native[0]});
                }
                else foreach(var sample in manifest.Samples)
                {
                    var reference=mode=="dc"?native[0]:native.MinBy(row=>Math.Abs(row[0]-sample.TimeS))!;
                    if(mode=="tran")Assert.InRange(Math.Abs(reference[0]-sample.TimeS),0,1e-12);
                    var actual=sample.Values[index];Assert.InRange(Math.Abs(actual-reference[1]),0,1e-9);
                    Assert.InRange(Math.Abs(actual-(mode=="dc"?2*factor:factor*1000*sample.TimeS)),0,1e-9);
                    checks.Add(new{mode,backend,time=sample.TimeS,actual,native=reference[1]});
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(folder,"summary.json"),JsonSerializer.Serialize(new{profile,scope="original constant or sheet R model and divider",checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}
