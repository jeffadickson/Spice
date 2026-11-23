using System.Net.Http.Json;
using System.Text.Json;
using Spice.App;
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
        Assert.Contains("class=\"spice-snap-grid\"", svg);
        Assert.Contains("id=\"snap-v\"", svg);
        Assert.Contains("id=\"snap-h\"", svg);
        Assert.Contains("id=\"wire-preview\"", svg);
        Assert.Contains("class=\"spice-wire-path\"", svg);
        Assert.Contains("class=\"spice-pin\"", svg);
        Assert.Contains("R1", svg);

        // OperatingPointOverlay formats DC node voltages and Ohm's-law branch currents directly onto the SVG
        var dividerDoc = new SchematicDocument(
            SchemaVersion: "2.0",
            Name: "divider_op_overlay",
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: "Divider DC Overlay",
                Components:
                [
                    new PlacedComponent("V1", "spice.vsrc", 80, 140, 270, new Dictionary<string, double> { ["dc_volts"] = 10.0 }),
                    new PlacedComponent("R1", "spice.resistor", 180, 80, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("R2", "spice.resistor", 280, 140, 90, new Dictionary<string, double> { ["ohms"] = 2000.0 })
                ],
                Nets:
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("R2", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("R2", "n")], IsGlobalGround: true)
                ]));
        var synthOp = new ResultManifestIr(
            CaseId: "divider_op",
            BackendId: "synthetic",
            Status: RunTerminalStatus.Succeeded,
            Complete: true,
            Signals: [],
            RealData: new RealSeriesIr("point", "1", [0.0], new Dictionary<string, IReadOnlyList<double>>
            {
                ["v(in)"] = [10.0],
                ["v(out)"] = [20.0 / 3.0],
                ["i(V1)"] = [-1.0 / 300.0]
            }));
        var overlay = SchematicCompiler.BuildOperatingPointOverlay(dividerDoc, synthOp);
        Assert.Equal(10.0, overlay.NodeVoltages["in"], precision: 12);
        Assert.Equal(20.0 / 3.0, overlay.NodeVoltages["out"], precision: 12);
        Assert.Equal(1.0 / 300.0, overlay.BranchCurrents["R1"], precision: 12);
        Assert.Equal(1.0 / 300.0, overlay.BranchCurrents["R2"], precision: 12);
        Assert.Equal(-1.0 / 300.0, overlay.BranchCurrents["V1"], precision: 12);

        var opSvg = SchematicCompiler.ExportSvg(dividerDoc, overlay);
        Assert.Contains("class=\"spice-op-v\"", opSvg);
        Assert.Contains("10.00 V", opSvg);
        Assert.Contains("6.67 V", opSvg);
        Assert.Contains("class=\"spice-op-i\"", opSvg);
        Assert.Contains("3.33 mA", opSvg);

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

    private static NativeCppBackend? TryCreateNativeBackend()
    {
        var explicitLib = Environment.GetEnvironmentVariable("SPICE_V2_CAPI_LIB");
        try
        {
            return new NativeCppBackend(enableAudits: true);
        }
        catch (DllNotFoundException) when (string.IsNullOrWhiteSpace(explicitLib))
        {
            return null;
        }
    }

    [Fact]
    public async Task NativeCppBackend_InProcessCAbi_SolvesOpAcAndTransient_WhenNativeLibraryAvailable()
    {
        using var backend = TryCreateNativeBackend();
        if (backend is null) return;

        var caps = backend.QueryCapabilities();
        Assert.Equal(2u, caps.AbiMajor);
        Assert.True(caps.AbiMinor >= 1u);
        Assert.True(caps.OsdiAvailable);
        Assert.True(caps.AuditsAvailable);

        var circuit = new CircuitBuilder("rc_lowpass_native")
            .AddVoltageSource("V1", "in", "0", dcVolts: 5.0, acMag: 1.0)
            .AddResistor("R1", "in", "out", SiUnits.KiloOhms(1.0))
            .AddCapacitor("C1", "out", "0", SiUnits.MicroFarads(1.0), icVolts: 0.0)
            .Build();

        // 1. DC OP: V(out) = 5.0 V exactly
        var opRes = await backend.RunAsync(new SimulationRequestIr("op_rc", circuit, AnalysisKind.OperatingPoint));
        Assert.Equal(RunTerminalStatus.Succeeded, opRes.Status);
        Assert.NotNull(opRes.RealData);
        Assert.Equal(5.0, opRes.RealData!.SignalValues["v(out)"][0], precision: 11);

        // 2. AC at 1/(2*pi*R*C) ~= 159.155 Hz: H(jw) = 1 / (1 + j*w*R*C)
        var acRes = await backend.RunAsync(new SimulationRequestIr(
            "ac_rc", circuit, AnalysisKind.Ac,
            new Dictionary<string, double> { ["f_start_hz"] = 100.0, ["f_stop_hz"] = 1000.0, ["points_per_decade"] = 5 }));
        Assert.Equal(RunTerminalStatus.Succeeded, acRes.Status);
        Assert.NotNull(acRes.ComplexData);
        var f0 = acRes.ComplexData!.AxisValues[0];
        var w0 = 2.0 * Math.PI * f0;
        var denom = 1.0 + (w0 * 1e-3) * (w0 * 1e-3);
        var expectedRe = 1.0 / denom;
        var expectedIm = -(w0 * 1e-3) / denom;
        var got = acRes.ComplexData.SignalValues["v(out)"][0];
        Assert.Equal(expectedRe, got.Real, precision: 10);
        Assert.Equal(expectedIm, got.Imaginary, precision: 10);

        // 3. UIC Transient step response with BDF2: V(out, t=1ms) ~= 5 * (1 - e^-1) ~= 3.16060279 V
        var trRes = await backend.RunAsync(new SimulationRequestIr(
            "tr_rc", circuit, AnalysisKind.Transient,
            new Dictionary<string, double> { ["step_s"] = 1e-5, ["stop_s"] = 1e-3, ["uic"] = 1.0, ["method"] = 2.0 }));
        Assert.Equal(RunTerminalStatus.Succeeded, trRes.Status);
        Assert.NotNull(trRes.RealData);
        var vEnd = trRes.RealData!.SignalValues["v(out)"][^1];
        Assert.InRange(Math.Abs(vEnd - 5.0 * (1.0 - Math.Exp(-1.0))), 0.0, 2e-4);
    }

    [Fact]
    public async Task NativeCppBackend_AbiV21_RunsSchematicBsim4RfHbPssNoiseSpAndParallelCampaign()
    {
        using var backend = TryCreateNativeBackend();
        if (backend is null) return;

        // 1. Schematic -> ERC -> Canonical IR -> live NativeCppBackend OP solve!
        var doc = new SchematicDocument(
            SchemaVersion: "2.0",
            Name: "schem_divider_live",
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: "Divider",
                Components:
                [
                    new PlacedComponent("V1", "spice.vsrc", 0, 0, 0, new Dictionary<string, double> { ["dc_volts"] = 9.0 }),
                    new PlacedComponent("R1", "spice.resistor", 100, 0, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("R2", "spice.resistor", 200, 0, 0, new Dictionary<string, double> { ["ohms"] = 2000.0 })
                ],
                Nets:
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("R2", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("R2", "n")], IsGlobalGround: true)
                ]));
        var compiled = SchematicCompiler.CompileToIr(doc);
        Assert.True(compiled.Succeeded);
        var schemOp = await backend.RunAsync(new SimulationRequestIr("schem_op", compiled.Circuit!, AnalysisKind.OperatingPoint));
        Assert.Equal(RunTerminalStatus.Succeeded, schemOp.Status);
        Assert.Equal(6.0, schemOp.RealData!.SignalValues["v(out)"][0], precision: 11);

        // 2. Full BSIM4 (LEVEL=54) CMOS Inverter SPICE deck solved in-process via .DC sweep!
        const string bsim4Deck = """
            BSIM4 CMOS Inverter
            .MODEL nch4 NMOS(LEVEL=54 VTH0=0.35 TOXE=2.2n U0=420 VSAT=1.1e5)
            .MODEL pch4 PMOS(LEVEL=54 VTH0=-0.35 TOXE=2.2n U0=160 VSAT=0.9e5)
            Vdd vdd 0 DC 1.2
            Vin in 0 DC 0.0
            Mn out in 0 0 nch4 W=1u L=65n
            Mp out in vdd vdd pch4 W=2.2u L=65n
            .END
            """;
        var dcRes = await backend.RunSpiceDeckAsync(
            "bsim4_vtc", bsim4Deck, AnalysisKind.DcSweep,
            new SpiceDeckExecutionOptions(
                SweepSourceId: "Vin",
                NumericOptions: new Dictionary<string, double> { ["start"] = 0.0, ["stop"] = 1.2, ["step"] = 0.1 }));
        Assert.Equal(RunTerminalStatus.Succeeded, dcRes.Status);
        var vout = dcRes.RealData!.SignalValues["v(out)"];
        Assert.True(vout[0] > 1.18);
        Assert.True(vout[^1] < 0.02);

        // 3. Pumped Diode RF Detector/Mixer solved in-process via .HB, .PSS, and .NOISE!
        const string rfDeck = """
            Pumped Diode Mixer
            .MODEL D1 D(IS=1e-14 CJO=5p)
            Vlo in 0 DC 0.4 SIN(0.4 0.8 1Meg)
            R1 in out 500
            C1 out 0 200p
            D1 out 0 D1
            .END
            """;
        var hbRes = await backend.RunSpiceDeckAsync(
            "rf_hb", rfDeck, AnalysisKind.HarmonicBalance,
            new SpiceDeckExecutionOptions(NumericOptions: new Dictionary<string, double>
            {
                ["fundamental_hz"] = 1.0e6,
                ["harmonics"] = 7.0
            }));
        Assert.Equal(RunTerminalStatus.Succeeded, hbRes.Status);
        Assert.NotNull(hbRes.ComplexData);
        var h2 = hbRes.ComplexData!.SignalValues["v(out)"][2];
        Assert.True(Math.Sqrt(h2.Real * h2.Real + h2.Imaginary * h2.Imaginary) > 1e-3);

        var pssRes = await backend.RunSpiceDeckAsync(
            "rf_pss", rfDeck, AnalysisKind.PeriodicSteadyState,
            new SpiceDeckExecutionOptions(NumericOptions: new Dictionary<string, double>
            {
                ["fundamental_hz"] = 1.0e6,
                ["steps_per_period"] = 64.0,
                ["method"] = 1.0
            }));
        Assert.Equal(RunTerminalStatus.Succeeded, pssRes.Status);
        Assert.Equal(65, pssRes.RealData!.AxisValues.Count);

        var noiseRes = await backend.RunSpiceDeckAsync(
            "rf_noise", rfDeck, AnalysisKind.Noise,
            new SpiceDeckExecutionOptions(
                NoiseOutPositive: "out",
                NoiseInputSourceId: "Vlo",
                NumericOptions: new Dictionary<string, double>
                {
                    ["f_start_hz"] = 1e3,
                    ["f_stop_hz"] = 1e5,
                    ["points_per_decade"] = 2.0
                }));
        Assert.Equal(RunTerminalStatus.Succeeded, noiseRes.Status);
        Assert.True(noiseRes.RealData!.SignalValues["onoise_v_per_sqrt_hz"][0] > 0.0);

        // 4. 2-Port S-Parameters (.SP) on lossless 50-Ohm T-line!
        const string spDeck = """
            Lossless Tline
            T1 p1 0 p2 0 Z0=50 TD=1n
            .END
            """;
        var spRes = await backend.RunSpiceDeckAsync(
            "rf_sp", spDeck, AnalysisKind.SParameters,
            new SpiceDeckExecutionOptions(
                SParamPorts: [("p1", "0"), ("p2", "0")],
                NumericOptions: new Dictionary<string, double>
                {
                    ["f_start_hz"] = 250.0e6,
                    ["f_stop_hz"] = 250.0e6,
                    ["z0_ohms"] = 50.0
                }));
        Assert.Equal(RunTerminalStatus.Succeeded, spRes.Status);
        var s11 = spRes.ComplexData!.SignalValues["S11"][0];
        var s21 = spRes.ComplexData!.SignalValues["S21"][0];
        Assert.True(Math.Sqrt(s11.Real * s11.Real + s11.Imaginary * s11.Imaginary) < 1e-10);
        Assert.Equal(1.0, Math.Sqrt(s21.Real * s21.Real + s21.Imaginary * s21.Imaginary), precision: 9);

        // 5. Live parallel multi-corner campaign directly on NativeCppBackend!
        double[] r2Vals = [1000.0, 3000.0, 9000.0];
        var camp = await CampaignRunner.RunParallelMapAsync(
            r2Vals,
            (r2, idx) => new SimulationRequestIr(
                $"live_corner_{idx}",
                new CircuitBuilder($"c_{idx}")
                    .AddVoltageSource("V1", "in", "0", SiUnits.Volts(10.0))
                    .AddResistor("R1", "in", "out", SiUnits.KiloOhms(1.0))
                    .AddResistor("R2", "out", "0", r2)
                    .Build(),
                AnalysisKind.OperatingPoint),
            backend,
            maxDegreeOfParallelism: 3);
        Assert.Equal(3, camp.Count);
        Assert.Equal(5.0, camp[0].RealData!.SignalValues["v(out)"][0], precision: 11);
        Assert.Equal(7.5, camp[1].RealData!.SignalValues["v(out)"][0], precision: 11);
        Assert.Equal(9.0, camp[2].RealData!.SignalValues["v(out)"][0], precision: 11);
    }

    [Fact]
    public async Task WorkbenchServer_ServesInteractiveUi_CrossProbesSchematic_AndRunsSpectreLtraTrnoiseAndCampaign()
    {
        using var backend = TryCreateNativeBackend();
        if (backend is null) return;

        await using var server = new WorkbenchServer(port: 0, backend: backend);
        server.Start();
        using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

        // 1. Interactive HTML workbench SPA + /api/info
        var html = await http.GetStringAsync("/");
        Assert.Contains("Spice V2 Workbench", html, StringComparison.Ordinal);
        Assert.Contains("data-signal", html, StringComparison.Ordinal);
        Assert.Contains("snap-v", html, StringComparison.Ordinal);
        Assert.Contains("op-v-table", html, StringComparison.Ordinal);
        Assert.Contains("op-i-table", html, StringComparison.Ordinal);

        using var infoDoc = JsonDocument.Parse(await http.GetStringAsync("/api/info"));
        Assert.Equal("2.1", infoDoc.RootElement.GetProperty("abi").GetString());
        Assert.True(infoDoc.RootElement.GetProperty("osdi_available").GetBoolean());
        Assert.True(infoDoc.RootElement.GetProperty("device_kinds").GetArrayLength() >= 9);

        // 2. Schematic -> ERC -> Cross-probable Snap-Grid + DC V/I SVG -> Live BDF2 Transient over libspice_capi
        var schem = new SchematicDocument(
            SchemaVersion: "2.0",
            Name: "workbench_rc",
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: "RC Filter",
                Components:
                [
                    new PlacedComponent("V1", "spice.vsrc", 80, 140, 270, new Dictionary<string, double> { ["dc_volts"] = 5.0, ["ac_mag"] = 1.0 }),
                    new PlacedComponent("R1", "spice.resistor", 180, 80, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("C1", "spice.capacitor", 280, 140, 90, new Dictionary<string, double> { ["farads"] = 1e-6, ["ic_volts"] = 0.0 })
                ],
                Nets:
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("C1", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("C1", "n")], IsGlobalGround: true)
                ]));

        var schemResp = await http.PostAsJsonAsync("/api/schematic/simulate", new
        {
            document = schem,
            analysis = "tran",
            step = 1e-5,
            stop = 1e-3,
            gridSize = 20.0,
            showGrid = true,
            showOp = true
        });
        schemResp.EnsureSuccessStatusCode();
        using var schemJson = JsonDocument.Parse(await schemResp.Content.ReadAsStringAsync());
        Assert.True(schemJson.RootElement.GetProperty("erc_ok").GetBoolean());
        Assert.Equal("Succeeded", schemJson.RootElement.GetProperty("status").GetString());
        var svg = schemJson.RootElement.GetProperty("svg").GetString()!;
        Assert.Contains("data-signal=\"v(out)\"", svg, StringComparison.Ordinal);
        Assert.Contains("data-instance=\"R1\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"spice-snap-grid\"", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"snap-v\"", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"snap-h\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"spice-op-v\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"spice-op-i\"", svg, StringComparison.Ordinal);
        Assert.Equal(5.0, schemJson.RootElement.GetProperty("op_voltages").GetProperty("in").GetDouble(), precision: 11);
        Assert.Equal(5.0, schemJson.RootElement.GetProperty("op_voltages").GetProperty("out").GetDouble(), precision: 11);
        Assert.Equal(0.0, schemJson.RootElement.GetProperty("op_currents").GetProperty("C1").GetDouble(), precision: 11);
        var voutTrace = schemJson.RootElement.GetProperty("series").GetProperty("v(out)");
        var vEnd = voutTrace[voutTrace.GetArrayLength() - 1].GetDouble();
        Assert.InRange(Math.Abs(vEnd - 5.0 * (1.0 - Math.Exp(-1.0))), 0.0, 2e-4);

        // 2b. Resistive divider live DC operating-point voltage & Ohm's-law current overlay
        var divSchem = new SchematicDocument(
            SchemaVersion: "2.0",
            Name: "workbench_divider",
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: "Divider",
                Components:
                [
                    new PlacedComponent("V1", "spice.vsrc", 80, 140, 270, new Dictionary<string, double> { ["dc_volts"] = 10.0 }),
                    new PlacedComponent("R1", "spice.resistor", 180, 80, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("R2", "spice.resistor", 280, 140, 90, new Dictionary<string, double> { ["ohms"] = 2000.0 })
                ],
                Nets:
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("R2", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("R2", "n")], IsGlobalGround: true)
                ]));
        var divResp = await http.PostAsJsonAsync("/api/schematic/simulate", new
        {
            document = divSchem,
            analysis = "op"
        });
        divResp.EnsureSuccessStatusCode();
        using var divJson = JsonDocument.Parse(await divResp.Content.ReadAsStringAsync());
        Assert.Equal("Succeeded", divJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(10.0, divJson.RootElement.GetProperty("op_voltages").GetProperty("in").GetDouble(), precision: 11);
        Assert.Equal(20.0 / 3.0, divJson.RootElement.GetProperty("op_voltages").GetProperty("out").GetDouble(), precision: 11);
        Assert.Equal(1.0 / 300.0, divJson.RootElement.GetProperty("op_currents").GetProperty("R1").GetDouble(), precision: 11);
        Assert.Equal(1.0 / 300.0, divJson.RootElement.GetProperty("op_currents").GetProperty("R2").GetDouble(), precision: 11);
        Assert.Equal(-1.0 / 300.0, divJson.RootElement.GetProperty("op_currents").GetProperty("V1").GetDouble(), precision: 11);
        var divSvg = divJson.RootElement.GetProperty("svg").GetString()!;
        Assert.Contains("6.67 V", divSvg, StringComparison.Ordinal);
        Assert.Contains("3.33 mA", divSvg, StringComparison.Ordinal);

        // 3. Live Spectre Dialect BSIM4 CMOS Inverter .DC through /api/deck/simulate
        const string spectreBsim4 = """
            // Spectre BSIM4 Inverter
            simulator lang=spectre
            model nch bsim4 type=n vth0=0.35 toxe=2.2n u0=420 vsat=1.1e5
            model pch bsim4 type=p vth0=-0.35 toxe=2.2n u0=160 vsat=0.9e5
            Vdd (vdd 0) vsource dc=1.2
            Vin (in 0) vsource dc=0
            Mn (out in 0 0) nch w=1u l=65n
            Mp (out in vdd vdd) pch w=2.2u l=65n
            """;
        var spResp = await http.PostAsJsonAsync("/api/deck/simulate", new
        {
            deck = spectreBsim4,
            analysis = "dc",
            source = "Vin",
            start = 0.0,
            stop = 1.2,
            step = 0.2
        });
        spResp.EnsureSuccessStatusCode();
        using var spJson = JsonDocument.Parse(await spResp.Content.ReadAsStringAsync());
        Assert.Equal("Succeeded", spJson.RootElement.GetProperty("status").GetString());
        var spVout = spJson.RootElement.GetProperty("series").GetProperty("v(out)");
        Assert.True(spVout[0].GetDouble() > 1.18);
        Assert.True(spVout[spVout.GetArrayLength() - 1].GetDouble() < 0.02);

        // 4. Live Lossy LTRA Transmission Line .AC through /api/deck/simulate
        const string ltraDeck = """
            Lossy LTRA Workbench
            .MODEL lossy50 LTRA(R=20 L=250n G=0 C=100p LEN=1.0 NSEGS=32)
            V1 in 0 DC 5 AC 1
            O1 in 0 out 0 lossy50
            Rload out 0 50
            .END
            """;
        var ltraResp = await http.PostAsJsonAsync("/api/deck/simulate", new
        {
            deck = ltraDeck,
            analysis = "ac",
            start = 50.0e6,
            stop = 50.0e6,
            step = 1.0
        });
        ltraResp.EnsureSuccessStatusCode();
        using var ltraJson = JsonDocument.Parse(await ltraResp.Content.ReadAsStringAsync());
        Assert.Equal("Succeeded", ltraJson.RootElement.GetProperty("status").GetString());
        var ltraMag = ltraJson.RootElement.GetProperty("series").GetProperty("v(out)")[0].GetDouble();
        Assert.InRange(Math.Abs(ltraMag - Math.Exp(-0.2)), 0.0, 2e-2);

        // 5. Live Parallel Corner Campaign through /api/campaign/run
        var campResp = await http.PostAsJsonAsync("/api/campaign/run", new
        {
            vdd = 10.0,
            r1 = 1000.0,
            r2Values = new[] { 1000.0, 3000.0, 9000.0 }
        });
        campResp.EnsureSuccessStatusCode();
        using var campJson = JsonDocument.Parse(await campResp.Content.ReadAsStringAsync());
        var runs = campJson.RootElement.GetProperty("runs");
        Assert.Equal(3, runs.GetArrayLength());
        Assert.Equal(5.0, runs[0].GetProperty("v_out").GetDouble(), precision: 11);
        Assert.Equal(7.5, runs[1].GetProperty("v_out").GetDouble(), precision: 11);
        Assert.Equal(9.0, runs[2].GetProperty("v_out").GetDouble(), precision: 11);
    }

    [Fact]
    public void SchematicSaveFormat_MultiDialectExport_SpiceImport_AndPersistentCustomComponentLibrary()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "spice_v2_lib_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var libPath = Path.Combine(tmpDir, "user.spice-lib.json");
            var store = new ComponentLibraryStore(libPath);

            // 1. Foundation library ships with realistic diodes, BJTs, BSIM4 MOSFETs, JFET, and Op-Amp .SUBCKT
            Assert.True(store.ListAll().Count >= 9);
            Assert.True(store.TryGet("foundation.mos.bsim4_nch65", out var nmos));
            Assert.NotNull(nmos?.AttachedModel);
            Assert.Contains("LEVEL=54", nmos!.AttachedModel!.SpiceText);

            // 2. Fail-closed validation rejects malformed attached models and invalid symbol primitives
            var badPart = new CustomComponentDefinition(
                "user.bad.sub", "Bad Sub", "Custom", "Missing .ENDS", "spice.subckt", "X", "ic_box",
                [new CustomPinDefinition("in", -40, 0), new CustomPinDefinition("out", 40, 0)],
                new Dictionary<string, double>(),
                new AttachedModelDefinition(AttachedModelKind.Subcircuit, "BAD_SUB", ".SUBCKT BAD_SUB in out\nR1 in out 1k"),
                SymbolPrimitives: [new SymbolPrimitive("bogus_shape", X1: 0, Y1: 0, StrokeWidth: -1)]);
            var badErrs = ComponentLibraryStore.Validate(badPart);
            Assert.Contains(badErrs, d => d.Code == "invalid_subckt_definition");
            Assert.Contains(badErrs, d => d.Code == "invalid_symbol_primitive_kind");
            Assert.Contains(badErrs, d => d.Code == "invalid_symbol_primitive_dimension");

            // 3. Create custom component with custom vector SymbolPrimitives + attached .SUBCKT model, persist to disk, and reload fresh store
            var customAtten = new CustomComponentDefinition(
                "user.subckt.pi_atten_6db",
                "6dB Pi Attenuator",
                "Custom RF",
                "50-Ohm matched 6dB Pi attenuator",
                "spice.subckt",
                "X",
                "custom",
                [
                    new CustomPinDefinition("in", -40, 0, "input"),
                    new CustomPinDefinition("out", 40, 0, "output"),
                    new CustomPinDefinition("gnd", 0, 20, "ground")
                ],
                new Dictionary<string, double>(),
                new AttachedModelDefinition(
                    AttachedModelKind.Subcircuit,
                    "PI_ATTEN_6DB",
                    """
                    .SUBCKT PI_ATTEN_6DB in out gnd
                      Rsh1 in gnd 150.47
                      Rser in out 37.35
                      Rsh2 out gnd 150.47
                    .ENDS PI_ATTEN_6DB
                    """),
                SymbolPrimitives:
                [
                    new SymbolPrimitive("rect", X1: -28, Y1: -18, X2: 28, Y2: 18, Stroke: "#10b981", Fill: "#064e3b", StrokeWidth: 2),
                    new SymbolPrimitive("line", X1: -40, Y1: 0, X2: -28, Y2: 0, Stroke: "#e2e8f0", StrokeWidth: 2),
                    new SymbolPrimitive("text", X1: 0, Y1: 4, Text: "PI6", Stroke: "#a7f3d0", StrokeWidth: 1.5)
                ]);
            var savedPart = store.UpsertCustom(customAtten);
            Assert.False(savedPart.IsBuiltIn);
            Assert.False(string.IsNullOrWhiteSpace(savedPart.ContentSha256));
            Assert.True(File.Exists(libPath));

            // Reload from disk into a fresh store instance and verify custom vector symbol rendering
            var reloaded = new ComponentLibraryStore(libPath);
            Assert.True(reloaded.TryGet("user.subckt.pi_atten_6db", out var fetched));
            Assert.Equal("PI_ATTEN_6DB", fetched!.AttachedModel!.ModelName);
            Assert.Equal(3, fetched.SymbolPrimitives!.Count);
            var previewSvg = SchematicCompiler.ExportSymbolPreviewSvg(fetched);
            Assert.Contains("class=\"spice-custom-symbol\"", previewSvg);
            Assert.Contains(">PI6</text>", previewSvg);

            // 4. Build schematic using the saved custom library component + directive + port + mirror
            var doc = new SchematicDocument(
                SchemaVersion: "2.1",
                Name: "custom_atten_test",
                RootSheet: new SchematicSheet(
                    SheetId: "root",
                    Title: "Custom Attenuator Test",
                    Components:
                    [
                        new PlacedComponent("V1", "spice.vsrc", 80, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 2.0, ["ac_mag"] = 1.0 }),
                        new PlacedComponent("Rs", "spice.resistor", 160, 80, 0, new Dictionary<string, double> { ["ohms"] = 50.0 }),
                        new PlacedComponent("X1", "spice.subckt", 280, 100, 0, new Dictionary<string, double>(), MirrorX: false, ModelName: "PI_ATTEN_6DB", LibraryPartId: "user.subckt.pi_atten_6db"),
                        new PlacedComponent("Rload", "spice.resistor", 400, 140, 90, new Dictionary<string, double> { ["ohms"] = 50.0 })
                    ],
                    Nets:
                    [
                        new WireNet("src", [new PinRef("V1", "p"), new PinRef("Rs", "p")]),
                        new WireNet("in", [new PinRef("Rs", "n"), new PinRef("X1", "in")]),
                        new WireNet("out", [new PinRef("X1", "out"), new PinRef("Rload", "p")]),
                        new WireNet("GND", [new PinRef("V1", "n"), new PinRef("X1", "gnd"), new PinRef("Rload", "n")], IsGlobalGround: true)
                    ],
                    Annotations: [new SheetAnnotation("a1", 80, 240, ".TEMP 27", IsSpiceDirective: true)],
                    Ports: [new SheetPort("in", 160, 40, "in"), new SheetPort("out", 400, 40, "out")],
                    GridPitch: 20.0,
                    GridStyle: "dots"));

            // 5. Versioned save format round-trip with SHA-256 verification + tamper fail-closed + 2.0->2.1 migration
            var savedJson = SchematicCompiler.SerializeJson(doc, reloaded);
            Assert.Contains("\"formatId\": \"spice.schematic.v2\"", savedJson);
            Assert.Contains("\"contentSha256\":", savedJson);
            var restored = SchematicCompiler.DeserializeJson(savedJson, verifyChecksum: true);
            Assert.Equal("2.1", restored.SchemaVersion);
            Assert.NotNull(restored.EmbeddedParts);
            Assert.Single(restored.EmbeddedParts!);

            var tampered = savedJson.Replace("custom_atten_test", "tampered_name", StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() => SchematicCompiler.DeserializeJson(tampered, verifyChecksum: true));

            var badVersion = savedJson.Replace("\"schemaVersion\": \"2.1\"", "\"schemaVersion\": \"9.9\"", StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() => SchematicCompiler.DeserializeJson(badVersion, verifyChecksum: false));

            // 6. Multi-dialect netlist, StarLark, BOM, Bundle + SHA-256 manifest, and Light/Dots SVG exports
            var spice = SchematicExporter.ExportNetlist(doc, new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, AnalysisKind.OperatingPoint), library: reloaded);
            Assert.Contains(".SUBCKT PI_ATTEN_6DB in out gnd", spice);
            Assert.Contains("X1 in out 0 PI_ATTEN_6DB", spice);
            Assert.Contains(".TEMP 27", spice);

            var ngspice = SchematicExporter.ExportNetlist(doc, new SchematicExportOptions(NetlistDialect.Ngspice, AnalysisKind.OperatingPoint), library: reloaded);
            Assert.Contains(".SAVE ", ngspice);

            var xyce = SchematicExporter.ExportNetlist(doc, new SchematicExportOptions(NetlistDialect.Xyce, AnalysisKind.OperatingPoint), library: reloaded);
            Assert.Contains(".PRINT DC FORMAT=CSV", xyce);

            var ltspice = SchematicExporter.ExportNetlist(doc, new SchematicExportOptions(NetlistDialect.Ltspice, AnalysisKind.OperatingPoint), library: reloaded);
            Assert.Contains(".backanno", ltspice);

            var spectre = SchematicExporter.ExportNetlist(doc, new SchematicExportOptions(NetlistDialect.Spectre, AnalysisKind.OperatingPoint), library: reloaded);
            Assert.Contains("simulator lang=spectre", spectre);
            Assert.Contains("Rs (src in) resistor r=50", spectre);

            var starlark = SchematicExporter.ExportStarlarkScript(doc, library: reloaded);
            Assert.Contains("def build_custom_atten_test():", starlark);

            var bom = SchematicExporter.ExportBomCsv(doc, reloaded);
            Assert.Contains("6dB Pi Attenuator", bom);
            Assert.Contains("PI_ATTEN_6DB", bom);

            var bundle = SchematicExporter.ExportBundleJson(doc, new SchematicExportOptions(NetlistDialect.Spectre, AnalysisKind.OperatingPoint), library: reloaded);
            Assert.Contains("spice.export-bundle.v2", bundle);
            Assert.Contains("custom_atten_test.models.lib", bundle);
            Assert.Contains("\"sha256\":", bundle);

            var lightSvg = SchematicCompiler.ExportSvg(doc, library: reloaded, lightTheme: true, gridStyle: "dots");
            Assert.Contains("data-grid-style=\"dots\"", lightSvg);
            Assert.Contains("fill=\"#ffffff\"", lightSvg);
            Assert.Contains("class=\"spice-custom-symbol\"", lightSvg);
            Assert.Contains(">PI6</text>", lightSvg);
            Assert.Contains("class=\"spice-port\"", lightSvg);
            Assert.Contains("class=\"spice-annotation\"", lightSvg);

            // 7. SPICE Netlist -> Schematic Importer creates valid ERC-passing schematic
            const string rawSpice = """
                Imported Divider
                V1 in 0 DC 12
                R1 in out 1k
                R2 out 0 3k
                .END
                """;
            var imported = SchematicExporter.ImportFromSpiceNetlist(rawSpice);
            var impCompiled = SchematicCompiler.CompileToIr(imported);
            Assert.True(impCompiled.Succeeded);
            Assert.Equal(3, imported.RootSheet.Components.Count);

            // 8. CreateFromSchematic turns a schematic sheet into a saved .SUBCKT library part
            var createdSub = reloaded.CreateFromSchematic(imported, "user.subckt.imported_div", "Imported Divider Block");
            Assert.Equal("spice.subckt", createdSub.BaseDeviceKindId);
            Assert.Contains(".SUBCKT ", createdSub.AttachedModel!.SpiceText);
            Assert.Contains(".ENDS ", createdSub.AttachedModel.SpiceText);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task WorkbenchServer_CustomLibraryParts_SimulateLiveAndExportMultiDialect_OverNativeCppEngine()
    {
        using var backend = TryCreateNativeBackend();
        if (backend is null) return;

        var tmpDir = Path.Combine(Path.GetTempPath(), "spice_v2_srv_lib_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var libStore = new ComponentLibraryStore(Path.Combine(tmpDir, "srv.spice-lib.json"));
            await using var server = new WorkbenchServer(port: 0, backend: backend, library: libStore);
            server.Start();
            using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

            var uiHtml = await http.GetStringAsync("/");
            Assert.Contains("id=\"sym-canvas\"", uiHtml, StringComparison.Ordinal);

            // 1. Save a custom component with vector SymbolPrimitives & attached .SUBCKT model through POST /api/library
            var customPart = new
            {
                id = "user.subckt.half_div",
                name = "Precision 2:1 Divider",
                category = "Custom Blocks",
                description = "Matched 10k/10k resistive divider subcircuit",
                baseDeviceKindId = "spice.subckt",
                spicePrefix = "X",
                symbolStyle = "custom",
                pins = new[]
                {
                    new { name = "in", localX = -40.0, localY = 0.0, role = "input" },
                    new { name = "out", localX = 40.0, localY = 0.0, role = "output" },
                    new { name = "gnd", localX = 0.0, localY = 20.0, role = "ground" }
                },
                defaultParameters = new Dictionary<string, double>(),
                attachedModel = new
                {
                    kind = "subcircuit",
                    modelName = "HALF_DIV",
                    spiceText = ".SUBCKT HALF_DIV in out gnd\n  R1 in out 10k\n  R2 out gnd 10k\n.ENDS HALF_DIV"
                },
                symbolPrimitives = new object[]
                {
                    new { kind = "polygon", points = "-26,-16 26,-16 26,16 -26,16", stroke = "#38bdf8", fill = "#172554", strokeWidth = 2.0 },
                    new { kind = "text", x1 = 0.0, y1 = 4.0, text = "DIV2", stroke = "#e2e8f0", strokeWidth = 1.5 }
                }
            };
            var savePartResp = await http.PostAsJsonAsync("/api/library", customPart);
            savePartResp.EnsureSuccessStatusCode();

            // Verify GET /api/library lists the saved custom part with preview_svg
            using var libList = JsonDocument.Parse(await http.GetStringAsync("/api/library"));
            Assert.Contains(
                libList.RootElement.GetProperty("components").EnumerateArray(),
                el => el.GetProperty("part").GetProperty("id").GetString() == "user.subckt.half_div" &&
                      el.GetProperty("preview_svg").GetString()!.Contains("<svg ", StringComparison.Ordinal));

            // 2. Place the saved custom component on a schematic & simulate LIVE via libspice_capi!
            var schemWithCustom = new SchematicDocument(
                SchemaVersion: "2.1",
                Name: "live_custom_subckt",
                RootSheet: new SchematicSheet(
                    SheetId: "root",
                    Title: "Live Custom Subcircuit",
                    Components:
                    [
                        new PlacedComponent("V1", "spice.vsrc", 80, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 8.0 }),
                        new PlacedComponent("X1", "spice.subckt", 240, 140, 0, new Dictionary<string, double>(), ModelName: "HALF_DIV", LibraryPartId: "user.subckt.half_div")
                    ],
                    Nets:
                    [
                        new WireNet("in", [new PinRef("V1", "p"), new PinRef("X1", "in")]),
                        new WireNet("out", [new PinRef("X1", "out")]),
                        new WireNet("GND", [new PinRef("V1", "n"), new PinRef("X1", "gnd")], IsGlobalGround: true)
                    ]));

            var simResp = await http.PostAsJsonAsync("/api/schematic/simulate", new
            {
                document = schemWithCustom,
                analysis = "op"
            });
            simResp.EnsureSuccessStatusCode();
            using var simJson = JsonDocument.Parse(await simResp.Content.ReadAsStringAsync());
            Assert.True(simJson.RootElement.GetProperty("erc_ok").GetBoolean());
            Assert.Equal("Succeeded", simJson.RootElement.GetProperty("status").GetString());
            // 8.0 V through custom 10k/10k HALF_DIV subcircuit -> exact 4.00 V at out + custom vector symbol!
            Assert.Equal(4.0, simJson.RootElement.GetProperty("op_voltages").GetProperty("out").GetDouble(), precision: 9);
            var simSvg = simJson.RootElement.GetProperty("svg").GetString()!;
            Assert.Contains("4.00 V", simSvg, StringComparison.Ordinal);
            Assert.Contains("class=\"spice-custom-symbol\"", simSvg, StringComparison.Ordinal);
            Assert.Contains(">DIV2</text>", simSvg, StringComparison.Ordinal);

            // 3. Test Ideal Op-Amp Foundation Library .SUBCKT (non-inverting gain = 1 + 20k/10k = 3.0 -> 4.5 V)
            var opAmpSchem = new SchematicDocument(
                SchemaVersion: "2.1",
                Name: "live_opamp_gain3",
                RootSheet: new SchematicSheet(
                    SheetId: "root",
                    Title: "OpAmp Gain 3",
                    Components:
                    [
                        new PlacedComponent("Vin", "spice.vsrc", 80, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 1.5 }),
                        new PlacedComponent("X1", "spice.subckt", 240, 140, 0, new Dictionary<string, double>(), ModelName: "IDEAL_OPAMP", LibraryPartId: "foundation.subckt.ideal_opamp"),
                        new PlacedComponent("Rf", "spice.resistor", 340, 220, 0, new Dictionary<string, double> { ["ohms"] = 20000.0 }),
                        new PlacedComponent("Rg", "spice.resistor", 180, 240, 90, new Dictionary<string, double> { ["ohms"] = 10000.0 })
                    ],
                    Nets:
                    [
                        new WireNet("in", [new PinRef("Vin", "p"), new PinRef("X1", "inp")]),
                        new WireNet("fb", [new PinRef("X1", "inn"), new PinRef("Rf", "p"), new PinRef("Rg", "p")]),
                        new WireNet("out", [new PinRef("X1", "out"), new PinRef("Rf", "n")]),
                        new WireNet("GND", [new PinRef("Vin", "n"), new PinRef("Rg", "n")], IsGlobalGround: true)
                    ]));
            var opAmpResp = await http.PostAsJsonAsync("/api/schematic/simulate", new { document = opAmpSchem, analysis = "op" });
            opAmpResp.EnsureSuccessStatusCode();
            using var opAmpJson = JsonDocument.Parse(await opAmpResp.Content.ReadAsStringAsync());
            Assert.Equal("Succeeded", opAmpJson.RootElement.GetProperty("status").GetString());
            Assert.InRange(Math.Abs(opAmpJson.RootElement.GetProperty("op_voltages").GetProperty("out").GetDouble() - 4.5), 0.0, 1e-3);

            // 4. Test POST /api/schematic/save, /load, /export (Spectre + Bundle), and DELETE /api/library/{id}
            var saveResp = await http.PostAsJsonAsync("/api/schematic/save", new { document = schemWithCustom, author = "TestUser" });
            saveResp.EnsureSuccessStatusCode();
            using var saveJson = JsonDocument.Parse(await saveResp.Content.ReadAsStringAsync());
            var savedContent = saveJson.RootElement.GetProperty("content").GetString()!;
            Assert.Equal("live_custom_subckt.spice.json", saveJson.RootElement.GetProperty("filename").GetString());

            var loadResp = await http.PostAsJsonAsync("/api/schematic/load", new { content = savedContent, verifyChecksum = true });
            loadResp.EnsureSuccessStatusCode();
            using var loadJson = JsonDocument.Parse(await loadResp.Content.ReadAsStringAsync());
            Assert.True(loadJson.RootElement.GetProperty("loaded").GetBoolean());

            var expResp = await http.PostAsJsonAsync("/api/schematic/export", new { document = schemWithCustom, format = "spectre", analysis = "op" });
            expResp.EnsureSuccessStatusCode();
            using var expJson = JsonDocument.Parse(await expResp.Content.ReadAsStringAsync());
            Assert.Contains("simulator lang=spectre", expJson.RootElement.GetProperty("content").GetString()!, StringComparison.Ordinal);

            var delResp = await http.DeleteAsync("/api/library/user.subckt.half_div");
            delResp.EnsureSuccessStatusCode();
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
