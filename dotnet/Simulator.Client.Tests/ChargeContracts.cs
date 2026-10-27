using Simulator.Client;
using System.Text.Json;
using Xunit;

/// <summary>Independent wire and real-engine equations for the bounded composite model.</summary>
public sealed class ChargeContracts
{
    [Fact]
    public void LegacyAndCompositeWireShapesRemainDistinct()
    {
        foreach (var composite in new[] { false, true })
        {
            Element diode = composite ? new DiodeWithCapacitance("D1", 1, 0, 1e-12, 0.025, 1e-6)
                : new Diode("D1", 1, 0, 1e-12, 0.025);
            var request = new SimulationRequest("shape", new Circuit(["0", "out"], [diode]), new OperatingPoint());
            var json = JsonSerializer.Serialize(request, WireFormat.Options);
            var decoded = JsonSerializer.Deserialize<SimulationRequest>(json, WireFormat.Options)!;
            Assert.Equal(diode, decoded.Circuit.Elements[0]);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(composite, document.RootElement.GetProperty("circuit").GetProperty("elements")[0].TryGetProperty("farads", out _));
        }
    }

    [Theory]
    [InlineData("lu")]
    [InlineData("faer")]
    public async Task TypedChargeMatchesDcAcAndBackwardEulerEquations(string backend)
    {
        var root = Environment.GetEnvironmentVariable("SPICE_TEST_ROOT");
        if (root is null)
            for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
                if (File.Exists(Path.Combine(path.FullName, "Cargo.toml"))) { root = path.FullName; break; }
        Assert.NotNull(root);
        var executable = Environment.GetEnvironmentVariable("SPICE_TEST_EXECUTABLE") ??
            Path.Combine(root, "target", "debug", "circuit-simulator" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var circuit = new Circuit(["0", "out"], [new Voltage("V1", 1, 0, 0.1),
            new DiodeWithCapacitance("D1", 1, 0, 1e-12, 0.025, 1e-6)]);
        var client = new SimulationClient(executable);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dc = (await client.RunAsync(new("charge-dc", circuit, new OperatingPoint(), backend), limit.Token)).ReadManifest();
        var conduction = 1e-12 * (Math.Exp(4) - 1);
        Assert.InRange(Math.Abs(dc.Samples[0].Values[1] + conduction), 0, 1e-15);
        Assert.Contains(dc.Signals, signal => signal.Id == "i_conduction(D1)");
        var ac = (await client.RunAsync(new("charge-ac", circuit, new Ac([100], [new("V1", 1, 0)]), backend), limit.Token)).ReadManifest();
        var branch = ac.AcSamples[0].Values[1];
        Assert.InRange(Math.Abs(branch.Real + 1e-12 * Math.Exp(4) / 0.025), 0, 1e-14);
        Assert.InRange(Math.Abs(branch.Imag + 2 * Math.PI * 100 * 1e-6), 0, 1e-14);
        var be = (await client.RunAsync(new("charge-be", circuit, new Transient(new(0.1, 0.1, 1)), backend), limit.Token)).ReadManifest();
        Assert.InRange(Math.Abs(be.Samples[0].Values[1] + conduction + 1e-6), 0, 1e-14);
    }
}
