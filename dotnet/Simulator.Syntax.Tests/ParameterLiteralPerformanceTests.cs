using System.Diagnostics.Metrics;
using Xunit;
namespace Simulator.Syntax.Tests;

/// <summary>Numeric fast-path work and whole-input semantics, without flaky wall-time assertions.</summary>
public sealed class ParameterLiteralPerformanceTests
{
    private static (DeclarationCatalog Catalog,ParameterEvaluation Environment) Context()
    {
        var catalog=DeckParser.Parse("Parameters\n.end\n",collectDeclarations:true).Declarations!;
        return (catalog,ParameterEvaluator.Evaluate(catalog));
    }
    private static ScalarEvaluation Evaluate(DeclarationCatalog catalog,ParameterEvaluation environment,string text)
        =>ParameterEvaluator.EvaluateValue(catalog,environment,new(text,new(0,text.Length,1,1)));

    private sealed class ParseCounter:IDisposable
    {
        private readonly MeterListener listener=new();
        private readonly int thread=Environment.CurrentManagedThreadId;
        internal long Count;
        internal bool Seen;
        internal ParseCounter()
        {
            listener.InstrumentPublished=(instrument,sink)=>{
                if(instrument.Meter.Name=="Simulator.Syntax.Parameters"&&instrument.Name=="parameter.synthetic_expression_preparations"){
                    Seen=true;sink.EnableMeasurementEvents(instrument);
                }
            };
            // Evaluations are synchronous: filter unrelated parallel test work.
            listener.SetMeasurementEventCallback<long>((_,value,_,_)=>{
                if(Environment.CurrentManagedThreadId==thread)Count+=value;
            });
            listener.Start();
        }
        public void Dispose()=>listener.Dispose();
    }
    [Fact]
    public void FullExpressionWorkHasAStandardDiagnosticCounter()
    {
        var (catalog,environment)=Context();using var counter=new ParseCounter();
        Assert.Equal(3,Evaluate(catalog,environment,"1+2").Value);
        Assert.True(counter.Seen);Assert.Equal(1,counter.Count);
    }
    [Theory]
    [InlineData("1M",.001)][InlineData("-4.5u",-4.5e-6)][InlineData("+1e3",1000d)]
    [InlineData("{1.25k}",1250d)][InlineData("'2mil'",.0000508)][InlineData("1pF",1e-12)]
    public void WholeNumericLiteralsDoNotPrepareSyntheticDecks(string text,double expected)
    {
        var (catalog,environment)=Context();using var counter=new ParseCounter();
        var result=Evaluate(catalog,environment,text);Assert.Equal(expected,result.Value);
        Assert.True(counter.Seen);Assert.Equal(0,counter.Count);
    }
    [Theory][InlineData("1@")][InlineData("@1")][InlineData("1@2")][InlineData("1m 2")][InlineData("1+missing")]
    public void PartialOrMalformedNumericTextNeverBecomesAPrefixValue(string text)
    {
        var (catalog,environment)=Context();var result=Evaluate(catalog,environment,text);
        Assert.Null(result.Value);Assert.NotEmpty(result.Diagnostics);
    }
    [Fact]
    public void NumericLiteralCannotBypassEnvironmentBrandingOrFiniteChecks()
    {
        var (catalog,environment)=Context();
        Assert.Null(Evaluate(catalog,environment with{Values=environment.Values.Add("bad",double.NaN)},"1").Value);
        Assert.Null(Evaluate(catalog,environment,"1e309").Value);
        Assert.Null(Evaluate(catalog,environment,"1e308T").Value);
    }
    [FsCheck.Xunit.Property(MaxTest=128,Replay="2164,20261117")]
    public bool NumericLiteralMatchesTheFullGrammarPath(int mantissa,int exponent)
    {
        var (catalog,environment)=Context();
        var text=FormattableString.Invariant($"{mantissa%1000}e{exponent%50}u");
        var direct=Evaluate(catalog,environment,text);var grammar=Evaluate(catalog,environment,"("+text+")+0");
        return direct.Diagnostics.IsEmpty&&grammar.Diagnostics.IsEmpty&&direct.Value==grammar.Value;
    }
    [Fact]
    public void LiteralFunctionBodiesPreserveTheSharedExpressionBudget()
    {
        // Each function body stays small; expansion consumes the shared budget.
        var text="Functions\n.func f0() {1}\n";
        for(var i=1;i<=12;i++)text+=$".func f{i}() {{f{i-1}()+f{i-1}()}}\n";
        var catalog=DeckParser.Parse(text+".end\n",collectDeclarations:true).Declarations!;
        var environment=ParameterEvaluator.Evaluate(catalog);
        Assert.Equal(2048,Evaluate(catalog,environment,"f11()").Value);
        Assert.Null(Evaluate(catalog,environment,"f12()").Value);
    }
}
