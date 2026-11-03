using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>Original analog summer cards compared with existing typed voltage elements and analytical references.</summary>
public sealed class XspiceSummerOracleIntegration
{
    [XspiceGainOracleFact]
    public async Task Original_summer_defaults_weights_and_differential_controls_match_DC_AC_and_transient()
    {
        foreach(var profile in new[]{"default","weighted","differential"})await Check(profile);
    }
    private static async Task Check(string profile)
    {
        var folder=Path.Combine(Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!,"summer-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var differential=profile=="differential";var weighted=profile=="weighted";
        var model=(differential?"A1 [%vd(in ref) %vd(other ref)] %vd(out low) sum":"A1 [in other] out sum")+"\n.model sum summer"+(weighted?"(in_gain=[2 -3] in_offset=[.25 -.5] out_gain=4 out_offset=.1)":"")+"\nR1 out 0 1k\n";
        var controls=$"VOTHER other 0 DC .1 AC .5 90\nVREF ref 0 {(differential?".5":"0")}\nVLOW low 0 {(differential?".2":"0")}\n";
        var syntax=DeckParser.Parse("summer\n"+model+".end\n",collectDeclarations:true);var catalog=syntax.Declarations!;
        var preparation=XspiceSummerLowering.Prepare(catalog,Assert.Single(catalog.XspiceInstances),ParameterEvaluator.Evaluate(catalog));Assert.True(preparation.Success);var summer=preparation.Summer!;
        var checks=new List<object>();
        foreach(var mode in new[]{"dc","ac","tran"})
        {
            var path=Path.Combine(folder,mode+".cir");var data=Path.Combine(folder,mode+".dat");var command=mode switch{"dc"=>"op","ac"=>"ac lin 1 100 100",_=>"tran .1m 1m\nlinearize v(out)"};
            await File.WriteAllTextAsync(path,"Original summer\n"+(mode=="tran"?"VDRIVE in 0 PWL(0 0 1m 1)":"VDRIVE in 0 DC 2 AC 1")+"\n"+controls+model+".control\nset numdgt=15\n"+command+"\nwrdata "+data.Replace('\\','/')+" v(out)\nquit\n.endc\n.end\n");
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=folder};foreach(var arg in new[]{"-n","-b",path})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)!){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}await File.WriteAllTextAsync(Path.Combine(folder,mode+".stdout"),await output);await File.WriteAllTextAsync(Path.Combine(folder,mode+".stderr"),await error);Assert.Equal(0,process.ExitCode);}
            var native=(await File.ReadAllLinesAsync(data)).Where(l=>!string.IsNullOrWhiteSpace(l)).Select(l=>l.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray()).ToArray();
            var input=mode=="tran"?"VDRIVE in 0 PWL(0 0 1m 1)":"VDRIVE in 0 DC 2 AC 1";
            var integrated=DeckParser.Parse("Integrated summer\n"+input+"\n"+controls+model+".end\n",collectDeclarations:true);
            var selected=OperatingPointSelection.Select(integrated);Assert.True(selected.Success,string.Join(',',selected.Prepared.Diagnostics.Select(d=>d.Code)));
            foreach(var backend in new[]{"lu","faer"})
            {
                var request=selected.Prepared.Request! with{Backend=backend,CaseId="summer-"+profile+"-"+mode};
                if(mode=="ac")request=request with{Analysis=new Ac([100],selected.Prepared.AcSources.ToArray())};
                if(mode=="tran"){
                    var transient=TransientSelection.Prepare(integrated,.001,.0001,100,backend:backend);
                    Assert.True(transient.Success,string.Join(',',transient.Prepared.Diagnostics.Select(d=>d.Code)));request=transient.Prepared.Request!;
                }
                await File.WriteAllTextAsync(Path.Combine(folder,mode+"-"+backend+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
                var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!).RunAsync(request);await File.WriteAllTextAsync(Path.Combine(folder,mode+"-"+backend+".response.json"),response.Payload.ToString());Assert.True(response.Succeeded,response.Payload.ToString());var manifest=response.ReadManifest();var index=Array.FindIndex(manifest.Signals,s=>s.Id=="v("+selected.Prepared.RootNodes["out"]+")");Assert.True(index>=0);
                if(mode=="ac"){var actual=Assert.Single(manifest.AcSamples).Values[index];Assert.InRange(Math.Abs(actual.Real-native[0][1]),0,1e-9);Assert.InRange(Math.Abs(actual.Imag-native[0][2]),0,1e-9);Assert.InRange(Math.Abs(actual.Real-(weighted?8:1)),0,1e-9);Assert.InRange(Math.Abs(actual.Imag-(weighted?-6:.5)),0,1e-9);checks.Add(new{mode,backend,actual.Real,actual.Imag});}
                else foreach(var sample in manifest.Samples){var reference=mode=="dc"?native[0]:native.MinBy(row=>Math.Abs(row[0]-sample.TimeS))!;if(mode=="tran")Assert.InRange(Math.Abs(reference[0]-sample.TimeS),0,1e-12);var actual=sample.Values[index];var vin=mode=="dc"?2:1000*sample.TimeS;var expected=weighted?8*vin+6.9:differential?vin-.7:vin+.1;Assert.InRange(Math.Abs(actual-reference[1]),0,1e-9);Assert.InRange(Math.Abs(actual-expected),0,1e-9);checks.Add(new{mode,backend,sample.TimeS,actual,expected});}
            }
        }
        await File.WriteAllTextAsync(Path.Combine(folder,"summary.json"),JsonSerializer.Serialize(new{profile,checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}