using System.Text.Json;
using Cameo.Netlist;
using Spice.App;
using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;
using Spice.Starlark;
using Xunit;

namespace Spice.Tests;

/// <summary>
/// Rigorous unit, error-path, multi-dialect, differential-comparer, Starlark DSL,
/// CameoNetlistBridge, and CLI integration tests across Spice.Ir, Spice.Starlark,
/// Spice.Sdk, Spice.Schematic, and Spice.App.
/// </summary>
public sealed class SpiceCoreAndCliRigorousTests
{
    // ==================== 1. SPICE.IR NETLIST EMITTER ====================

    [Fact]
    public void SpiceNetlistEmitter_EmitsAllBuiltInDevicesModelsSubcktsOsdiAndXyceCsvModes()
    {
        var circuit = new CircuitIr(
            Name: "kitchen_sink_ir",
            Nodes: ["0", "in", "n1", "n2", "n3", "ctrl", "out"],
            Devices:
            [
                new DeviceInstanceIr("1", "spice.vsrc",
                    new Dictionary<string, string> { ["p"] = "in", ["n"] = "0" },
                    new Dictionary<string, double> { ["dc_volts"] = 5.0, ["ac_mag"] = 1.0, ["ac_phase_deg"] = 45.0, ["sin_offset"] = 1.0, ["sin_amp"] = 0.5, ["sin_freq_hz"] = 1000.0 }),
                new DeviceInstanceIr("Vpulse", "spice.vsrc",
                    new Dictionary<string, string> { ["p"] = "ctrl", ["n"] = "0" },
                    new Dictionary<string, double> { ["dc_volts"] = 0.0, ["pulse_v1"] = 0.0, ["pulse_v2"] = 5.0, ["pulse_td"] = 1e-6, ["pulse_tr"] = 1e-9, ["pulse_tf"] = 1e-9, ["pulse_pw"] = 1e-5, ["pulse_per"] = 2e-5 }),
                new DeviceInstanceIr("src_i", "spice.isrc",
                    new Dictionary<string, string> { ["p"] = "n1", ["n"] = "0" },
                    new Dictionary<string, double> { ["dc_amps"] = 1e-3 }),
                new DeviceInstanceIr("load", "spice.resistor",
                    new Dictionary<string, string> { ["p"] = "in", ["n"] = "n1" },
                    new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                new DeviceInstanceIr("C1", "spice.capacitor",
                    new Dictionary<string, string> { ["p"] = "n1", ["n"] = "0" },
                    new Dictionary<string, double> { ["farads"] = 1e-6, ["ic_volts"] = 1.2 }),
                new DeviceInstanceIr("L1", "spice.inductor",
                    new Dictionary<string, string> { ["p"] = "n1", ["n"] = "n2" },
                    new Dictionary<string, double> { ["henries"] = 1e-3, ["ic_amps"] = 0.05 }),
                new DeviceInstanceIr("G1", "spice.vccs",
                    new Dictionary<string, string> { ["p"] = "n2", ["n"] = "0", ["cp"] = "in", ["cn"] = "0" },
                    new Dictionary<string, double> { ["siemens"] = 0.01 }),
                new DeviceInstanceIr("E1", "spice.vcvs",
                    new Dictionary<string, string> { ["p"] = "n3", ["n"] = "0", ["cp"] = "n2", ["cn"] = "0" },
                    new Dictionary<string, double> { ["gain"] = 2.0 }),
                new DeviceInstanceIr("D1", "spice.diode",
                    new Dictionary<string, string> { ["p"] = "n1", ["n"] = "n2" },
                    new Dictionary<string, double> { ["is_amps"] = 1e-14, ["n_emission"] = 1.1 }),
                new DeviceInstanceIr("M1", "spice.mosfet",
                    new Dictionary<string, string> { ["d"] = "n2", ["g"] = "ctrl", ["s"] = "0", ["b"] = "0" },
                    new Dictionary<string, double> { ["level"] = 54.0, ["vth0_volts"] = 0.4, ["w_m"] = 2e-6, ["l_m"] = 65e-9 }),
                new DeviceInstanceIr("M2", "spice.mosfet",
                    new Dictionary<string, string> { ["d"] = "n2", ["g"] = "ctrl", ["s"] = "in", ["b"] = "in" },
                    new Dictionary<string, double> { ["level"] = 54.0, ["vth0_volts"] = -0.4, ["w_m"] = 4e-6, ["l_m"] = 65e-9 }),
                new DeviceInstanceIr("Q1", "spice.bjt",
                    new Dictionary<string, string> { ["c"] = "n2", ["b"] = "n1", ["e"] = "0" },
                    new Dictionary<string, double> { ["bf"] = 150.0, ["is_amps"] = 1e-15, ["area"] = 1.0 }),
                new DeviceInstanceIr("J1", "spice.jfet",
                    new Dictionary<string, string> { ["d"] = "n2", ["g"] = "n1", ["s"] = "0" },
                    new Dictionary<string, double> { ["vto_volts"] = -2.0, ["beta_a_per_v2"] = 1e-4 }),
                new DeviceInstanceIr("M3", "spice.vdmos",
                    new Dictionary<string, string> { ["d"] = "n3", ["g"] = "ctrl", ["s"] = "0" },
                    new Dictionary<string, double> { ["vto_volts"] = 2.5, ["kp_a_per_v2"] = 12.0, ["m_mult"] = 2.0 }),
                new DeviceInstanceIr("Z1", "spice.mesfet",
                    new Dictionary<string, string> { ["d"] = "n3", ["g"] = "ctrl", ["s"] = "0" },
                    new Dictionary<string, double> { ["vto_volts"] = -1.5, ["beta_a_per_v2"] = 0.05 }),
                new DeviceInstanceIr("S1", "spice.switch",
                    new Dictionary<string, string> { ["p"] = "n3", ["n"] = "out", ["cp"] = "ctrl", ["cn"] = "0" },
                    new Dictionary<string, double> { ["vt_volts"] = 2.5, ["vh_volts"] = 0.2, ["ron_ohms"] = 1.0, ["roff_ohms"] = 1e6, ["hysteresis"] = 1.0 }),
                new DeviceInstanceIr("W1", "spice.cswitch",
                    new Dictionary<string, string> { ["p"] = "n3", ["n"] = "out" },
                    new Dictionary<string, double> { ["it_amps"] = 5e-3, ["ih_amps"] = 1e-3, ["ron_ohms"] = 2.0, ["roff_ohms"] = 1e6, ["hysteresis"] = 1.0 },
                    ControlSourceId: "V1"),
                new DeviceInstanceIr("Lcore", "spice.core",
                    new Dictionary<string, string> { ["p"] = "out", ["n"] = "0" },
                    new Dictionary<string, double> { ["turns"] = 25.0, ["area_m2"] = 1e-4, ["path_m"] = 0.05, ["ic_amps"] = 0.01 }),
                new DeviceInstanceIr("U1", "spice.subckt",
                    new Dictionary<string, string> { ["in"] = "n1", ["out"] = "out" },
                    new Dictionary<string, double> { ["gain"] = 3.0 },
                    ModelName: "AMP_SUB",
                    PinOrder: ["in", "out"]),
                new DeviceInstanceIr("U2", "spice.osdi",
                    new Dictionary<string, string> { ["a"] = "out", ["b"] = "0" },
                    new Dictionary<string, double>(),
                    ModelName: "OSDI_MOD")
            ],
            Models:
            [
                new ModelDefinitionIr("AMP_SUB", "SUBCKT", new Dictionary<string, double>(),
                    RawSpiceDefinition: ".SUBCKT AMP_SUB in out\nRsub in out 1k\n.ENDS AMP_SUB"),
                new ModelDefinitionIr("AMP_SUB", "SUBCKT", new Dictionary<string, double>(), // duplicate deduped
                    RawSpiceDefinition: ".SUBCKT AMP_SUB in out\n.ENDS"),
                new ModelDefinitionIr("OSDI_MOD", "OSDI", new Dictionary<string, double> { ["r"] = 50.0 },
                    OsdiLibraryPath: "/opt/models/res.osdi", OsdiModelName: "va_res")
            ],
            Directives: [".TEMP 27", "   "]);

        var opDeck = SpiceNetlistEmitter.EmitNetlist(new SimulationRequestIr("op", circuit, AnalysisKind.OperatingPoint), includeXycePrintCsv: true);
        Assert.Contains(".OSDI \"/opt/models/res.osdi\"", opDeck, StringComparison.Ordinal);
        Assert.Contains("model=va_res r=50", opDeck, StringComparison.Ordinal);
        Assert.Contains("V1 in 0 DC 5 AC 1 45 SIN(1 0.5 1000)", opDeck, StringComparison.Ordinal);
        Assert.Contains("PULSE(0 5 ", opDeck, StringComparison.Ordinal);
        Assert.Contains("Isrc_i n1 0 DC 0.001", opDeck, StringComparison.Ordinal);
        Assert.Contains("Rload in n1 1000", opDeck, StringComparison.Ordinal);
        Assert.Contains("IC=1.2", opDeck, StringComparison.Ordinal);
        Assert.Contains("IC=0.05", opDeck, StringComparison.Ordinal);
        Assert.Contains("PMOS(", opDeck, StringComparison.Ordinal);
        Assert.Contains("NMOS(", opDeck, StringComparison.Ordinal);
        Assert.Contains("VDMOS(", opDeck, StringComparison.Ordinal);
        Assert.Contains(" M=2", opDeck, StringComparison.Ordinal);
        Assert.Contains("HYST=1", opDeck, StringComparison.Ordinal);
        Assert.Contains("W1 n3 out V1", opDeck, StringComparison.Ordinal);
        Assert.Contains("CORE(", opDeck, StringComparison.Ordinal);
        Assert.Contains("XU1 n1 out AMP_SUB gain=3", opDeck, StringComparison.Ordinal);
        Assert.Contains("XU2 out 0 OSDI_MOD", opDeck, StringComparison.Ordinal);
        Assert.Contains(".TEMP 27", opDeck, StringComparison.Ordinal);
        Assert.Contains(".PRINT DC FORMAT=CSV", opDeck, StringComparison.Ordinal);

        var dcDeck = SpiceNetlistEmitter.EmitNetlist(
            new SimulationRequestIr("dc", circuit, AnalysisKind.DcSweep, SweepSourceId: "V1",
                Options: new Dictionary<string, double> { ["start"] = 0, ["stop"] = 5, ["step"] = 0.5 }),
            includeXycePrintCsv: true);
        Assert.Contains(".DC V1 0 5 0.5", dcDeck, StringComparison.Ordinal);
        Assert.Contains(".PRINT DC FORMAT=CSV", dcDeck, StringComparison.Ordinal);

        var acDeck = SpiceNetlistEmitter.EmitNetlist(
            new SimulationRequestIr("ac", circuit, AnalysisKind.Ac,
                Options: new Dictionary<string, double> { ["points_per_decade"] = 20, ["f_start_hz"] = 10, ["f_stop_hz"] = 1e6 }),
            includeXycePrintCsv: true);
        Assert.Contains(".AC DEC 20 10 1000000", acDeck, StringComparison.Ordinal);
        Assert.Contains(".PRINT AC FORMAT=CSV", acDeck, StringComparison.Ordinal);

        var tranDeck = SpiceNetlistEmitter.EmitNetlist(
            new SimulationRequestIr("tr", circuit, AnalysisKind.Transient,
                Options: new Dictionary<string, double> { ["step_s"] = 1e-6, ["stop_s"] = 1e-4, ["uic"] = 1.0 }),
            includeXycePrintCsv: true);
        Assert.Contains(".TRAN ", tranDeck, StringComparison.Ordinal);
        Assert.Contains(" UIC", tranDeck, StringComparison.Ordinal);
        Assert.Contains(".PRINT TRAN FORMAT=CSV", tranDeck, StringComparison.Ordinal);

        // Unsupported analysis throws cleanly
        Assert.Throws<NotSupportedException>(() =>
            SpiceNetlistEmitter.EmitNetlist(new SimulationRequestIr("noise", circuit, AnalysisKind.Noise)));

        // Invalid circuit throws InvalidOperationException
        var badCircuit = new CircuitIr("bad", ["in"], []);
        Assert.Throws<InvalidOperationException>(() =>
            SpiceNetlistEmitter.EmitNetlist(new SimulationRequestIr("bad", badCircuit, AnalysisKind.OperatingPoint)));
    }

    // ==================== 2. SPICE.STARLARK DSL & CAMPAIGN RUNNER ====================

    [Fact]
    public void SiUnitsAndCircuitBuilder_CoverAllUnitsAndValidationErrors()
    {
        Assert.Equal(10.0, SiUnits.Ohms(10), 12);
        Assert.Equal(10e3, SiUnits.KiloOhms(10), 12);
        Assert.Equal(10e6, SiUnits.MegaOhms(10), 12);
        Assert.Equal(2.0, SiUnits.Farads(2), 12);
        Assert.Equal(2e-6, SiUnits.MicroFarads(2), tolerance: 1e-15);
        Assert.Equal(2e-9, SiUnits.NanoFarads(2), tolerance: 1e-18);
        Assert.Equal(2e-12, SiUnits.PicoFarads(2), tolerance: 1e-21);
        Assert.Equal(3.0, SiUnits.Henries(3), 12);
        Assert.Equal(3e-3, SiUnits.MilliHenries(3), 12);
        Assert.Equal(3e-6, SiUnits.MicroHenries(3), tolerance: 1e-15);
        Assert.Equal(5.0, SiUnits.Volts(5), 12);
        Assert.Equal(5e-3, SiUnits.MilliVolts(5), 12);
        Assert.Equal(1.0, SiUnits.Amps(1), 12);
        Assert.Equal(1e-3, SiUnits.MilliAmps(1), 12);
        Assert.Equal(60.0, SiUnits.Hertz(60), 12);
        Assert.Equal(60e3, SiUnits.KiloHertz(60), 12);
        Assert.Equal(60e6, SiUnits.MegaHertz(60), 12);
        Assert.Equal(4.0, SiUnits.Seconds(4), 12);
        Assert.Equal(4e-3, SiUnits.MilliSeconds(4), 12);
        Assert.Equal(4e-6, SiUnits.MicroSeconds(4), tolerance: 1e-15);
        Assert.Equal(4e-9, SiUnits.NanoSeconds(4), tolerance: 1e-18);

        Assert.Throws<ArgumentException>(() => new CircuitBuilder("   "));

        var c = new CircuitBuilder("rlc_diode")
            .AddVoltageSource("V1", "in", "0", SiUnits.Volts(5), acMag: 1.0, acPhaseDeg: 0.0)
            .AddCurrentSource("I1", "n1", "0", SiUnits.MilliAmps(2), acMag: 0.1)
            .AddResistor("R1", "in", "n1", SiUnits.KiloOhms(1))
            .AddCapacitor("C1", "n1", "0", SiUnits.MicroFarads(1), icVolts: 0.5)
            .AddInductor("L1", "n1", "n2", SiUnits.MilliHenries(10), icAmps: 0.01)
            .AddDiode("D1", "n2", "0")
            .Build();

        Assert.Equal("0", c.Nodes[0]);
        Assert.Equal(6, c.Devices.Count);

        // Non-finite resistor parameter fails closed on Build()
        Assert.Throws<InvalidOperationException>(() =>
            new CircuitBuilder("bad_r")
                .AddResistor("R1", "in", "0", double.NaN)
                .Build());
    }

    [Fact]
    public async Task CampaignRunner_ValidatesConcurrency_PreservesExactOrder_AndHonorsCancellation()
    {
        var fakeBackend = new DelayedOrderCheckingBackend();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CampaignRunner.RunParallelMapAsync([1], (p, i) => MakeDummyReq(i), fakeBackend, maxDegreeOfParallelism: 0));

        // Intentionally slow down earlier indices so later ones complete first;
        // CampaignRunner must still return results in exact input order [0..7].
        var points = Enumerable.Range(0, 8).ToArray();
        var results = await CampaignRunner.RunParallelMapAsync(points, (p, i) => MakeDummyReq(p), fakeBackend, maxDegreeOfParallelism: 4);
        Assert.Equal(8, results.Count);
        for (int i = 0; i < 8; ++i)
        {
            Assert.Equal($"case_{i}", results[i].CaseId);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CampaignRunner.RunParallelMapAsync(points, (p, i) => MakeDummyReq(p), fakeBackend, maxDegreeOfParallelism: 2, cts.Token));
    }

    // ==================== 3. SPICE.SDK DIFFERENTIAL COMPARER ====================

    [Fact]
    public void DifferentialComparer_CoversRealComplexToleranceMismatchAndFailureModes()
    {
        ResultManifestIr MakeReal(string id, RunTerminalStatus st, bool complete, Dictionary<string, IReadOnlyList<double>>? data) =>
            new(id, "test", st, complete, [], RealData: data is null ? null : new RealSeriesIr("x", "V", [0.0, 1.0], data), ErrorMessage: st == RunTerminalStatus.Succeeded ? null : "boom");

        ResultManifestIr MakeComplex(string id, Dictionary<string, IReadOnlyList<ComplexSample>> data) =>
            new(id, "test", RunTerminalStatus.Succeeded, true, [], ComplexData: new ComplexSeriesIr("freq", "Hz", [100.0], data));

        var okRef = MakeReal("r", RunTerminalStatus.Succeeded, true, new() { ["v(out)"] = [1.0, 2.0] });
        var okCand = MakeReal("c", RunTerminalStatus.Succeeded, true, new() { ["v(out)"] = [1.0 + 1e-8, 2.0 - 1e-8] });
        var passRep = DifferentialComparer.Compare(okRef, okCand);
        Assert.True(passRep.Passed);
        Assert.Single(passRep.Metrics);

        // Reference failed / candidate failed
        Assert.False(DifferentialComparer.Compare(MakeReal("r", RunTerminalStatus.Failed, false, null), okCand).Passed);
        Assert.False(DifferentialComparer.Compare(okRef, MakeReal("c", RunTerminalStatus.Cancelled, false, null)).Passed);

        // Missing signal & sample count mismatch & out-of-tolerance
        var missingSig = MakeReal("c", RunTerminalStatus.Succeeded, true, new() { ["v(other)"] = [1.0, 2.0] });
        Assert.Contains("missing signal", DifferentialComparer.Compare(okRef, missingSig).FailureReason!, StringComparison.OrdinalIgnoreCase);

        var wrongLen = MakeReal("c", RunTerminalStatus.Succeeded, true, new() { ["v(out)"] = [1.0] });
        Assert.Contains("Sample count mismatch", DifferentialComparer.Compare(okRef, wrongLen).FailureReason!, StringComparison.Ordinal);

        var farOff = MakeReal("c", RunTerminalStatus.Succeeded, true, new() { ["v(out)"] = [1.0, 2.5] });
        var farRep = DifferentialComparer.Compare(okRef, farOff);
        Assert.False(farRep.Passed);
        Assert.True(farRep.Metrics[0].MaxAbsError >= 0.49);

        // Complex data: pass, tolerance fail, missing signal, count mismatch
        var cxRef = MakeComplex("r", new() { ["v(out)"] = [new ComplexSample(0.5, -0.5)] });
        var cxOk = MakeComplex("c", new() { ["v(out)"] = [new ComplexSample(0.5 + 1e-8, -0.5)] });
        Assert.True(DifferentialComparer.Compare(cxRef, cxOk).Passed);

        var cxBad = MakeComplex("c", new() { ["v(out)"] = [new ComplexSample(0.1, 0.1)] });
        Assert.False(DifferentialComparer.Compare(cxRef, cxBad).Passed);

        var cxMiss = MakeComplex("c", new() { ["v(other)"] = [new ComplexSample(0.5, -0.5)] });
        Assert.Contains("missing complex signal", DifferentialComparer.Compare(cxRef, cxMiss).FailureReason!, StringComparison.OrdinalIgnoreCase);

        var cxLen = MakeComplex("c", new() { ["v(out)"] = [new ComplexSample(0.5, -0.5), new ComplexSample(0.0, 0.0)] });
        Assert.Contains("Complex sample count mismatch", DifferentialComparer.Compare(cxRef, cxLen).FailureReason!, StringComparison.Ordinal);

        // Incompatible payloads (real vs complex)
        Assert.Contains("incompatible", DifferentialComparer.Compare(okRef, cxRef).FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ==================== 4. CAMEONETLISTBRIDGE & SCHEMATIC EXPORTER ====================

    [Fact]
    public void CameoNetlistBridgeAndSchematicExporter_CoverMultiSheetAllDialectsAndErrorPaths()
    {
        // Cyclic flagship netlist fails cleanly in CompileToIr and throws in ToSchematicDocument
        var cyclic = FlagshipNetlist.Parse("netlist \"cyc\" { module A { use b = B() } module B { use a = A() } circuit { use t = A() } }").Document!;
        var failCompile = CameoNetlistBridge.CompileToIr(cyclic);
        Assert.False(failCompile.Succeeded);
        Assert.Contains(failCompile.Diagnostics, d => d.Code == "cyclic_module_hierarchy");
        Assert.Throws<InvalidOperationException>(() => CameoNetlistBridge.ToSchematicDocument(cyclic));

        // Multi-sheet schematic lifted to Cameo.Netlist and exported across all 5 SPICE dialects + BOM + Starlark + Bundle + Polyglot SVG
        var rootSheet = new SchematicSheet(
            SheetId: "root",
            Title: "Top",
            Components:
            [
                new PlacedComponent("V1", "spice.vsrc", 80, 120, 0, new Dictionary<string, double> { ["dc_volts"] = 5.0, ["ac_mag"] = 1.0 }),
                new PlacedComponent("R1", "spice.resistor", 180, 120, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 })
            ],
            Nets:
            [
                new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                new WireNet("mid", [new PinRef("R1", "n")]),
                new WireNet("GND", [new PinRef("V1", "n")], IsGlobalGround: true)
            ],
            Annotations: [new SheetAnnotation("a1", 40, 260, ".TEMP 27", IsSpiceDirective: true)],
            Ports: [new SheetPort("mid", 320, 120, "out")]);

        var subSheet = new SchematicSheet(
            SheetId: "load_sheet",
            Title: "Load",
            Components:
            [
                new PlacedComponent("R2", "spice.resistor", 120, 120, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 })
            ],
            Nets:
            [
                new WireNet("mid", [new PinRef("R2", "p")]),
                new WireNet("GND", [new PinRef("R2", "n")], IsGlobalGround: true)
            ],
            Ports: [new SheetPort("mid", 40, 120, "in")]);

        var multi = new SchematicDocument(
            SchemaVersion: SchematicCompiler.CurrentSchemaVersion,
            Name: "multi_sheet_divider",
            RootSheet: rootSheet,
            SubSheets: [subSheet]);

        var lifted = CameoNetlistBridge.FromSchematic(multi);
        Assert.Equal(3, lifted.Circuit.Parts.Count);
        Assert.Contains(".TEMP 27", lifted.Circuit.Directives);

        var polyFromSchem = CameoNetlistBridge.ExportPolyglotSvg(multi, lightTheme: true, gridStyle: "dots");
        Assert.Contains("<metadata>", polyFromSchem, StringComparison.Ordinal);
        Assert.True(CameoNetlistBridge.ExtractFromPolyglotSvg(polyFromSchem).Succeeded);
        var veloFromSchem = CameoNetlistBridge.ExportVeloSimNetlistJson(multi);
        Assert.Contains("velosim.netlist.v1", veloFromSchem, StringComparison.Ordinal);
        var cjFromSchem = CameoNetlistBridge.ExportCircuitJson(multi);
        Assert.Contains("source_component", cjFromSchem, StringComparison.Ordinal);

        foreach (var dialect in new[] { NetlistDialect.CanonicalSpice3f5, NetlistDialect.Ngspice, NetlistDialect.Xyce, NetlistDialect.Ltspice, NetlistDialect.Spectre })
        {
            var exported = SchematicExporter.ExportNetlist(multi, new SchematicExportOptions(dialect, AnalysisKind.OperatingPoint));
            Assert.NotEmpty(exported);
        }

        var bom = SchematicExporter.ExportBomCsv(multi);
        Assert.Contains("R1", bom, StringComparison.Ordinal);
        var star = SchematicExporter.ExportStarlarkScript(multi);
        Assert.Contains("StarLark-cs", star, StringComparison.Ordinal);
        var bundle = SchematicExporter.ExportBundleJson(multi, new SchematicExportOptions(NetlistDialect.Ngspice, AnalysisKind.OperatingPoint));
        Assert.Contains("spice.export-bundle.v2", bundle, StringComparison.Ordinal);
    }

    // ==================== 5. SPICE.APP CLI INTEGRATION ====================

    [Fact]
    public async Task ProgramCli_ExercisesHelpUnknownInfoLibraryCampaignRunAndSchematicFormats()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"spice_cli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            Assert.Equal(0, await Program.Main([]));
            Assert.Equal(0, await Program.Main(["--help"]));
            Assert.Equal(2, await Program.Main(["does-not-exist"]));
            Assert.Equal(2, await Program.Main(["run"]));
            Assert.Equal(2, await Program.Main(["schematic"]));

            Assert.Equal(0, await Program.Main(["info"]));
            Assert.Equal(0, await Program.Main(["library", "list"]));
            Assert.Equal(0, await Program.Main(["campaign", "--preset", "rc-sweep"]));

            // Run a SPICE deck via CLI across op, dc, ac, tran
            var deckPath = Path.Combine(tmpDir, "div.cir");
            await File.WriteAllTextAsync(deckPath, "* div\nV1 in 0 DC 10 AC 1\nR1 in out 1k\nR2 out 0 1k\nC1 out 0 1u\n.END\n");
            Assert.Equal(0, await Program.Main(["run", deckPath, "--analysis", "op"]));
            Assert.Equal(0, await Program.Main(["run", deckPath, "--analysis", "dc", "--source", "V1", "--start", "0", "--stop", "5", "--step", "1"]));
            Assert.Equal(0, await Program.Main(["run", deckPath, "--analysis", "ac"]));
            Assert.Equal(0, await Program.Main(["run", deckPath, "--analysis", "tran", "--step-s", "1e-4", "--stop-s", "1e-3"]));
            Assert.Equal(1, await Program.Main(["run", deckPath, "--analysis", "not_real"]));

            // Run schematic CLI on .spicenet writing ALL export artifacts
            var netPath = Path.Combine(tmpDir, "cli_div.spicenet");
            await File.WriteAllTextAsync(netPath, """
                netlist "cli_div" format="spice.netlist.v3" {
                  circuit {
                    part V1 "spice.vsrc" dc_volts=10V
                    part R1 "spice.resistor" ohms=1k
                    part R2 "spice.resistor" ohms=1k
                    chain V1.p -> R1 -> out -> R2 -> GND
                    net GND ground=true { V1.n; R2.n }
                  }
                }
                """);

            var svgOut = Path.Combine(tmpDir, "out.svg");
            var polyOut = Path.Combine(tmpDir, "out.spice.svg");
            var netOut = Path.Combine(tmpDir, "out.spicenet");
            var veloOut = Path.Combine(tmpDir, "out.velosim.json");
            var cjOut = Path.Combine(tmpDir, "out.circuit.json");
            var cirOut = Path.Combine(tmpDir, "out.cir");
            var bomOut = Path.Combine(tmpDir, "out.csv");
            var starOut = Path.Combine(tmpDir, "out.star");
            var bundleOut = Path.Combine(tmpDir, "out.bundle.json");

            Assert.Equal(0, await Program.Main([
                "schematic", netPath,
                "--analysis", "op", "--dialect", "ngspice",
                "--svg", svgOut,
                "--polyglot-svg", polyOut,
                "--spicenet", netOut,
                "--velosim", veloOut,
                "--circuit-json", cjOut,
                "--spice", cirOut,
                "--bom", bomOut,
                "--starlark", starOut,
                "--bundle", bundleOut
            ]));

            Assert.True(File.Exists(svgOut));
            Assert.True(File.Exists(polyOut));
            Assert.True(File.Exists(netOut));
            Assert.True(File.Exists(veloOut));
            Assert.True(File.Exists(cjOut));
            Assert.True(File.Exists(cirOut));
            Assert.True(File.Exists(bomOut));
            Assert.True(File.Exists(starOut));
            Assert.True(File.Exists(bundleOut));

            // Now feed the generated polyglot .spice.svg AND .velosim.json BACK into `schematic` CLI!
            Assert.Equal(0, await Program.Main(["schematic", polyOut, "--dialect", "spectre"]));
            Assert.Equal(0, await Program.Main(["schematic", veloOut, "--dialect", "ltspice"]));

            // Also feed legacy .spice.json
            var jsonPath = Path.Combine(tmpDir, "legacy.spice.json");
            var parsedDoc = CameoNetlistBridge.ToSchematicDocument(FlagshipNetlist.Parse(await File.ReadAllTextAsync(netPath)).Document!);
            await File.WriteAllTextAsync(jsonPath, SchematicCompiler.SerializeJson(parsedDoc));
            Assert.Equal(0, await Program.Main(["schematic", jsonPath, "--dialect", "xyce"]));

            // Fail-closed exit code 3 for broken .spicenet, broken .spice.svg, and broken .velosim.json
            var badNet = Path.Combine(tmpDir, "bad.spicenet");
            await File.WriteAllTextAsync(badNet, "netlist \"bad\" { circuit { bogus } }");
            Assert.Equal(3, await Program.Main(["schematic", badNet]));

            var badSvg = Path.Combine(tmpDir, "bad.spice.svg");
            await File.WriteAllTextAsync(badSvg, "<svg></svg>");
            Assert.Equal(3, await Program.Main(["schematic", badSvg]));

            var badVelo = Path.Combine(tmpDir, "bad.velosim.json");
            await File.WriteAllTextAsync(badVelo, "{\"format\":\"velosim.netlist.v1\"}");
            Assert.Equal(3, await Program.Main(["schematic", badVelo]));
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    private static SimulationRequestIr MakeDummyReq(int idx)
    {
        var c = new CircuitIr($"c_{idx}", ["0", "n1"],
        [
            new DeviceInstanceIr("V1", "spice.vsrc", new Dictionary<string, string> { ["p"] = "n1", ["n"] = "0" }, new Dictionary<string, double> { ["dc_volts"] = idx }),
            new DeviceInstanceIr("R1", "spice.resistor", new Dictionary<string, string> { ["p"] = "n1", ["n"] = "0" }, new Dictionary<string, double> { ["ohms"] = 1000.0 })
        ]);
        return new SimulationRequestIr($"case_{idx}", c, AnalysisKind.OperatingPoint);
    }

    private sealed class DelayedOrderCheckingBackend : ISimulationBackend
    {
        public string BackendId => "fake-delayed";
        public void Dispose() { }
        public Task<ResultManifestIr> RunAsync(SimulationRequestIr request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int idx = int.Parse(request.CaseId["case_".Length..], System.Globalization.CultureInfo.InvariantCulture);
            return Task.Delay((8 - idx) * 5, cancellationToken).ContinueWith(
                _ => new ResultManifestIr(request.CaseId, BackendId, RunTerminalStatus.Succeeded, true, []),
                cancellationToken,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }
    }
}
