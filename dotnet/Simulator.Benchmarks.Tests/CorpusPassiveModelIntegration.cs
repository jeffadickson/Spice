using Simulator.Benchmarks;
using Xunit;

public sealed partial class CorpusNumericalIntegration
{
    [CorpusOracleFact]
    public async Task NamedResistorModelMatchesOhmsLawAndNgspice()
    {
        foreach(var resistance in new[]{2000,4000})
        foreach(var analysis in new[]{".op",".ac lin 1 100 100",".tran 1m 2m"})
        {
            var ac=analysis.StartsWith(".ac",StringComparison.Ordinal);
            var source=ac?"DC 2 AC 1":"2";
            var expected=-(ac?1d:2d)/resistance;
            var (report,_) = await Run($"owned\nV1 out 0 {source}\nR1 out 0 rm\n.model RM R (R={resistance})\n{analysis}\n.end");
            Assert.Equal("compared_pass",Assert.Single(report.Rows).Status);
            foreach(var backend in report.Rows[0].Backends)
            {
                var current=Assert.Single(backend.Signals,s=>s.Observable=="i(V1)");
                Assert.Equal("A",current.Unit);
                Assert.InRange(current.ActualReal,expected-1e-10,expected+1e-10);
                Assert.InRange(current.ExpectedReal,expected-1e-10,expected+1e-10);
                AssertConsistentWitness(current);
            }
        }
    }
}
