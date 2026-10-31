using Simulator.Benchmarks;
using Xunit;
using FsCheck.Xunit;

public sealed class CorpusContracts
{
    [Fact]
    public void AxisValidationAndNearestAcPointCannotBeMaskedByDistantSamples()
    {
        Point[] rows=[new(1,10,1),new(100,20,2)];
        Assert.Equal(new Point(100,20,2),CorpusPolicy.Reference(rows,100,"ac"));
        Assert.Equal(new Point(1,10,1),CorpusPolicy.Reference(rows,1,"ac"));
        foreach(var coordinate in new[]{0d,-1e-15,double.NaN,double.PositiveInfinity})
        {
            var error=Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([new(1e-15,1,0)],coordinate,"ac"));
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
        var missing=Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference(rows,50,"ac"));
        Assert.False(string.IsNullOrWhiteSpace(missing.Message));
        Assert.Equal(new Point(0,1,0),CorpusPolicy.Reference([new(0,1,0),new(1,2,0)],0,"transient"));
        var invalid=Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference(rows,-1,"transient"));
        Assert.False(string.IsNullOrWhiteSpace(invalid.Message));
        // Fixed binary-representable boundary for the existing AC absolute
        // plus relative print-axis contract; the next double is outside it.
        const double coordinateAtBound=3.125e-14;
        const double frequencyAtBound=1.0312501562500002e-12;
        Assert.Equal(coordinateAtBound,CorpusPolicy.Reference([new(frequencyAtBound,1,0)],coordinateAtBound,"ac").Coordinate);
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([new(Math.BitIncrement(frequencyAtBound),1,0)],coordinateAtBound,"ac"));
    }
    [Fact]
    public void TransientEndpointAllowsOnlyBoundedBinaryRepresentationDifference()
    {
        var endpoint=6e-9;
        var coordinate=Math.BitIncrement(endpoint);
        Assert.Equal(new Point(coordinate,1,0),CorpusPolicy.Reference([new(0,1,0),new(endpoint,1,0)],coordinate,"transient"));
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([new(0,1,0),new(endpoint,1,0)],endpoint+1e-20,"transient"));
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([new(0,1,0),new(endpoint,1,0)],double.NaN,"transient"));
        for(var i=0;i<4;i++)endpoint=Math.BitIncrement(endpoint);
        Assert.Equal(endpoint,CorpusPolicy.Reference([new(0,1,0),new(6e-9,1,0)],endpoint,"transient").Coordinate);
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([new(0,1,0),new(6e-9,1,0)],Math.BitIncrement(endpoint),"transient"));
        var first=1e-9;var before=first;
        for(var i=0;i<4;i++)before=Math.BitDecrement(before);
        Assert.Equal(before,CorpusPolicy.Reference([new(first,1,0),new(2e-9,2,0)],before,"transient").Coordinate);
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([new(first,1,0),new(2e-9,2,0)],Math.BitDecrement(before),"transient"));
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([new(0,1,0),new(6e-9,1,0)],-double.Epsilon,"transient"));
        Assert.Equal(1.5,CorpusPolicy.Reference([new(1,1,0),new(2,2,0)],1.5,"transient").Real);
    }
    // Assert the documented outcome contract, including collisions between
    // publication, harness, unfinished work and numerical failures.
    [Theory]
    [InlineData("completed_with_results","offline","compared_pass",0)]
    [InlineData("completed_with_results","created","unsupported",0)]
    [InlineData("completed_with_results","created","import_rejected",0)]
    [InlineData("completed_with_results","created","partial_comparison",0)]
    [InlineData("completed_with_results","created","comparison_failed",1)]
    [InlineData("completed_with_results","created","simulator_failed",1)]
    [InlineData("completed_with_results","created","oracle_failed",1)]
    [InlineData("completed_with_results","created","input_or_harness_failed",1)]
    [InlineData("completed_with_results","created","provenance_changed",1)]
    [InlineData("completed_with_results","created","budget_exhausted",2)]
    [InlineData("completed_with_results","created","cancelled",2)]
    [InlineData("cancelled","created","comparison_failed",2)]
    [InlineData("budget_exhausted","created","compared_pass",2)]
    [InlineData("harness_failed","created","cancelled",4)]
    [InlineData("harness_failed","publish_failed","cancelled",3)]
    [InlineData("completed_with_results","created_with_publisher_error","compared_pass",3)]
    public void ExitContractPreservesFailuresAndPriority(string state,string publisher,string row,int expected)
    {
        var report=new CorpusReport{State=state,IssueState=publisher};
        report.Rows.Add(new CorpusRow("deck","hash","tests",[]){Status=row});
        Assert.Equal(expected,CorpusPolicy.ExitCode(report));
    }
    [Theory]
    [InlineData("op",false)]
    [InlineData("op",true)]
    [InlineData("ac",false)]
    [InlineData("transient",false)]
    public void ObservationDeckHasRequiredAnalysisAndTerminator(string kind,bool existingOp)
    {
        var original="title\nV1 out 0 1\nR1 out 0 1000\n"+(existingOp?".op\n":"")+".end";
        var deck=CorpusPolicy.ObservedDeck(original,kind,["out","other"]);
        Assert.EndsWith("\n.end\n",deck);
        Assert.Equal(kind=="op"?1:0,deck.Split('\n').Count(line=>line==".op"));
        var printKind=kind=="transient"?"tran":kind;
        Assert.Contains($"\n.print {printKind} v(out)\n",deck);
        Assert.Contains($"\n.print {printKind} v(other)\n",deck);
        Assert.Contains("R1 out 0 1000\n",deck);
    }
    [Theory]
    [InlineData("")]
    [InlineData("out)\n.control")]
    [InlineData("out space")]
    public void ObservationIdentifiersCannotInjectStatements(string node)
    {
        var error=Assert.Throws<InvalidDataException>(()=>CorpusPolicy.ObservedDeck("title\n.end","op",[node]));
        Assert.Contains("Node name",error.Message);
    }
    [Fact]
    public void ObservationNeedsAtLeastOneSignal()
    {
        var error=Assert.Throws<InvalidDataException>(()=>CorpusPolicy.ObservedDeck("title\n.end","op",[]));
        Assert.Contains("Node name",error.Message);
    }
    [Fact]
    public void OneUnsafeSignalCannotHideAmongValidSignals()
    {
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.ObservedDeck("title\n.end","op",["out","out)\n.control"]));
        var error=Assert.Throws<InvalidDataException>(()=>CorpusPolicy.ObservedDeck("title\n.control\n.endc\n.end","op",["out"]));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportTimeoutCannotExitSuccessfully(bool precedingPass)
    {
        var report=new CorpusReport{State="completed_with_results"};
        if(precedingPass)report.Rows.Add(new CorpusRow("first","h","tests",[]){Status="compared_pass"});
        report.Rows.Add(new CorpusRow("last","h","tests",[]){Status="timeout"});
        Assert.Equal(1,CorpusPolicy.ExitCode(report));
    }
    [Theory]
    [InlineData("print")]
    [InlineData("plot")]
    [InlineData("probe")]
    public void ObservationRemovalIncludesContinuationsAndPreservesDevices(string card)
    {
        var deck=CorpusPolicy.ObservedDeck($"title\nV1 out 0 1\nR1 out 0\n+ 1000\n.{card} op v(out)\n* comment\n\n+ i(V1)\n.op\n.end\n","op",["out"]);
        Assert.Contains("R1 out 0\n+ 1000",deck);
        Assert.DoesNotContain("i(V1)",deck);
        Assert.Contains("V1 out 0 1",deck);
    }
    [Fact]
    public void ProvenanceFailureCannotExitSuccessfullyEvenWithNoPassingRows()=>
        Assert.Equal(1,CorpusPolicy.ExitCode(new CorpusReport{State="provenance_changed"}));
    [Fact]
    public void EmptyOrZeroFrequencyAcCannotSupplyReferenceEvidence()
    {
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([],1,"ac"));
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference([new(1,2,0)],0,"ac"));
    }
    [Fact]
    public void DeckExtensionAndExtensionlessCoverageIsExplicit()
    {
        Assert.True(CorpusPolicy.IsDeck("examples/test.deck","title\n.end\n"));
        Assert.True(CorpusPolicy.IsDeck("examples/plain","title\nR1 a 0 1\n.end\n"));
        Assert.False(CorpusPolicy.IsDeck("Makefile.in","all: build\n"));
        Assert.False(CorpusPolicy.IsDeck("notes.txt",".end\n"));
    }
    [Fact]
    public void FeatureHintsAreNotExecutionPassesAndIncludeMultipleBlockers()
    {
        var hints=CorpusPolicy.Features("title\n.control\n.endc\nX1 a 0 foo\n.model M NMOS\n.end");
        Assert.Contains("control_blocks",hints); Assert.Contains("hierarchy",hints);
        Assert.Contains("model_cards_present",hints);
        Assert.True(CorpusPolicy.UnsafeReference("title\n.include external.lib\n.end"));
        Assert.False(CorpusPolicy.UnsafeReference("title\n* .control ignored\nR1 a 0 1\n.end"));
    }
    [Fact]
    public void BudgetAndUnsupportedRemainUnfinishedWork()
    {
        var rows=new[]{new CorpusRow("a","hash","tests",[]){Status="compared_pass"},
            new CorpusRow("b","hash","tests",[]){Status="unsupported"},
            new CorpusRow("c","hash","tests",[]){Status="budget_exhausted"}};
        Assert.Equal(2,CorpusPolicy.Remaining(rows));
    }
    [Fact]
    public void AcPrintRoundingCannotMatchArbitrarilyDifferentFrequencies()
    {
        var rows=new[]{new Point(12.58925,1,2)};
        Assert.Equal(new Point(12.5892541179,1,2),CorpusPolicy.Reference(rows,12.5892541179,"ac"));
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Reference(rows,15,"ac"));
    }
    [Fact]
    public void ChartSeparatesChangedInventoriesAndEscapesLabels()
    {
        var h=new[]{new CorpusHistory("r1","same",3,1,"now"),new CorpusHistory("r2","different",9,0,"later")};
        var svg=CorpusPresentation.Chart(h,"same");
        Assert.Contains("Remaining",svg);Assert.DoesNotContain("r2",svg);
        Assert.Contains("baseline",svg);
    }
    [Theory]
    [InlineData("https://github.com/owner/repository.git","owner/repository")]
    [InlineData("git@github.com:owner/repository.git","owner/repository")]
    public void IssueTargetIsDerivedFromExplicitGitHubOrigin(string remote,string expected)=>Assert.Equal(expected,CorpusPolicy.Repository(remote));
    [Theory]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("owner/repo --delete")]
    public void NonGitHubOrMalformedIssueTargetsAreRejected(string remote)=>Assert.Throws<InvalidDataException>(()=>CorpusPolicy.Repository(remote));
    [Fact]
    public void ObservationEditsPreserveEquationsAndNeverExecuteControls()
    {
        var deck=CorpusPolicy.ObservedDeck("title\nR1 out 0 1000\n.print op v(missing)\n.end\n","op",["out"]);
        Assert.Contains("R1 out 0 1000",deck);Assert.DoesNotContain("v(missing)",deck);Assert.Contains(".print op v(out)",deck);
        Assert.Throws<InvalidDataException>(()=>CorpusPolicy.ObservedDeck("title\n.control\nshell x\n.endc\n.end","op",["out"]));
    }
    [Property(MaxTest=128,Replay="123,20261031")]
    public void MalformedFeatureTextRemainsOnlyHints(string? text)
    {
        var hints=CorpusPolicy.Features(text??"");
        Assert.Equal(hints.Length,hints.Distinct().Count());
    }
    [Property(MaxTest=64,Replay="91,20261031")]
    public void PendingRowsNeverBecomePasses(int value)
    {
        var count=(int)((uint)value%64)+1;
        Assert.Equal(count,CorpusPolicy.Remaining(Enumerable.Range(0,count).Select(i=>new CorpusRow(i.ToString(),"h","tests",[])).ToArray()));
    }
}
