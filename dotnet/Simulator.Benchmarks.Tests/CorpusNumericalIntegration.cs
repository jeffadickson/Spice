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

public sealed class CorpusNumericalIntegration
{
    [CorpusOracleFact]
    public async Task RealOracleComparesOpAcAndTransientOnBothCpuProviders()
    {
        foreach(var analysis in new[]{".op",".ac lin 3 1 1000",".tran 1ns 6ns"})
        {
            var source=analysis.StartsWith(".ac",StringComparison.Ordinal)?"DC 1 AC 1":"1";
            var (report,_) = await Run($"title\nV1 in 0 {source}\nR1 in out 1000\nR2 out 0 1000\n"+analysis+"\n.end");
            Assert.Equal("completed_with_results",report.State);
            var row=Assert.Single(report.Rows);Assert.Equal("compared_pass",row.Status);
            Assert.Equal(2,row.Backends.Count);
            foreach(var backend in row.Backends)
            {
                Assert.Equal("compared_pass",backend.Status);
                var output=Assert.Single(backend.Signals.Where(s=>s.Observable=="v(out)"));
                Assert.Equal(0,output.FailedPoints);
                Assert.InRange(output.ActualReal,0.499999,0.500001);
                Assert.InRange(output.ExpectedReal,0.499999,0.500001);
            }
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
