using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

public sealed class AffineBehaviorOracleIntegration
{
    [XspiceGainOracleFact]
    public async Task Original_B_voltage_current_and_constant_match_DC_AC_and_ramp_transient()
    {
        foreach(var profile in new[]{"voltage","current","constant"})await Check(profile);
    }
    private static async Task Check(string profile)
    {
        var folder=Path.Combine(Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")!,"affine-b-oracle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var card=profile switch{"voltage"=>"B1 out low V=2*v(in,ref)-3*v(other)+.25","current"=>"B1 out 0 I=1m*v(in,ref)+2m",_=>"B1 out 0 V=1.25"};
        var parsed=DeckParser.Parse("B source\n"+card+"\n.end\n",collectDeclarations:true);var catalog=parsed.Declarations!;
        var prepared=BehavioralSourceLowering.Prepare(catalog,Assert.Single(catalog.BehavioralSources),ParameterEvaluator.Evaluate(catalog));Assert.True(prepared.Success);
        var source=prepared.Source!;var checks=new List<object>();
        foreach(var mode in new[]{"dc","ac","tran"})
        {
            var drive=mode=="tran"?"VDR in 0 PWL(0 0 1m 1)":"VDR in 0 DC 2 AC 1";
            var command=mode switch{"dc"=>"op","ac"=>"ac lin 1 100 100",_=>"tran .1m 1m\nlinearize v(out)"};
            var path=Path.Combine(folder,mode+".cir");var data=Path.Combine(folder,mode+".dat");
            await File.WriteAllTextAsync(path,"Original affine B source\n"+drive+"\nVREF ref 0 .5\nVOTHER other 0 DC .1 AC .5 90\nVLOW low 0 .2\n"+card+"\nRLOAD out 0 1k\n.control\nset numdgt=15\n"+command+"\nwrdata "+data.Replace('\\','/')+" v(out)\nquit\n.endc\n.end\n");
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=folder};foreach(var arg in new[]{"-n","-b",path})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)!)
            {
                var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);await process.WaitForExitAsync();throw;}
                await File.WriteAllTextAsync(Path.Combine(folder,mode+".stdout"),await stdout);await File.WriteAllTextAsync(Path.Combine(folder,mode+".stderr"),await stderr);Assert.Equal(0,process.ExitCode);
            }
            var native=(await File.ReadAllLinesAsync(data)).Where(line=>!string.IsNullOrWhiteSpace(line)).Select(line=>line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray()).ToArray();
            var nodes=new List<string>{"0","in","ref","other","low","out"};
            var elements=new List<Element>{new Voltage("drive",1,0,mode=="tran"?0:2),new Voltage("reference",2,0,.5),new Voltage("other",3,0,.1),new Voltage("low",4,0,.2),new Resistor("load",5,0,1000)};
            var ac=new List<AcSourcePhasor>{new("drive",1,0),new("reference",0,0),new("other",0,.5),new("low",0,0)};
            int Node(string name){var index=nodes.FindIndex(n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));Assert.True(index>=0);return index;}
            var p=Node(source.P.Text);var n=Node(source.N.Text);
            if(source.Quantity==BehavioralQuantity.Current)
            {
                foreach(var term in source.Terms)elements.Add(new Vccs("term"+elements.Count,p,n,Node(term.Node.Text),0,term.Coefficient));
                elements.Add(new Current("offset",p,n,source.Offset));ac.Add(new("offset",0,0));
            }
            else
            {
                var previous=p;
                foreach(var term in source.Terms)
                {
                    var next=nodes.Count;nodes.Add("hidden"+next);
                    elements.Add(new Vcvs("term"+elements.Count,previous,next,Node(term.Node.Text),0,term.Coefficient));previous=next;
                }
                elements.Add(new Voltage("offset",previous,n,source.Offset));ac.Add(new("offset",0,0));
            }
            foreach(var backend in new[]{"lu","faer"})
            {
                Analysis analysis=mode switch{"dc"=>new OperatingPoint(),"ac"=>new Ac([100],ac.ToArray()),_=>new TransientConfigured(new(.001,.0001,100,Sources:[new("drive",new Pwl([new(0,0),new(.001,1)]))]))};
                var request=new SimulationRequest("affine-b-"+mode,new(nodes.ToArray(),elements.ToArray()),analysis,backend);
                await File.WriteAllTextAsync(Path.Combine(folder,mode+"-"+backend+".request.json"),JsonSerializer.Serialize(request,WireFormat.Options));
                var response=await new SimulationClient(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!).RunAsync(request);
                await File.WriteAllTextAsync(Path.Combine(folder,mode+"-"+backend+".response.json"),response.Payload.ToString());Assert.True(response.Succeeded,response.Payload.ToString());
                var manifest=response.ReadManifest();var index=Array.FindIndex(manifest.Signals,s=>s.Id=="v(out)");Assert.True(index>=0);
                if(mode=="ac")
                {
                    var actual=Assert.Single(manifest.AcSamples).Values[index];Assert.InRange(Math.Abs(actual.Real-native[0][1]),0,1e-9);Assert.InRange(Math.Abs(actual.Imag-native[0][2]),0,1e-9);
                    var expected=profile switch{"voltage"=>(2d,-1.5),"current"=>(-1d,0d),_=>(0d,0d)};
                    Assert.InRange(Math.Abs(actual.Real-expected.Item1),0,1e-9);Assert.InRange(Math.Abs(actual.Imag-expected.Item2),0,1e-9);checks.Add(new{mode,backend,real=actual.Real,imag=actual.Imag,native=native[0]});
                }
                else foreach(var sample in manifest.Samples)
                {
                    var reference=mode=="dc"?native[0]:native.MinBy(row=>Math.Abs(row[0]-sample.TimeS))!;if(mode=="tran")Assert.InRange(Math.Abs(reference[0]-sample.TimeS),0,1e-12);
                    var actual=sample.Values[index];Assert.InRange(Math.Abs(actual-reference[1]),0,1e-9);
                    var input=mode=="dc"?2:1000*sample.TimeS;var expected=profile switch{"voltage"=>2*input-.85,"current"=>-input-1.5,_=>1.25};
                    Assert.InRange(Math.Abs(actual-expected),0,1e-9);checks.Add(new{mode,backend,time=sample.TimeS,actual,native=reference[1]});
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(folder,"summary.json"),JsonSerializer.Serialize(new{profile,checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}
