using Simulator.Client;
using System.Text.Json;
using Xunit;

/// <summary>Public client contracts; the native engine is an external immutable test dependency.</summary>
public sealed class ClientContracts
{
    [Fact]
    public async Task RealEngineConformance()
    {
        var root = Environment.GetEnvironmentVariable("SPICE_TEST_ROOT") ?? FindRoot();
        var executable = Environment.GetEnvironmentVariable("SPICE_TEST_EXECUTABLE") ??
            Path.Combine(root,"target","debug","circuit-simulator" + (OperatingSystem.IsWindows()?".exe":""));
        Assert.Equal(29, await ConformanceChecks.Run(executable, root));
    }

    [Theory]
    [InlineData("succeeded",true,true)]
    [InlineData("succeeded",false,false)]
    [InlineData("failed",true,false)]
    [InlineData("failed",false,false)]
    public void CompletionRequiresBothFields(string status, bool complete, bool expected)
    {
        using var payload=JsonDocument.Parse("{}");
        var response=new SimulationResponse("1",status,complete,payload.RootElement.Clone());
        Assert.Equal(expected,response.Succeeded);
        Assert.Null(response.Error);
        if(!expected) Assert.Throws<InvalidOperationException>(()=>response.ReadManifest());
    }

    [Fact]
    public async Task RejectOversizedInputBeforeStartingProcess()
    {
        var client=new SimulationClient("nonexistent-engine");
        await Assert.ThrowsAsync<ArgumentException>(()=>client.RunJsonAsync(new string('x',1_048_577)));
        // UTF-8 byte bounds must also reject multibyte input shorter in characters.
        await Assert.ThrowsAsync<ArgumentException>(()=>client.RunJsonAsync(new string('é',524_289)));
    }

    private static string FindRoot()
    {
        for(var path=new DirectoryInfo(AppContext.BaseDirectory);path is not null;path=path.Parent)
            if(File.Exists(Path.Combine(path.FullName,"Cargo.toml"))) return path.FullName;
        throw new InvalidOperationException("Set SPICE_TEST_ROOT to the tested source snapshot.");
    }
}
