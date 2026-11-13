using Simulator.Syntax;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
using System.Globalization;
namespace Simulator.Syntax.Tests;

/// <summary>Pulse alterations act on owned metadata, never borrowed physical results.</summary>
public sealed class ControlSourceAlterTests
{
    [Fact]
    public void Reads_before_and_after_alter_observe_distinct_owned_parameter_vectors()
    {
        double[] initial=[-1,2,.003,4e-6,5e-6,.006,.007];
        var report=Execute("let before=@v1[pulse]\nalter @v1[pulse] = [ 2 5 11n 12n 13n 54n 105n ]\nlet after=@v1[pulse]",initial);
        Assert.Equal(initial,report.Locals["before"].ToArray());
        var expected=new[]{2d,5d,11e-9,12e-9,13e-9,54e-9,105e-9};
        Assert.All(report.Locals["after"].Zip(expected),pair=>Assert.InRange(pair.First/pair.Second-1,-3e-16,3e-16));
        Assert.Equal(report.Locals["after"].ToArray(),report.SourceParameters["@v1[pulse]"].ToArray());
        Assert.Equal(new[]{-1d,2d,.003,4e-6,5e-6,.006,.007},initial);
    }

    [Theory]
    [InlineData("alter @missing[pulse] = [1 2 3 4 5 6 7]")]
    [InlineData("alter @v1[dc] = [1 2 3 4 5 6 7]")]
    [InlineData("alter @v1[pulse] = [1 2 3]")]
    [InlineData("alter @v1[pulse] = [1 2 3 4 5 6 1e309]")]
    [InlineData("alter @v1[pulse] = [1 2 3 4 5 6 7]; shell unsafe")]
    public void Invalid_updates_refuse_without_mutating_the_initial_state(string command)
    {
        double[] initial=[1,2,3,4,5,6,7];
        Assert.Throws<InvalidDataException>(()=>Execute(command,initial));
        Assert.Equal(new[]{1d,2d,3d,4d,5d,6d,7d},initial);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(8)]
    public void Initial_parameter_cardinality_is_validated_before_reporting(int length)
        =>Assert.Throws<InvalidDataException>(()=>Execute("echo first",new double[length]));

    [Fact]
    public void Nonfinite_initial_parameter_data_is_refused()
    {
        double[] values=[1,2,3,4,5,6,double.NaN];
        Assert.Throws<InvalidDataException>(()=>Execute("echo first",values));
        Assert.True(double.IsNaN(values[^1]));
    }
    [Property(MaxTest=128,Replay="5006,2026111301")]
    public bool Literal_updates_preserve_cardinality_order_and_borrowed_values(NonNegativeInt input)
    {
        var value=input.Get%1000;
        var expected=Enumerable.Range(0,7).Select(i=>(double)(value+i)).ToArray();
        double[] initial=[-1,2,3,4,5,6,7];
        var text=string.Join(" ",expected.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
        var report=Execute("alter @v1[pulse] = [ "+text+" ]\nlet after=@v1[pulse]",initial);
        return report.Locals["after"].SequenceEqual(expected)&&initial.SequenceEqual(new[]{-1d,2d,3d,4d,5d,6d,7d});
    }

    private static ControlVectorReport Execute(string body,double[] initial)
    {
        var syntax=DeckParser.Parse("Alter\n.control\n"+body+"\n.endc\n.end\n",collectDeclarations:true);
        return ControlVectorExecution.ExecuteWithSourceParameters(Assert.Single(syntax.Declarations!.ControlBlocks).Commands,
            new Dictionary<string,double[]>(),new Dictionary<string,double[]> {["@v1[pulse]"]=initial});
    }
}
