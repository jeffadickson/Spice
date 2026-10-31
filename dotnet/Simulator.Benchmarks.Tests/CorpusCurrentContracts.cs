using Simulator.Benchmarks;
using Xunit;
using Simulator.Client;
using FsCheck.Xunit;

public sealed class CorpusCurrentContracts
{
    [Theory]
    [InlineData("L1")]
    [InlineData("E1")]
    [InlineData("H1")]
    public void SupportedMnaBranchesHaveExactDimensionalObservations(string id)
    {
        var signal=Assert.Single(CorpusPolicy.ObservationSignals(["out"],[id]), s=>s.Unit=="A");
        Assert.Equal($"i({id})",signal.Name);
        var rows=Tables.Parse($"Index time {id.ToLowerInvariant()}#branch\n0 0 -0.001\n", "transient", signal.Name);
        Assert.Equal(-0.001,Assert.Single(rows).Real);
        Assert.Throws<InvalidDataException>(()=>Tables.Parse("Index time vother#branch\n0 0 1\n", "transient",signal.Name));
    }
    [Fact]
    public void DirectiveShapedTitleDoesNotCreateAPhantomAnalysis()
    {
        Assert.DoesNotContain("multiple_declared_analyses",CorpusPolicy.Features(".tran nonsense nope\nV1 out 0 1\n.ac lin 1 10 10\n.end"));
        Assert.Contains("multiple_declared_analyses",CorpusPolicy.Features("owned\nV1 out 0 1\n.op\n.ac lin 1 10 10\n.end"));
    }
    [Fact]
    public void CurrentRequiresItsNamedColumnQuantityUnitAndAmpereBound()
    {
        var signals=CorpusPolicy.ObservationSignals(["out"],["V1"]);
        Assert.Equal(new[]{"v(out)","i(V1)"},signals.Select(s=>s.Name));
        var current=signals[1];Assert.Equal("current",current.Quantity);Assert.Equal("A",current.Unit);
        Assert.Equal(1,CorpusPolicy.SignalColumn([new("v(out)","voltage","V"),new("i(V1)","current","A")],current));
        var errors=new[]{
            Assert.Throws<InvalidDataException>(()=>CorpusPolicy.SignalColumn([new("i(V1)","current","V")],current)),
            Assert.Throws<InvalidDataException>(()=>CorpusPolicy.SignalColumn([new("i(V1)","voltage","A")],current)),
            Assert.Throws<InvalidDataException>(()=>CorpusPolicy.SignalColumn([new("i(V2)","current","A")],current))};
        foreach(var error in errors)Assert.False(string.IsNullOrWhiteSpace(error.Message));
        var report=new CorpusReport();
        Assert.Equal(1e-9,CorpusPolicy.AbsoluteBound(report,current));
        Assert.Equal(1e-5,CorpusPolicy.AbsoluteBound(report,signals[0]));
        var unitError=Assert.Throws<InvalidDataException>(()=>CorpusPolicy.AbsoluteBound(report,new("other","other","unknown")));
        Assert.False(string.IsNullOrWhiteSpace(unitError.Message));
        Assert.False(Tables.Within(new(0,0,0),new(0,5e-9,0),CorpusPolicy.AbsoluteBound(report,current),report.RelativeTolerance));
        var deck=CorpusPolicy.ObservedDeck("title\nV1 out 0 1\nR1 out 0 1000\n.end","op",["out"],["V1"]);
        Assert.Contains(".print op i(V1)\n",deck);
    }
    [Theory]
    [InlineData("R1")]
    [InlineData("V1\n")]
    [InlineData("V1)\n.control")]
    public void SourceObservationIdentifiersCannotInjectStatements(string source)=>
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.ObservationSignals(["out"],[source]));

    [Property(MaxTest=128,Replay="917,20261031")]
    public void ArbitrarySignalTextEitherHasSafeObservationsOrExplicitlyRejects(string? text)
    {
        try
        {
            var signals=CorpusPolicy.ObservationSignals([text??""],["V1"]);
            Assert.DoesNotContain('\n',signals[0].Name);Assert.DoesNotContain('\r',signals[0].Name);
            Assert.Equal("A",signals[1].Unit);
        }
        catch(InvalidDataException) { }
    }
    [Fact]
    public void CurrentAliasMustReferToTheRequestedVoltageSource()
    {
        const string table="Index time v1#branch\n0 0 -0.001\n1 1 -0.001\n";
        var rows=Tables.Parse(table,"transient","i(V1)");
        Assert.Equal(-0.001,rows[0].Real);
        Assert.Throws<InvalidDataException>(()=>Tables.Parse(table,"transient","i(V2)"));
        Assert.Throws<InvalidDataException>(()=>Tables.Parse(table,"transient","v(v1)"));
        Assert.Throws<InvalidDataException>(()=>Tables.Parse("Index time r1#branch\n0 0 -0.001\n","transient","i(R1)"));
    }
    [Fact]
    public void ObservationIdentifierCannotSmuggleATerminalNewline()=>
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.ObservedDeck("title\n.end","op",["out\n"]));
}
