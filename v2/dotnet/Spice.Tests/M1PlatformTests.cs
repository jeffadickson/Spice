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

        using var infoDoc = JsonDocument.Parse(await http.GetStringAsync("/api/info"));
        Assert.Equal("2.1", infoDoc.RootElement.GetProperty("abi").GetString());
        Assert.True(infoDoc.RootElement.GetProperty("osdi_available").GetBoolean());
        Assert.True(infoDoc.RootElement.GetProperty("device_kinds").GetArrayLength() >= 9);

        // 2. Schematic -> ERC -> Cross-probable SVG -> Live BDF2 Transient over libspice_capi
        var schem = new SchematicDocument(
            SchemaVersion: "2.0",
            Name: "workbench_rc",
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: "RC Filter",
                Components:
                [
                    new PlacedComponent("V1", "spice.vsrc", 60, 100, 0, new Dictionary<string, double> { ["dc_volts"] = 5.0, ["ac_mag"] = 1.0 }),
                    new PlacedComponent("R1", "spice.resistor", 180, 60, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("C1", "spice.capacitor", 300, 100, 0, new Dictionary<string, double> { ["farads"] = 1e-6, ["ic_volts"] = 0.0 })
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
            stop = 1e-3
        });
        schemResp.EnsureSuccessStatusCode();
        using var schemJson = JsonDocument.Parse(await schemResp.Content.ReadAsStringAsync());
        Assert.True(schemJson.RootElement.GetProperty("erc_ok").GetBoolean());
        Assert.Equal("Succeeded", schemJson.RootElement.GetProperty("status").GetString());
        var svg = schemJson.RootElement.GetProperty("svg").GetString()!;
        Assert.Contains("data-signal=\"v(out)\"", svg, StringComparison.Ordinal);
        Assert.Contains("data-instance=\"R1\"", svg, StringComparison.Ordinal);
        var voutTrace = schemJson.RootElement.GetProperty("series").GetProperty("v(out)");
        var vEnd = voutTrace[voutTrace.GetArrayLength() - 1].GetDouble();
        Assert.InRange(Math.Abs(vEnd - 5.0 * (1.0 - Math.Exp(-1.0))), 0.0, 2e-4);

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
}
