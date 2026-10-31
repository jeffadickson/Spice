using System.Security.Cryptography;
using System.Text;
using Simulator.Benchmarks;
using Xunit;

/// <summary>Real external-oracle tests opt in explicitly; missing tools are skips.</summary>
public sealed class CorpusOracleFactAttribute : FactAttribute
{
    public CorpusOracleFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")) ||
           !File.Exists(Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")))
            Skip="Set SPICE_CORPUS_ENGINE and SPICE_CORPUS_NGSPICE to run real-oracle integration.";
    }
}

public sealed partial class CorpusNumericalIntegration
{
    [CorpusOracleFact]
    public async Task ExplicitTransientMaximumStepMatchesConstantOracle()
    {
        var (report,_) = await Run("owned\nV1 out 0 1\nR1 out 0 1000\n.tran 1m 5m 0 250u\n.end");
        Assert.Equal("compared_pass",Assert.Single(report.Rows).Status);
        foreach(var backend in report.Rows[0].Backends)
        {
            var signal=Assert.Single(backend.Signals,s=>s.Observable=="i(V1)");
            Assert.InRange(signal.ActualReal,-0.00100000001,-0.00099999999);
            Assert.InRange(signal.ExpectedReal,-0.00100000001,-0.00099999999);
            AssertConsistentWitness(signal);
        }
    }
    [CorpusOracleFact]
    public async Task InductorAndControlledVoltageBranchesMatchIndependentEquations()
    {
        var (rl,_) = await Run("owned\nV1 in 0 DC 0 AC 1\nR1 in out 1000\nL1 out 0 1\n.ac lin 1 100 100\n.end");
        Assert.Equal("compared_pass",Assert.Single(rl.Rows).Status);
        var expected=System.Numerics.Complex.One/new System.Numerics.Complex(1000,2*Math.PI*100);
        foreach(var backend in rl.Rows[0].Backends)
        {
            var current=Assert.Single(backend.Signals,s=>s.Observable=="i(L1)");
            Assert.InRange(current.ActualReal,expected.Real-1e-10,expected.Real+1e-10);
            Assert.InRange(current.ActualImag,expected.Imaginary-1e-10,expected.Imaginary+1e-10);
            AssertConsistentWitness(current);
        }
        foreach(var (source,id,current) in new[]{("E1 out 0 in 0 2","E1",-0.002),("H1 out 0 V1 1000","H1",0.001)})
        {
            var (report,_) = await Run($"owned\nV1 in 0 1\nRin in 0 1000\n{source}\nRout out 0 1000\n.op\n.end");
            Assert.Equal("compared_pass",Assert.Single(report.Rows).Status);
            foreach(var backend in report.Rows[0].Backends)
            {
                var signal=Assert.Single(backend.Signals,s=>s.Observable==$"i({id})");
                Assert.InRange(signal.ActualReal,current-1e-10,current+1e-10);
                AssertConsistentWitness(signal);
            }
        }
    }
    [CorpusOracleFact]
    public async Task InactiveAcMetadataMatchesDcAndTransientOracle()
    {
        foreach(var source in new[]{"V1 out 0 DC 2 AC 3 45","I1 out 0 DC 2m AC 3m 45"})
        foreach(var analysis in new[]{".op",".tran 1m 2m"})
        {
            var expected=source[0]=='V'?2d:-2d;
            var (report,_)=await Run($"owned\n{source}\nR1 out 0 1000\n{analysis}\n.end");
            var row=Assert.Single(report.Rows);Assert.Equal("compared_pass",row.Status);
            foreach(var backend in row.Backends)
            {
                var voltage=Assert.Single(backend.Signals,s=>s.Observable=="v(out)");
                Assert.InRange(voltage.ActualReal,expected-1e-10,expected+1e-10);
                Assert.InRange(voltage.ExpectedReal,expected-1e-10,expected+1e-10);
            }
        }
    }
    [CorpusOracleFact]
    public async Task AttoMagnitudeMatchesOracleWithoutAnAbsoluteFloorMaskingZero()
    {
        foreach(var (source,expected) in new[]{("V1 out 0 DC 0 AC 1A",1e-18),("I1 out 0 DC 0 AC 1a",-1e-15),("I1 out 0 DC 0 AC 2mA",-2d)})
        {
            var (report,_)=await Run($"owned\n{source}\nR1 out 0 1000\n.ac lin 1 10 10\n.end");
            var row=Assert.Single(report.Rows);Assert.Equal("compared_pass",row.Status);
            foreach(var backend in row.Backends)
            {
                var voltage=Assert.Single(backend.Signals,s=>s.Observable=="v(out)");
                // Relative-to-known-nonzero assertions supplement the corpus
                // absolute floor, which alone could accept zero at this scale.
                Assert.InRange(voltage.ActualReal/expected,1-1e-9,1+1e-9);
                Assert.InRange(voltage.ExpectedReal/expected,1-1e-9,1+1e-9);
                if(source[0]=='V')
                {
                    var current=Assert.Single(backend.Signals,s=>s.Observable=="i(V1)");
                    Assert.InRange(current.ActualReal/(-1e-21),1-1e-9,1+1e-9);
                    Assert.InRange(current.ExpectedReal/(-1e-21),1-1e-9,1+1e-9);
                }
            }
        }
    }
    [CorpusOracleFact]
    public async Task RealOracleComparesOpAcAndTransientOnBothCpuProviders()
    {
        foreach(var analysis in new[]{".op",".ac lin 3 1 1000",".tran 1ns 6ns"})
        foreach(var resistance in new[]{1000,2000})
        {
            var source=analysis.StartsWith(".ac",StringComparison.Ordinal)?"DC 1 AC 1":"1";
            var (report,_) = await Run($"title\nV1 in 0 {source}\nR1 in out {resistance}\nR2 out 0 {resistance}\n"+analysis+"\n.end");
            Assert.Equal("completed_with_results",report.State);
            var row=Assert.Single(report.Rows);Assert.Equal("compared_pass",row.Status);
            Assert.Equal(2,row.Backends.Count);
            foreach(var backend in row.Backends)
            {
                Assert.Equal("compared_pass",backend.Status);
                var output=Assert.Single(backend.Signals,s=>s.Observable=="v(out)");
                Assert.Equal(0,output.FailedPoints);
                Assert.InRange(output.ActualReal,0.499999,0.500001);
                Assert.InRange(output.ExpectedReal,0.499999,0.500001);
                // Identical divider voltages cannot establish conductance.
                // Independent KCL/Ohm current must change with resistance.
                var current=Assert.Single(backend.Signals,s=>s.Observable=="i(V1)");
                Assert.Equal("A",current.Unit);Assert.Equal(1e-9,current.AbsoluteTolerance);
                var expected=-1d/(2*resistance);
                Assert.InRange(current.ActualReal,expected-1e-10,expected+1e-10);
                Assert.InRange(current.ExpectedReal,expected-1e-10,expected+1e-10);
                Assert.Equal(0,current.FailedPoints);
                AssertConsistentWitness(current);
            }
        }
    }

    [CorpusOracleFact]
    public async Task ComplexAcSourceCurrentMatchesIndependentRcAdmittance()
    {
        var (report,_)=await Run("title\nV1 in 0 DC 0 AC 1\nR1 in out 1000\nC1 out 0 1u\n.ac lin 3 1 1000\n.end");
        var row=Assert.Single(report.Rows);Assert.Equal("compared_pass",row.Status);
        foreach(var backend in row.Backends)
        {
            var current=Assert.Single(backend.Signals,s=>s.Observable=="i(V1)");
            Assert.Equal("A",current.Unit);Assert.Equal(1e-9,current.AbsoluteTolerance);
            var jwC=new System.Numerics.Complex(0,2*Math.PI*current.WorstCoordinate*1e-6);
            var expected=-jwC/(1+1000*jwC);
            Assert.InRange(current.ActualReal,expected.Real-1e-10,expected.Real+1e-10);
            Assert.InRange(current.ActualImag,expected.Imaginary-1e-10,expected.Imaginary+1e-10);
            Assert.InRange(current.ExpectedReal,expected.Real-1e-8,expected.Real+1e-8);
            Assert.InRange(current.ExpectedImag,expected.Imaginary-1e-8,expected.Imaginary+1e-8);
            Assert.Equal(0,current.FailedPoints);
            AssertConsistentWitness(current);
        }
    }

    [CorpusOracleFact]
    public async Task UnrepresentableObservationStopsBeforeAnyOracleDeckIsWritten()
    {
        // The core accepts this node, but it is outside the explicitly bounded
        // observation identifier profile. Fail visibly rather than rewrite it.
        var (report,raw)=await Run("title\nV1 bad+node 0 1\nR1 bad+node 0 1000\n.op\n.end");
        var row=Assert.Single(report.Rows);
        Assert.Equal("comparison_unavailable",row.Status);
        Assert.False(string.IsNullOrWhiteSpace(row.Diagnostic));
        Assert.Empty(row.Backends);
        Assert.Empty(Directory.GetFiles(raw,"observed.cir",SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(raw,"ngspice.log",SearchOption.AllDirectories));
    }

    [CorpusOracleFact]
    public async Task CoarseTransientRetainsNumericalFailureAndWitnesses()
    {
        // One backward-Euler step deliberately differs from ngspice's refined
        // ramp integration. A completed oracle run must not hide the mismatch.
        var (report,_)=await Run("title\nV1 in 0 PWL(0,0,1m,1)\nR1 in out 1k\nC1 out 0 1u\n.tran 1m 1m\n.end");
        var row=Assert.Single(report.Rows);Assert.Equal("comparison_failed",row.Status);
        Assert.Equal(2,row.Backends.Count);
        foreach(var backend in row.Backends)
        {
            Assert.Equal("comparison_failed",backend.Status);
            var current=Assert.Single(backend.Signals,s=>s.Observable=="i(V1)");
            Assert.True(current.FailedPoints>0);Assert.True(current.Points>=current.FailedPoints);
            Assert.True(current.MaximumAbsoluteDifference>current.AbsoluteTolerance);
            Assert.NotEqual(current.ExpectedReal,current.ActualReal);
            Assert.Equal(1e-3,current.WorstCoordinate);
            Assert.Equal("A",current.Unit);
            AssertConsistentWitness(current);
        }
    }

    private static void AssertConsistentWitness(CorpusSignal signal)
    {
        // For these bounded magnitudes, an independent Euclidean formula is
        // safe from overflow and checks that the stored error matches its data.
        var real=signal.ExpectedReal-signal.ActualReal;
        var imaginary=signal.ExpectedImag-signal.ActualImag;
        var expected=Math.Sqrt(real*real+imaginary*imaginary);
        Assert.InRange(signal.MaximumAbsoluteDifference,expected-1e-14,expected+1e-14);
    }

    private static async Task<(CorpusReport Report,string Raw)> Run(string deck)
    {
        var root=Environment.GetEnvironmentVariable("SPICE_CORPUS_TEST_ROOT")??Path.GetTempPath();
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        var folder=Path.Combine(root,"corpus-oracle-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var input=Path.Combine(folder,"fixture.cir");
        File.WriteAllText(input,deck,new UTF8Encoding(false));
        var raw=Path.Combine(folder,"raw");Directory.CreateDirectory(raw);
        var report=new CorpusReport{CorpusRoot=folder,RawDirectory=raw};
        report.Rows.Add(new("fixture.cir",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant(),"owned-fixture",[]));
        var checkpoints=0;
        await CorpusRunner.Run(report,Environment.GetEnvironmentVariable("SPICE_CORPUS_ENGINE")!,
            Environment.GetEnvironmentVariable("SPICE_CORPUS_NGSPICE")!,120,CancellationToken.None,()=>checkpoints++);
        Assert.True(checkpoints>0);
        return(report,raw);
    }
}
