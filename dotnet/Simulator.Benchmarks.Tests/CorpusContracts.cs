using Simulator.Benchmarks;
using Xunit;
using FsCheck.Xunit;

public sealed class CorpusContracts
{
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
