using Simulator.Client;
using System.Text.Json;
using Xunit;

/// <summary>Typed source-generated AC wire contracts and real-engine equations.</summary>
public sealed class AcContracts
{
    [Fact]
    public void HistoricalConstructorAndDeconstructionSignaturesRemainPresent()
    {
        double[] frequencies = [10, 100];
        var constructor = typeof(Ac).GetConstructor([typeof(double[])]);
        Assert.NotNull(constructor);
        var ac = Assert.IsType<Ac>(constructor.Invoke([frequencies]));
        ac.Deconstruct(out var actual);
        Assert.Same(frequencies, actual);
        Assert.Null(ac.SourcePhasors);
        var request = Request(ac);
        var decoded = JsonSerializer.Deserialize<SimulationRequest>(JsonSerializer.Serialize(request, WireFormat.Options), WireFormat.Options)!;
        Assert.Equal(frequencies, Assert.IsType<Ac>(decoded.Analysis).FrequenciesHz);
    }
    [Fact]
    public void OptionalPhasorsRetainHistoricalWireShape()
    {
        var request = Request(new Ac([1.0]));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(request, WireFormat.Options));
        var analysis = document.RootElement.GetProperty("analysis");
        Assert.Equal("ac", analysis.GetProperty("kind").GetString());
        Assert.False(analysis.TryGetProperty("source_phasors", out _));
    }

    [Theory]
    [InlineData("lu")]
    [InlineData("faer")]
    public async Task TypedComplexOverrideMatchesAnalyticalRcResponse(string backend)
    {
        var root = Environment.GetEnvironmentVariable("SPICE_TEST_ROOT") ?? FindRoot();
        var executable = Environment.GetEnvironmentVariable("SPICE_TEST_EXECUTABLE") ??
            Path.Combine(root, "target", "debug", "circuit-simulator" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var frequency = 1.0 / (2.0 * Math.PI * 0.001);
        var request = Request(new Ac([frequency], [new AcSourcePhasor("V1", 1.0, 2.0)])) with { Backend = backend };
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var manifest = (await new BoundedTestClient(executable).RunAsync(request, limit.Token)).ReadManifest();
        var index = Array.FindIndex(manifest.Signals, signal => signal.Id == "v(out)");
        Assert.True(index >= 0);
        var value = Assert.Single(manifest.AcSamples).Values[index];
        // (1+2j)/(1+j) = 1.5+0.5j.
        Assert.InRange(Math.Abs(value.Real - 1.5), 0.0, 1e-12);
        Assert.InRange(Math.Abs(value.Imag - 0.5), 0.0, 1e-12);
    }

    private static SimulationRequest Request(Ac analysis) => new("typed-ac",
        new Circuit(["0", "in", "out"], [new Voltage("V1", 1, 0, 1e308),
            new Resistor("R1", 1, 2, 1000), new Capacitor("C1", 2, 0, 1e-6)]), analysis);

    private static string FindRoot()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "Cargo.toml"))) return path.FullName;
        throw new InvalidOperationException("Set SPICE_TEST_ROOT to the source snapshot.");
    }
}
