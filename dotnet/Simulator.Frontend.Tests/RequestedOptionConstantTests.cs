using Simulator.Client;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class RequestedOptionConstantTests
{
    [Theory]
    [InlineData("DISTOF1 0 DISTOF2 0",true)]
    [InlineData("DISTOF1 1 DISTOF2 0",false)]
    [InlineData("DISTOF1 0 DISTOF1 0",false)]
    [InlineData("DISTOF1 0 90",false)]
    public void Legacy_listing_and_zero_distortion_leave_op_and_ac_excitation_intact(string distortion,bool accepted)
    {
        var text="legacy lowpass\n.options LIST NODE POST TRANS noacct\nV1 in 0 DC 0 AC 1 SIN 0 1 1K 0 0 "+distortion
            +"\nR1 in out 10k\nR2 out 0 10k\nC1 out 0 1n\n.op\n.ac dec 10 1k 1meg\n.end\n";
        var syntax=DeckParser.Parse(text,collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.Equal(accepted,plan.Success);
        if(!accepted){Assert.Empty(plan.Operations);return;}
        Assert.Equal(new[]{"op","ac"},plan.Operations.Select(o=>o.Kind));
        var ac=Assert.IsType<Ac>(Assert.Single(plan.Operations[1].Requests).Analysis);
        Assert.Equal(1,Assert.Single(ac.SourcePhasors!).Real);
        Assert.Equal(31,ac.FrequenciesHz.Length);
        var original=Simulator.RequestedQualification.OriginalProjection.Create(text,syntax,plan.Operations[1],plan);
        Assert.Contains(distortion,original.Text);
    }

    [Theory]
    [InlineData(".options POST=1")]
    [InlineData(".options LIST RELTOL=1e-2")]
    [InlineData(".options NODE BYPASS=1")]
    public void Listing_flags_do_not_admit_unqualified_option_assignments(string options)
    {
        var syntax=DeckParser.Parse("legacy\n"+options+"\nV1 in 0 1\nR1 in 0 1k\n.op\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.False(plan.Success);
        Assert.Empty(plan.Operations);
    }

    [Theory]
    [InlineData("gear","1","backward_euler")]
    [InlineData("trap","1","backward_euler")]
    [InlineData("trap","2","trapezoidal")]
    [InlineData("gear","3",null)]
    [InlineData("gear","0",null)]
    public void Explicit_order_is_bound_to_the_correct_engine_method(string method,string order,string? expected)
    {
        var text="RC\nR1 out 0 1k\nC1 out 0 1u\n.ic v(out)=1\n.options method="+method+" maxord="+order+"\n.tran 1u 1m uic\n.end\n";
        var syntax=DeckParser.Parse(text,collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.Equal(expected!=null,plan.Success);
        if(expected!=null)
        {
            var op=Assert.Single(plan.Operations);
            Assert.Equal(expected,Assert.IsType<TransientConfigured>(Assert.Single(op.Requests).Analysis).Options.Method);
            Assert.Contains("maxord="+order,Simulator.RequestedQualification.OriginalProjection.Create(text,syntax,op,plan).Text);
        }
    }

    [Fact]
    public void Constant_exports_read_parameters_and_keep_the_control_namespace_separate()
    {
        var syntax=DeckParser.Parse("Export\n.param r=2k\n.csparam gain={r/1k}\nV1 in 0 1\nR1 in 0 {r}\n.control\nop\necho gain=$&gain\n.endc\n.end\n",collectDeclarations:true);
        var plan=RequestedAnalysis.Prepare(syntax);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        Assert.Equal(2,plan.ControlConstants["gain"]);
        Assert.Equal(2000,Assert.Single(plan.CircuitPreparation!.Request!.Circuit.Elements.OfType<Resistor>()).Ohms);
        Assert.False(ParameterEvaluator.Evaluate(syntax.Declarations!).Values.ContainsKey("gain"));
    }

    [Theory]
    [InlineData(".csparam gain={unknown}")]
    [InlineData(".csparam pi=1")]
    [InlineData(".csparam gain=1 GAIN=2")]
    [InlineData(".csparam gain=1\n.csparam GAIN=2")]
    public void Invalid_or_duplicate_exports_refuse_atomically(string cards)
    {
        var syntax=DeckParser.Parse("Exports\n"+cards+"\nV1 in 0 1\nR1 in 0 1k\n.op\n.end\n",collectDeclarations:true);
        Assert.Empty(RequestedAnalysis.Prepare(syntax).Operations);
    }
    [FsCheck.Xunit.Property(MaxTest=100,Replay="883,20261107")]
    public bool Control_export_reads_the_effective_parameter_without_rebinding_it(FsCheck.NonNegativeInt input)
    {
        var resistance=1+input.Get%10000;
        var literal=resistance.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var text="Exports\n.param r="+literal+"\n.csparam gain={r*2}\nV1 in 0 1\nR1 in 0 {r}\n.op\n.end\n";
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(text,collectDeclarations:true));
        return plan.Success&&plan.ControlConstants["gain"]==2*resistance
            &&plan.CircuitPreparation!.Request!.Circuit.Elements.OfType<Resistor>().Single().Ohms==resistance;
    }

    [Fact]
    public void Constants_are_visible_before_dispatch_and_cannot_be_shadowed()
    {
        var deck="Exports\n.csparam gain=2\nV1 in 0 1\nR1 in 0 1k\n.control\nset noaskquit\nop\nlet gain=3\n.endc\n.end\n";
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(deck,collectDeclarations:true));
        Assert.Contains(plan.Diagnostics,d=>d.Code=="control_constant_assignment");
        var commands=DeckParser.Parse(deck.Replace("let gain=3","echo gain=$&gain",StringComparison.Ordinal)
            .Replace("set noaskquit\nop","echo before=$&gain\nop",StringComparison.Ordinal),collectDeclarations:true)
            .Declarations!.ControlBlocks.Single().Commands;
        var report=ControlVectorExecution.Execute(commands,new Dictionary<string,double[]>(),activateAtAnalysis:true,
            constants:new Dictionary<string,double>{{"gain",2}});
        Assert.Equal(new[]{"before=2","gain=2"},report.Output);
    }
}
