using System.Collections.Immutable;
using Simulator.Client;

namespace Simulator.RequestedQualification;

/// <summary>Comparison of one solver trace on its own recorded grid.</summary>
public sealed record ReferenceTraceResult(int Points,int Failed,double MaximumAbsoluteError);

/// <summary>Independent closed-form evidence for the same mapped observable on both grids.</summary>
public sealed record ReferenceObservable(string NativeSignal,string Unit,double AbsoluteTolerance,double RelativeTolerance,
    ReferenceTraceResult Engine,ReferenceTraceResult Native);

/// <summary>Supplementary evidence for a hash-pinned memoryless circuit.</summary>
public sealed record MemorylessReferenceResult(string Profile,string ExpandedSha256,string Status,ImmutableArray<ReferenceObservable> Observables)
{
    /// <summary>Both complete grids match every expected voltage and signed current.</summary>
    public bool Agreed=>Status=="agreed"&&!Observables.IsEmpty;
}

internal static class MemorylessReference
{
    private sealed record Profile(string Name,Dictionary<string,Func<double,double>> Equations);

    // Equations are derived from the named physical circuits, independent of
    // the generated engine requests and both recorded solver traces.
    private static readonly Profile Phase=new("arbitrary_phase",new(StringComparer.OrdinalIgnoreCase)
    {
        ["v(1)"]=PhasedSine, ["v(2)"]=PeriodicPulse,
        ["i(v1)"]=t=>-PhasedSine(t)/1000, ["i(v2)"]=t=>-PeriodicPulse(t)/1000
    });

    private static readonly Profile Array=new("res_array",new(StringComparer.OrdinalIgnoreCase)
    {
        ["v(1)"]=ArraySine, ["v(2)"]=ArraySine, ["v(3)"]=ArraySine,
        ["v(4)"]=ArraySine, ["v(5)"]=ArraySine, ["v(6)"]=ArraySine,
        ["i(vr1)"]=t=>ArraySine(t)/10000, ["i(vr2)"]=t=>ArraySine(t)/10000,
        ["i(vr3)"]=_=>0, ["i(vr4)"]=t=>ArraySine(t)/5000,
        ["i(vr5)"]=t=>ArraySine(t)/10000, ["i(vin)"]=t=>-ArraySine(t)/2000
    });

    internal static MemorylessReferenceResult? Compare(string hash,ComparisonResult strict,ResultManifest engine,NativeRaw native)
    {
        var profile=hash switch
        {
            "2637631a2c3e35f3fedb22f2d3409537aef24e988e3e5e093482a3140b475ca2"=>Phase,
            "618fc4d2f3f3752777327ee685c16da060744017421a7f0ac7d442f80f823ede"=>Array,
            _=>null
        };
        if(profile==null)return null;
        // Strict validation owns shape, units and grid completeness. An absent
        // or unexpected observable cannot become agreement in this second view.
        if(!strict.Gaps.IsEmpty||!profile.Equations.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(strict.Observables.Select(o=>o.NativeSignal)))
            return new(profile.Name,hash,"observable_gap",[]);
        var results=ImmutableArray.CreateBuilder<ReferenceObservable>(strict.Observables.Length);
        foreach(var mapping in strict.Observables)
        {
            var equation=profile.Equations[mapping.NativeSignal];
            var engineColumn=System.Array.FindIndex(engine.Signals,s=>s.Id==mapping.EngineSignal);
            var nativeColumn=Enumerable.Range(0,native.Variables.Length).Single(i=>native.Variables[i].Name.Equals(mapping.NativeSignal,StringComparison.OrdinalIgnoreCase));
            var ours=Measure(engine.Samples.Select(p=>(p.TimeS,p.Values[engineColumn])),equation,mapping);
            var reference=Measure(native.Points.Select(p=>(p[0].Real,p[nativeColumn].Real)),equation,mapping);
            results.Add(new(mapping.NativeSignal,mapping.Unit,mapping.AbsoluteTolerance,mapping.RelativeTolerance,ours,reference));
        }
        return new(profile.Name,hash,results.Any(o=>o.Engine.Failed!=0||o.Native.Failed!=0)?"numerical_disagreement":"agreed",results.MoveToImmutable());
    }

    private static ReferenceTraceResult Measure(IEnumerable<(double Time,double Value)> samples,Func<double,double> expected,ObservableComparison bounds)
    {
        var points=0;var failed=0;var maximum=0d;
        foreach(var (time,value) in samples)
        {
            var reference=expected(time);
            var error=Math.Abs(value-reference);
            var tolerance=bounds.AbsoluteTolerance+bounds.RelativeTolerance*Math.Abs(reference);
            if(!double.IsFinite(error)||!double.IsFinite(tolerance))throw new InvalidDataException("nonfinite_analytical_comparison");
            if(error>tolerance)failed++;
            maximum=Math.Max(maximum,error);points++;
        }
        return new(points,failed,maximum);
    }

    private static double PhasedSine(double t)=>Math.Sin(2*Math.PI*1000*t+Math.PI/4);
    private static double ArraySine(double t)=>Math.Sin(2*Math.PI*1e8*Math.Max(0,t-1e-9));

    private static double PeriodicPulse(double t)
    {
        var local=t%1e-3;
        if(local<1e-5)return -1+2*local/1e-5;
        if(local<=5.1e-4)return 1;
        if(local<5.2e-4)return 1-2*(local-5.1e-4)/1e-5;
        return -1;
    }
}
