using FsCheck;
using FsCheck.Xunit;
using Simulator.Syntax;
using System.Globalization;
using Xunit;

namespace Simulator.Syntax.Tests;

/// <summary>Echo substitution preserves every bounded real vector component.</summary>
public sealed class VectorEchoTests
{
    [Fact]
    public void Echo_substitutes_all_real_components_in_order()
    {
        double[] data=[-2,0,3];
        var report=Execute("echo values=$&data",data);
        Assert.Equal("values=-2 0 3",Assert.Single(report.Output));
        Assert.Equal(new[]{-2d,0d,3d},data);
    }

    [Property(MaxTest=128,Replay="5003,2026111301")]
    public bool Echo_round_trips_bounded_integer_vectors(NonNegativeInt input)
    {
        var count=1+input.Get%64;
        var values=Enumerable.Range(0,count).Select(i=>(double)(i-count/2)).ToArray();
        var output=Assert.Single(Execute("echo $&data",values).Output);
        return output.Split(' ').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).SequenceEqual(values);
    }

    [Theory]
    [InlineData("echo $&missing")]
    [InlineData("echo $&data[0]")]
    [InlineData("echo $&data.field")]
    public void Unsupported_substitutions_still_refuse_atomically(string command)
    {
        Assert.Throws<InvalidDataException>(()=>Execute("echo first\n"+command,[1,2]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void Echo_preserves_scalar_and_maximum_vector_cardinality(int length)
    {
        var data=Enumerable.Repeat(2d,length).ToArray();
        Assert.Equal(length,Assert.Single(Execute("echo $&data",data).Output).Split(' ').Length);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Empty_and_oversized_inputs_are_rejected(int length)
        =>Assert.Throws<InvalidDataException>(()=>Execute("echo $&data",new double[length]));

    private static ControlVectorReport Execute(string body,double[] data)
    {
        var syntax=DeckParser.Parse("echo\n.control\n"+body+"\n.endc\n.end\n",collectDeclarations:true);
        return ControlVectorExecution.Execute(Assert.Single(syntax.Declarations!.ControlBlocks).Commands,
            new Dictionary<string,double[]> {["data"]=data});
    }
}
