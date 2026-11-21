using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;
using Spice.Starlark;
using Xunit;

namespace Spice.Tests;

public sealed class M1PlatformTests
{
    private sealed class FakeXyceRunner(
        Func<string, IReadOnlyList<string>, string, ExternalProcessResult> handler) : IXyceProcessRunner
    {
        public Task<ExternalProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(executablePath, arguments, workingDirectory));
        }
    }

    [Fact]
    public void DeviceRegistry_FailsClosed_OnUnknownKindMissingPinAndBadParam()
    {
        var unknownKind = new DeviceInstanceIr(
            "X1", "spice.bogus",
            new Dictionary<string, string> { ["p"] = "in", ["n"] = "0" },
            new Dictionary<string, double>());
        Assert.Contains(DeviceRegistry.Default.ValidateInstance(unknownKind), d => d.Code == "unknown_device_kind");

        var missingPin = new DeviceInstanceIr(
            "R1", "spice.resistor",
            new Dictionary<string, string> { ["p"] = "in" },
            new Dictionary<string, double> { ["ohms"] = 1000.0 });
        Assert.Contains(DeviceRegistry.Default.ValidateInstance(missingPin), d => d.Code == "missing_pin");

        var badParam = new DeviceInstanceIr(
            "R2", "spice.resistor",
            new Dictionary<string, string> { ["p"] = "in", ["n"] = "0" },
            new Dictionary<string, double> { ["ohms"] = double.NaN, ["extra"] = 1.0 });
        var errs = DeviceRegistry.Default.ValidateInstance(badParam);
        Assert.Contains(errs, d => d.Code == "non_finite_parameter");
        Assert.Contains(errs, d => d.Code == "unknown_parameter");
    }

    [Fact]
    public void SchematicCompiler_ValidatesErc_EmitsIr_RoundTripsJson_AndExportsSpiceAndSvg()
    {
        var doc = new SchematicDocument(
            SchemaVersion: "2.0",
            Name: "rc_lowpass",
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: "RC Low-Pass Filter",
                Components:
                [
                    new PlacedComponent("V1", "spice.vsrc", 40, 100, 0, new Dictionary<string, double> { ["dc_volts"] = 5.0, ["ac_mag"] = 1.0 }),
                    new PlacedComponent("R1", "spice.resistor", 140, 60, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("C1", "spice.capacitor", 240, 100, 0, new Dictionary<string, double> { ["farads"] = 1e-6 })
                ],
                Nets:
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("C1", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("C1", "n")], IsGlobalGround: true)
                ]));

        var compiled = SchematicCompiler.CompileToIr(doc);
        Assert.True(compiled.Succeeded);
        Assert.NotNull(compiled.Circuit);
        Assert.Equal(["0", "in", "out"], compiled.Circuit!.Nodes);
        Assert.Equal(3, compiled.Circuit.Devices.Count);

        // JSON persistence round-trip
        var json = SchematicCompiler.SerializeJson(doc);
        var restored = SchematicCompiler.DeserializeJson(json);
        Assert.Equal(doc.Name, restored.Name);
        Assert.Equal(doc.RootSheet.Components.Count, restored.RootSheet.Components.Count);

        // SPICE netlist & SVG export
        var spice = SchematicCompiler.ExportSpiceNetlist(doc, AnalysisKind.OperatingPoint);
        Assert.Contains("R1 in out 1000", spice);
        Assert.Contains(".OP", spice);

        var svg = SchematicCompiler.ExportSvg(doc);
        Assert.Contains("<svg ", svg);
        Assert.Contains("R1", svg);

        // ERC catches missing ground and shorted voltage source fail-closed
        var badDoc = doc with
        {
            RootSheet = doc.RootSheet with
            {
                Nets = [new WireNet("short", [new PinRef("V1", "p"), new PinRef("V1", "n")])]
            }
        };
        var badResult = SchematicCompiler.CompileToIr(badDoc);
        Assert.False(badResult.Succeeded);
        Assert.Contains(badResult.Diagnostics, d => d.Code == "missing_ground");
        Assert.Contains(badResult.Diagnostics, d => d.Code == "shorted_voltage_source");
    }

    [Fact]
    public async Task XyceBackend_EmitsDeck_ParsesCsv_AndComparesDifferentially()
    {
        var circuit = new CircuitBuilder("divider")
            .AddVoltageSource("V1", "in", "0", SiUnits.Volts(10.0))
            .AddResistor("R1", "in", "out", SiUnits.KiloOhms(1.0))
            .AddResistor("R2", "out", "0", SiUnits.KiloOhms(1.0))
            .Build();

        string? capturedDeck = null;
        var runner = new FakeXyceRunner((_, args, workDir) =>
        {
            var deckPath = args[0];
            capturedDeck = File.ReadAllText(deckPath);
            File.WriteAllText(deckPath + ".csv", "INDEX,V(IN),V(OUT)\n0,10.0,5.0\n");
            return new ExternalProcessResult(0, "Xyce OK", "");
        });

        using var xyce = new XyceBackend("Xyce", runner);
        var req = new SimulationRequestIr("op_case", circuit, AnalysisKind.OperatingPoint);
        var res = await xyce.RunAsync(req);

        Assert.Equal(RunTerminalStatus.Succeeded, res.Status);
        Assert.True(res.Complete);
        Assert.NotNull(capturedDeck);
        Assert.Contains(".PRINT DC FORMAT=CSV", capturedDeck!);
        Assert.NotNull(res.RealData);
        Assert.Equal(5.0, res.RealData!.SignalValues["v(out)"][0], precision: 12);

        // Differential comparison passes on matching candidate and fails on perturbed candidate
        var okDiff = DifferentialComparer.Compare(res, res);
        Assert.True(okDiff.Passed);

        var perturbed = res with
        {
            RealData = res.RealData with
            {
                SignalValues = new Dictionary<string, IReadOnlyList<double>>
                {
                    ["v(in)"] = [10.0],
                    ["v(out)"] = [4.5]
                }
            }
        };
        var badDiff = DifferentialComparer.Compare(res, perturbed);
        Assert.False(badDiff.Passed);
    }

    [Fact]
    public async Task CampaignRunner_ParallelMap_PreservesDeterministicOrder()
    {
        var runner = new FakeXyceRunner((_, args, _) =>
        {
            var deckPath = args[0];
            var text = File.ReadAllText(deckPath);
            // Parse R2 value directly from deck to return exact divider voltage 10 * R2 / (1000 + R2).
            var r2Line = text.Split('\n').First(l => l.StartsWith("R2 ", StringComparison.Ordinal));
            var r2 = double.Parse(r2Line.Split(' ')[3], System.Globalization.CultureInfo.InvariantCulture);
            var vout = 10.0 * r2 / (1000.0 + r2);
            File.WriteAllText(deckPath + ".csv", $"INDEX,V(IN),V(OUT)\n0,10.0,{vout.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)}\n");
            return new ExternalProcessResult(0, "", "");
        });

        using var xyce = new XyceBackend("Xyce", runner);
        double[] r2Values = [1000.0, 2000.0, 4000.0, 9000.0];

        var results = await CampaignRunner.RunParallelMapAsync(
            r2Values,
            (r2, idx) =>
            {
                var c = new CircuitBuilder($"corner_{idx}")
                    .AddVoltageSource("V1", "in", "0", SiUnits.Volts(10.0))
                    .AddResistor("R1", "in", "out", SiUnits.KiloOhms(1.0))
                    .AddResistor("R2", "out", "0", r2)
                    .Build();
                return new SimulationRequestIr($"case_{idx}", c, AnalysisKind.OperatingPoint);
            },
            xyce,
            maxDegreeOfParallelism: 4);

        Assert.Equal(4, results.Count);
        Assert.Equal(5.0, results[0].RealData!.SignalValues["v(out)"][0], precision: 9);
        Assert.Equal(10.0 * 2.0 / 3.0, results[1].RealData!.SignalValues["v(out)"][0], precision: 9);
        Assert.Equal(8.0, results[2].RealData!.SignalValues["v(out)"][0], precision: 9);
        Assert.Equal(9.0, results[3].RealData!.SignalValues["v(out)"][0], precision: 9);
    }
}
