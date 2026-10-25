using Simulator.Client;

/// <summary>Exercise the public polymorphic DTO graph with real engine calls.</summary>
public static class MoreChecks
{
    public static async Task<int> Run(SimulationClient client)
    {
        var count=0;
        void Check(bool condition,string label){if(!condition)throw new Exception(label);count++;}
        var circuit=new Circuit(["0","in","out"],[new Voltage("V1",1,0,1),new Resistor("R1",1,2,1000),new Capacitor("C1",2,0,1e-6)]);
        var legacy=await client.RunAsync(new("typed-legacy",circuit,new Transient(new(0.005,1e-5,1000))));
        Check(Math.Abs(legacy.FinalValue("v(out)")-(1-Math.Exp(-5)))<0.001,"typed legacy transient");
        var ac=(await client.RunAsync(new("typed-ac",circuit,new Ac([1/(Math.Tau*0.001)]),"faer"))).ReadManifest();
        Check(Math.Abs(ac.AcSamples[0].Values[1].Real-0.5)<1e-10 && Math.Abs(ac.AcSamples[0].Values[1].Imag+0.5)<1e-10,"typed complex AC");
        var experiment=new Experiment([
            new("final","v(out)",new ValueAt(0.005)),
            new("min","v(out)",new Minimum(0,0.005)),
            new("max","v(out)",new Maximum(0,0.005)),
            new("integral","v(out)",new Integral(0,0.005)),
            new("rms","v(out)",new Rms(0,0.005))
        ],[new("charged","final","V",0.97,1.01)]);
        var options=new TransientConfiguration(0.005,0.0002,10000,"trapezoidal",null,
            [new("V1",new Pwl([new(0,0),new(0.001,1),new(0.005,1)]))],
            new(1e-4,1e-8,1e-10,1e-9,100000));
        var dynamicResult=(await client.RunAsync(new("typed-pwl",circuit,new TransientConfigured(options),"faer",experiment))).ReadManifest();
        Check(dynamicResult.Measurements.All(m=>m.Status=="valid") && dynamicResult.Assertions[0].Outcome=="pass","typed PWL and measurement operations");
        var currentCircuit=new Circuit(["0","a"],[new Current("I1",0,1,0.001),new Resistor("R1",1,0,1000)]);
        var current=await client.RunAsync(new("typed-current",currentCircuit,new OperatingPoint()));
        Check(Math.Abs(current.FinalValue("v(a)")-1)<1e-12,"typed independent current");
        var constant=new TransientConfiguration(0.01,0.005,Sources:[new("I1",new Constant(0.002))]);
        var changed=await client.RunAsync(new("typed-constant",currentCircuit,new TransientConfigured(constant)));
        Check(Math.Abs(changed.FinalValue("v(a)")-2)<1e-12,"typed constant override");
        var sineCircuit=new Circuit(["0","a"],[new Voltage("V1",1,0,0),new Resistor("R1",1,0,1000)]);
        var sine=new TransientConfiguration(0.25,0.05,Sources:[new("V1",new Sine(2,3,1,0))]);
        var driven=await client.RunAsync(new("typed-sine",sineCircuit,new TransientConfigured(sine)));
        Check(Math.Abs(driven.FinalValue("v(a)")-5)<1e-10,"typed sinusoid");
        var rl=new Circuit(["0","in","out"],[new Voltage("V1",1,0,1),new Inductor("L1",1,2,0.001),new Resistor("R1",2,0,100)]);
        var inductor=await client.RunAsync(new("typed-rl",rl,new OperatingPoint(),"faer"));
        Check(Math.Abs(inductor.FinalValue("i(L1)")-0.01)<1e-12,"typed inductor branch current");
        var diodeCircuit=new Circuit(["0","s","a"],[new Voltage("V1",1,0,1),new Resistor("R1",1,2,1000),new Diode("D1",2,0,1e-12,0.02585)]);
        var diode=await client.RunAsync(new("typed-diode",diodeCircuit,new OperatingPoint()));
        Check(diode.Succeeded && diode.FinalValue("v(a)")>0.45,"typed diode operating point");
        Check(diode.ReadManifest().Newton is not null,"typed Newton evidence");
        var sweepJson="""{"schema_version":"1","base_case_id":"typed","sweep_sha256":"abc","sweep":{"element_id":"R1","values":[1.0]},"points":[],"summary":{"total":0,"succeeded":0,"failed":0}}""";
        var sweep=System.Text.Json.JsonSerializer.Deserialize<ScalarSweepResult>(sweepJson,WireFormat.Options)!;
        Check(sweep.Sweep.ElementId=="R1" && sweep.Summary.Total==0,"typed scalar sweep envelope");
        return count;
    }
}
