using Simulator.Client;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;
/// <summary>Behavioral I(Vsource) is a branch-current read, not scalar parameter evaluation.</summary>
public sealed class BranchCurrentBehaviorTests
{
    /// <summary>Root and hierarchical, forward references use the existing native F/H laws with correct branch identity.</summary>
    [Theory][InlineData("I",false)][InlineData("V",false)][InlineData("I",true)][InlineData("V",true)]
    public void SingleCurrentReadBindsVoltageBranch(string quantity,bool nested)
    {
        var plan=Prepare(quantity,"Vsense",nested);
        Assert.True(plan.Success,string.Join(',',plan.Diagnostics.Select(d=>d.Code)));
        var elements=plan.Operations[0].Requests[0].Circuit.Elements;
        var sensor=Assert.Single(elements.OfType<Voltage>(),v=>v.Volts==0);
        if(quantity=="I") {var controlled=Assert.Single(elements.OfType<Cccs>());Assert.Equal(sensor.Id,controlled.ControlId);Assert.Equal(1,controlled.Gain);}
        else {var controlled=Assert.Single(elements.OfType<Ccvs>());Assert.Equal(sensor.Id,controlled.ControlId);Assert.Equal(1,controlled.Ohms);}
    }
    /// <summary>Unresolved and non-voltage-defined branch reads refuse atomically.</summary>
    [Theory][InlineData("Vmissing")][InlineData("R1")]
    public void UnsupportedControllerDoesNotBecomeZero(string control)
    {
        var plan=Prepare("I",control,false);Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }
    /// <summary>Nonlinear products, mixed runtime signals and extra arguments remain unsupported.</summary>
    [Theory][InlineData("I(Vsense)*I(Vdrive)")][InlineData("I(Vsense)+v(mid)")][InlineData("I(Vsense,Vdrive)")]
    public void ExtraOperatorsAndArgumentsAreNotDropped(string law)
    {
        var text="Current read\nVdrive in 0 1\nR1 in mid 1k\nVsense mid 0\nBcopy out 0 I={"+law+"}\nRload out 0 2k\n.op\n.end\n";
        var plan=RequestedAnalysis.Prepare(DeckParser.Parse(text,collectDeclarations:true));Assert.False(plan.Success);Assert.Empty(plan.Operations);
    }
    /// <summary>The bounded current profile cannot bypass the established expression-size fence.</summary>
    [Fact]
    public void LongReferenceRetainsExpressionBudget()
    {
        var plan=Prepare("I","V"+new string('x',4100),false);
        Assert.False(plan.Success);Assert.Empty(plan.Operations);Assert.Contains(plan.Diagnostics,d=>d.Code=="expression_budget");
    }
    /// <summary>Many valid identifier spellings still require a declared controlling branch.</summary>
    [FsCheck.Xunit.Property(MaxTest=64,Replay="2163,20261117")]
    public bool UnknownBranchesRemainAtomicFailures(int suffix)
    {
        var plan=Prepare("I","Vmissing_"+unchecked((uint)suffix).ToString(System.Globalization.CultureInfo.InvariantCulture),false);
        return !plan.Success&&plan.Operations.IsEmpty;
    }
    private static RequestedAnalysisPlan Prepare(string quantity,string control,bool nested)
    {
        var body=nested?"X1 mid 0 out mirror\n.subckt mirror s g o\nBcopy o g "+quantity+"={I("+control+")}\nVsense s g\n.ends mirror\n"
            :"Bcopy out 0 "+quantity+"={I("+control+")}\nVsense mid 0\n";
        return RequestedAnalysis.Prepare(DeckParser.Parse("Current read\nVdrive in 0 1\nR1 in mid 1k\nRload out 0 2k\n"+body+".op\n.end\n",collectDeclarations:true));
    }
}
