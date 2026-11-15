using System.Collections.Immutable;
using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Simulator.Client;
using Simulator.RequestedQualification;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

public sealed class ReferenceGridReplayTests
{
    private static SimulationRequest Original() => new("grid",new(["0","a"],[new Voltage("V1",1,0,1),new Resistor("R1",1,0,2)]),
        new TransientConfigured(new(1,0.5,Method:"trapezoidal",Adaptive:new(1e-4,1e-6,1e-9,1e-9,100))));
    private static NativeRaw Reference(params double[] times) => new(false,[new("time","time"),new("v(a)","voltage")],
        times.Select(t=>new[]{new ComplexValue(t,0),new ComplexValue(t,0)}).ToImmutableArray()){PlotName="Transient Analysis"};
    [Fact] public void Replay_is_a_separate_exact_grid_request_and_does_not_mutate_free_run()
    {
        var original=Original();var replay=ReferenceGridReplay.Prepare(original,Reference(0,0.1,0.35,0.5,1));
        var before=Assert.IsType<TransientConfigured>(original.Analysis).Options;var after=Assert.IsType<TransientConfigured>(replay.Analysis).Options;
        Assert.NotNull(before.Adaptive);Assert.Null(before.AcceptedTimesS);Assert.Null(after.Adaptive);
        Assert.Equal(new[]{0d,0.1,0.35,0.5,1},after.AcceptedTimesS);Assert.Same(original.Circuit,replay.Circuit);
        Assert.Equal(before.MaxStepS,after.MaxStepS);Assert.Equal(before.Method,after.Method);
        Assert.Contains("accepted_times_s",JsonSerializer.Serialize(replay,WireFormat.Options));
    }
    public static IEnumerable<object[]> InvalidGrids => new object[][] {
        ["missing_stop",new[]{0d,0.5}], ["missing_zero",new[]{0.1,0.5,1}],
        ["duplicate",new[]{0d,0.5,0.5,1}], ["ceiling",new[]{0d,0.8,1}], ["nonfinite",new[]{0d,double.NaN,1}]
    };
    [Theory][MemberData(nameof(InvalidGrids))]
    public void Invalid_or_incomplete_native_grids_cannot_become_passing_replays(string label,double[] times)
    { Assert.NotEmpty(label);Assert.Throws<InvalidDataException>(()=>ReferenceGridReplay.Prepare(Original(),Reference(times))); }
    [Fact] public void Wrong_analysis_and_budget_are_refused()
    {
        Assert.Throws<InvalidDataException>(()=>ReferenceGridReplay.Prepare(Original(),Reference(0,0.5,1) with{Complex=true}));
        var request=Original();var options=Assert.IsType<TransientConfigured>(request.Analysis).Options;
        Assert.Throws<InvalidDataException>(()=>ReferenceGridReplay.Prepare(request with{Analysis=new TransientConfigured(options with{MaxSteps=1})},Reference(0,0.5,1)));
    }
    [Fact] public async Task Unresolved_initial_conditions_cannot_be_omitted_by_replay()
    {
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse("IC\nV1 in 0 0\nR1 in out 1k\nC1 out 0 1u\n.ic v(out)=0.5\n.tran .1 1\n.end\n",collectDeclarations:true));
        Assert.True(plan.Success);var operation=Assert.Single(plan.Operations);Assert.NotEmpty(operation.InitialConditions);
        await Assert.ThrowsAsync<NotSupportedException>(()=>ReferenceGridReplay.RunAsync(operation,plan.CircuitPreparation!,Reference(0,0.5,1),new(Environment.ProcessPath!)));
    }
    [Fact]
    public void Every_finite_uniform_reference_time_is_retained()
    {
        // Invoke the installed runner explicitly with a recorded seed and its
        // valid default gamma; the test result then records the 64-case run.
        var config=Config.QuickThrowOnFailure.WithMaxTest(64).WithReplay(
            Microsoft.FSharp.Core.FSharpOption<Replay>.Some(new(new Rnd(2026111405UL),null)));
        Check.One(config,FsCheck.Fluent.Prop.ForAll<NonNegativeInt>(size=>{
            var n=2+size.Get%63;var times=Enumerable.Range(0,n+1).Select(i=>(double)i/n).ToArray();
            var replay=ReferenceGridReplay.Prepare(Original(),Reference(times));
            return Assert.IsType<TransientConfigured>(replay.Analysis).Options.AcceptedTimesS!.SequenceEqual(times);
        }));
    }
}
