using System.Diagnostics.Metrics;
using Xunit;
namespace Simulator.Syntax.Tests;

/// <summary>Recognition work is counted deterministically; no wall-time assertion or filesystem cache.</summary>
public sealed class ResourceRecognitionReuseTests
{
    private sealed class Counter:IDisposable
    {
        private readonly MeterListener listener=new();
        private readonly int thread=Environment.CurrentManagedThreadId;
        internal long Count;
        internal Counter()
        {
            listener.InstrumentPublished=(instrument,sink)=>{
                if(instrument.Meter.Name=="Simulator.Syntax.Recognition"&&instrument.Name=="syntax.deck_preparations")sink.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((_,value,_,_)=>{if(Environment.CurrentManagedThreadId==thread)Count+=value;});
            listener.Start();
        }
        public void Dispose()=>listener.Dispose();
    }
    private sealed class Files:IDisposable
    {
        internal string Root{get;}
        internal Files()
        {
            var parent=Environment.GetEnvironmentVariable("CODEX_ARTIFACT_ROOT")??Path.GetTempPath();
            if(!Directory.Exists(parent))throw new DirectoryNotFoundException(parent);
            Root=Path.Combine(parent,"recognition-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        }
        internal string Put(string name,string text){var path=Path.Combine(Root,name);File.WriteAllText(path,text);return path;}
        public void Dispose()=>Directory.Delete(Root,true);
    }
    [Fact]
    public void UnchangedRootIsRecognizedOnceAndRetainsIdenticalDeclarations()
    {
        using var files=new Files();var text="Divider\nV1 in 0 1\nR1 in out 1k\nR2 out 0 2k\n.op\n.end\n\r\n \t";
        var path=files.Put("root.cir",text);using var counter=new Counter();
        var resolved=ResourceResolver.Resolve(path,new([files.Root]));
        var first=resolved.Parse(collectDeclarations:true);var second=resolved.Parse(collectDeclarations:true);
        Assert.Equal("accepted_structure",first.Status);Assert.Equal(text,resolved.Text);
        Assert.Equal(1,counter.Count);Assert.Same(first,second);
        var independent=DeckParser.Parse(text,collectDeclarations:true);
        Assert.Equal(independent.Statements.ToArray(),first.Statements.ToArray());Assert.Equal(3,first.Declarations!.Primitives.Length);
    }
    [Fact]
    public void ChangedTextLimitsAndMetadataCannotReuseAnIncompatibleRecognition()
    {
        using var files=new Files();var path=files.Put("root.cir","Root\nR1 a 0 1k\n.end\n");
        var resolved=ResourceResolver.Resolve(path,new([files.Root]));
        Assert.Equal("resource_rejected",resolved.Parse(new(maxTokens:1),collectDeclarations:true).Status);
        Assert.Equal("resource_rejected",resolved.Parse(new(maxInputCharacters:1),collectDeclarations:true).Status);
        Assert.NotSame(resolved.Parse(collectDeclarations:true),resolved.Parse(new(useSllFirst:false),collectDeclarations:true));
        Assert.Equal("syntax_rejected",(resolved with{Text="Root\nR1\n.end\n"}).Parse(collectDeclarations:true).Status);
        Assert.NotEmpty(resolved.Parse(collectFeatures:true,collectDeclarations:true).Features);
        Assert.Null(resolved.Parse().Declarations);
        Assert.Throws<InvalidOperationException>(()=>(resolved with{Text=null}).Parse());
        files.Put("root.cir","Root\nR2 b 0 2k\n.end\n");
        var next=ResourceResolver.Resolve(path,new([files.Root])).Parse(collectDeclarations:true);
        Assert.Equal("R2",Assert.Single(next.Declarations!.Primitives).Name);
        Assert.Equal("R1",Assert.Single(resolved.Parse(collectDeclarations:true).Declarations!.Primitives).Name);
    }
    [Theory][InlineData("\n")][InlineData("\r\n")][InlineData("\r")]
    public void IncludesAndTrimmedRootTailsUseExpandedPhysicalRanges(string newline)
    {
        using var files=new Files();files.Put("part.inc","R1 a 0 1k"+newline);
        var path=files.Put("root.cir",string.Join(newline,"Root",".include 'part.inc'",".op",".end","* trailing comment",""));
        var resolved=ResourceResolver.Resolve(path,new([files.Root]));Assert.True(resolved.Success);
        var actual=resolved.Parse(collectDeclarations:true);var reference=DeckParser.Parse(resolved.Text!,collectDeclarations:true);
        Assert.Equal(reference.Statements.ToArray(),actual.Statements.ToArray());Assert.Single(actual.Declarations!.Primitives);
        Assert.Equal(2,resolved.Sources.Length);Assert.DoesNotContain("trailing comment",resolved.Text!);
    }
    [FsCheck.Xunit.Property(MaxTest=32,Replay="2164,20261119")]
    public bool ReusedSyntaxMatchesIndependentRecognitionAcrossValuesAndPadding(int value,byte padding)
    {
        using var files=new Files();
        var text=FormattableString.Invariant($"Root\nR1 a 0 {1+Math.Abs(value%10000)}\n.op\n.end\n")+new string(' ',padding%24);
        var path=files.Put("root.cir",text);var resolved=ResourceResolver.Resolve(path,new([files.Root]));
        var reused=resolved.Parse(collectDeclarations:true);var reference=DeckParser.Parse(text,collectDeclarations:true);
        return resolved.Text==text&&reused.Status==reference.Status&&reused.Statements.SequenceEqual(reference.Statements)
            &&reused.Declarations!.Scopes.SequenceEqual(reference.Declarations!.Scopes)
            &&reused.Declarations.Primitives.Select(p=>p.Value?.Text).SequenceEqual(reference.Declarations.Primitives.Select(p=>p.Value?.Text));
    }
    [Fact]
    public void IncludeExpansionKeepsItsEstablishedBlankTailBudget()
    {
        using var files=new Files();files.Put("part.inc","R1 a 0 1k\nR2 b 0 2k\n");
        var text="Root\n.include 'part.inc'\n.op\n.end\n"+new string(' ',40);
        var path=files.Put("root.cir",text);
        var limits=new ParserLimits(maxInputCharacters:text.Length);
        var result=ResourceResolver.Resolve(path,new([files.Root]){ParsingLimits=limits});
        Assert.True(result.Success);Assert.False(result.Text!.EndsWith(' '));
        Assert.Equal("accepted_structure",result.Parse(limits,collectDeclarations:true).Status);
    }
}
