using System.Diagnostics;
using Simulator.Client;
using Simulator.RequestedQualification;
using Simulator.Syntax;
using Xunit;

namespace Simulator.Frontend.Tests;

public sealed class TransferFunctionTests
{
    [Fact]
    public void PreservesRepeatedTransfersAndRejectsUnknownInput()
    {
        var valid = TransferFunctionSelection.Prepare(Parse(".tf v(out) v1\n.tf v(out,0) v2"));
        Assert.True(valid.Success, valid.Diagnostic?.Code);
        Assert.Equal(new[] { "v1", "v2" }, valid.Operations.Select(operation => operation.InputSource));
        var refused = TransferFunctionSelection.Prepare(Parse(".tf v(out) v1\n.tf v(out) missing"));
        Assert.False(refused.Success);
        Assert.Empty(refused.Operations);
    }

    [BjtOracleTheory]
    [InlineData("lu")]
    [InlineData("faer")]
    public async Task DividerTransferMatchesAnalyticalValues(string backend)
    {
        var plan = TransferFunctionSelection.Prepare(Parse(".tf v(out) v1"), backend);
        Assert.True(plan.Success, plan.Diagnostic?.Code);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var client = new SimulationClient(Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE")!);
        var result = await TransferFunctionSelection.ExecuteAsync(Assert.Single(plan.Operations), client, deadline.Token);
        Assert.True(result.Success, result.Diagnostic?.Code);
        Assert.Equal(2d / 3, result.Gain!.Value, 8);
        Assert.Equal(3000, result.InputOhms!.Value, 5);
        Assert.Equal(2000d / 3, result.OutputOhms!.Value, 5);

        var parent = Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ARTIFACT_ROOT")!;
        Assert.True(Directory.Exists(parent));
        var folder = Path.Combine(parent, $"tf-{backend}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var deck = Path.Combine(folder, "divider.cir");
        var raw = Path.Combine(folder, "divider.raw");
        await File.WriteAllTextAsync(deck, Deck(".tf v(out) v1"), deadline.Token);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SPICE_NGSPICE")!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = folder
        };
        foreach (var argument in new[] { "-n", "-D", "filetype=ascii", "-b", "-r", raw, "-o", Path.Combine(folder, "native.log"), deck })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.Equal(0, process.ExitCode);
        var plot = AsciiRaw.Read(await File.ReadAllTextAsync(raw, deadline.Token));
        Assert.Equal("Transfer Function", plot.PlotName);
        Assert.False(plot.Complex);
        Assert.Equal(new[] { "v(Transfer_function)", "v(v1#Input_impedance)", "v(output_impedance_at_V(out))" },
            plot.Variables.Select(variable => variable.Name), StringComparer.OrdinalIgnoreCase);
        var values = Assert.Single(plot.Points);
        Assert.Equal(values[0].Real, result.Gain.Value, 8);
        Assert.Equal(values[1].Real, result.InputOhms.Value, 5);
        Assert.Equal(values[2].Real, result.OutputOhms.Value, 5);
    }

    [Fact]
    public void SelectedDcTransferAdmitsForwardEarlyVoltageWithoutEnablingOtherAnalyses()
    {
        var syntax = DeckParser.Parse(
            "Early effect\nVin b 0 .6\nVcc s 0 5\nR1 s c 1k\nQ1 c b 0 qm\n.model qm NPN(VA=50)\n.tf v(c) Vin\n.end\n",
            collectDeclarations: true);
        Assert.True(TransferFunctionSelection.Prepare(syntax).Success);
        Assert.False(OperatingPointSelection.Select(syntax).Success);
    }

    [BjtOracleTheory]
    [InlineData("NPN", 1)]
    [InlineData("PNP", -1)]
    public async Task ForwardEarlyEffectScalesOnlyTransport(string family, int polarity)
    {
        var biasVolts = polarity == 1 ? ".6" : "-.6";
        var syntax = DeckParser.Parse(
            $"Early effect\nVin b 0 {biasVolts}\nVcc c 0 {polarity * 5}\nQ1 c b 0 qm\n.model qm {family}(VA=50)\n.end\n",
            collectDeclarations: true);
        var prepared = SelectedDcBias.Prepare(syntax, "lu");
        Assert.True(prepared.Success, string.Join(",", prepared.Diagnostics.Select(d => d.Code)));
        var client = new SimulationClient(Environment.GetEnvironmentVariable("SPICE_SEMICONDUCTOR_ENGINE")!);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var bias = new SelectedDcBias(prepared.EarlyEffects);
        var response = await bias.SolveAsync(client, prepared.Request!, deadline.Token);
        Assert.True(response is { Succeeded: true });
        var manifest = response.ReadManifest();
        double Current(string name)
        {
            var id = prepared.Origins.Single(o => o.LocalName == name).WireId;
            return Assert.Single(manifest.Samples).Values[Array.FindIndex(manifest.Signals, s => s.Id == $"i({id})")];
        }
        const double vt = 1.38064852e-23 / 1.6021766208e-19 * 300.15;
        var transport = 1e-16 * Math.Exp(.6 / vt);
        Assert.InRange(-polarity * Current("Vcc") / transport, 1.0879, 1.0881);
        Assert.InRange(-polarity * Current("Vin") / transport, .00999, .01001);
    }

    private static DeckSyntax Parse(string directives) => DeckParser.Parse(Deck(directives), collectDeclarations: true);

    private static string Deck(string directives) =>
        $"Divider\nV1 in 0 2\nV2 other 0 0\nR1 in out 1k\nR2 out other 2k\n{directives}\n.end\n";
}
