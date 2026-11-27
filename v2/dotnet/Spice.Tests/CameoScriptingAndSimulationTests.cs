using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cameo.Netlist;
using Cameo.Netlist.Scripting;
using Spice.App;
using Spice.Cameo;
using Spice.Schematic;
using Spice.Sdk;
using Xunit;

namespace Spice.Tests;

/// <summary>
/// Comprehensive test suite for the Cameo (.cameo) scripting language integration
/// (jeffadickson/Cameo-script) powering both standalone Cameo.Netlist.Scripting
/// netlist generation and Spice.Cameo live simulation control, parallel sweeps,
/// ISO 80000 physical units, hermetic security bounds, HTTP Workbench API, and CLI.
/// </summary>
public sealed class CameoScriptingAndSimulationTests
{
    [Fact]
    public void CameoNetlistScripting_HasZeroSpiceAssemblyDependencies()
    {
        var refs = typeof(CameoNetlistScriptHost).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(refs, r => r.Name is not null && r.Name.StartsWith("Spice.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(refs, r => string.Equals(r.Name, "Cameo.Netlist", StringComparison.Ordinal));
        Assert.Contains(refs, r => string.Equals(r.Name, "Cameo.Language", StringComparison.Ordinal));
    }

    [Fact]
    public void CameoNetlistScriptHost_GeneratesDeterministicParametricLadderWithSharpUnitsAndVeloSimMixedSignal()
    {
        const string script = """
            -- Parametric 3-stage RC ladder + VeloSim mixed-signal controller using SharpUnits
            net.design("cameo_rc_mixed_signal")

            let r_val = units.qty(2.2, "kOhm").in_unit("Ohm").value
            let c_val = units.qty(47.0, "nF").in_unit("F").value

            net.module("RcStage")
            net.param("r", r_val)
            net.param("c", c_val)
            net.port("in", "in", 1)
            net.port("out", "out", 1)
            net.port("gnd", "power", 1)
            net.part("R1", "spice.resistor", "ohms=$r footprint=0603")
            net.part("C1", "spice.capacitor", "farads=$c footprint=0603")
            net.chain("in -> R1 -> out -> C1 -> gnd")
            net.end_module()

            net.rtl("pwm_ctrl", "clk", "velosim.rtl.v1")
            net.rtl_port("clk", "input", 1)
            net.rtl_port("pwm_out", "output", 1)
            net.rtl_port("sense_cmp", "input", 1)
            net.bridge("b_dac", "dac", "pwm_out", "in", 0.0, 3.3, 1.65, 0.05, 50.0)
            net.bridge("b_adc", "adc", "sense_cmp", "out", 0.0, 3.3, 1.65, 0.1, 50.0)
            net.end_rtl()

            net.vsrc("V1", "in", "GND", 3.3, 1.0)
            for i = 0, 2 {
                let stage_name = $"s{i}"
                net.use(stage_name, "RcStage")
                if i == 0 {
                    net.connect("in", $"{stage_name}.in")
                } else {
                    net.connect($"n{i}", $"{stage_name}.in")
                }
                if i == 2 {
                    net.connect("out", $"{stage_name}.out")
                } else {
                    net.connect($"n{i + 1}", $"{stage_name}.out")
                }
                net.ground($"{stage_name}.gnd")
            }

            net.pcb_board(50.0, 35.0, 4)
            net.pcb_rule("GND", 0.2, 0.4)
            net.directive(".TEMP 27")
            net.verify("op", "v(out) == 3.3V")
            print("built 3-stage ladder")
            """;

        var r1 = CameoNetlistScriptHost.Evaluate(script);
        var r2 = CameoNetlistScriptHost.Evaluate(script);

        Assert.True(r1.Succeeded, string.Join("; ", r1.Diagnostics.Select(d => d.Message)));
        Assert.True(r2.Succeeded);
        Assert.Contains("built 3-stage ladder", r1.Stdout, StringComparison.Ordinal);

        // Strict determinism across independent hermetic runs
        Assert.Equal(r1.FormattedSpicenet, r2.FormattedSpicenet);
        Assert.Equal(r1.Document!.ElectricalSha256, r2.Document!.ElectricalSha256);

        var elab = FlagshipNetlist.Elaborate(r1.Document!);
        Assert.True(elab.Succeeded);
        Assert.Equal(7, elab.Parts.Count); // V1 + 3*(R1 + C1)
        Assert.Equal(2, elab.Bridges.Count);
        Assert.Single(r1.Document.RtlBlocks);
        Assert.Equal(4, r1.Document.Pcb!.LayerCount);

        // VeloSim ingestion & Circuit JSON export succeed cleanly
        var veloJson = VeloSimIngestion.ExportVeloSimNetlistJson(r1.Document);
        var reimp = VeloSimIngestion.ImportVeloSimNetlistJson(veloJson);
        Assert.True(reimp.Succeeded);
        Assert.Equal(7, reimp.Document!.Circuit.Parts.Count);

        var cj = VeloSimIngestion.ExportCircuitJson(r1.Document);
        Assert.Contains("source_component", cj, StringComparison.Ordinal);
    }

    [Fact]
    public void CameoSimulationHost_ControlsLiveCpp20OpDcAcTranAndParallelSweepWithAssertions()
    {
        // First-order RC low-pass filter (R=1k, C=159.154943nF -> fc = 1000 Hz) + parallel divider sweep
        const string script = """
            net.design("rc_filter_qualification")

            let r_ohm = units.qty(1.0, "kOhm").in_unit("Ohm").value
            let c_f = 1.0 / (2.0 * 3.141592653589793 * 1000.0 * r_ohm)

            net.vsrc("V1", "in", "GND", 5.0, 1.0)
            net.resistor("R1", "in", "out", r_ohm, "0603")
            net.capacitor("C1", "out", "GND", c_f, "0603")

            -- 1. Operating Point
            let op_ok = sim.op()
            sim.assert_true("op_ok", op_ok)
            sim.assert_near("dc_pass_vout", sim.v("out"), 5.0, 1e-6, 1e-4)

            -- 2. AC Sweep and -3 dB cutoff verification (analytical fc = 1000 Hz)
            let ac_ok = sim.ac(10.0, 100000.0, 40)
            sim.assert_true("ac_ok", ac_ok)
            let fc = sim.cutoff_hz("v(out)", -3.0)
            sim.assert_near("rc_cutoff_1khz", fc, 1000.0, 15.0, 0.02)
            let phases = sim.phase_deg("v(out)")
            sim.assert_true("has_phase_points", #phases > 100)

            -- 3. DC Sweep
            let dc_ok = sim.dc("V1", 0.0, 10.0, 2.5)
            sim.assert_true("dc_ok", dc_ok)
            sim.assert_near("dc_sweep_final", sim.final("v(out)"), 10.0, 1e-5, 1e-4)

            -- 4. Transient step response with UIC (C starts at 0V, charges toward 5V with tau = 159.15 us)
            let tr_ok = sim.tran(5e-6, 1.0e-3, true)
            sim.assert_true("tran_ok", tr_ok)
            sim.assert_near("tran_settled", sim.final("v(out)"), 5.0, 0.05, 0.02)
            sim.assert_true("tran_monotonic_max", sim.max("v(out)") > 4.9)

            -- 5. Export canonical artifacts directly from Cameo
            let sn = sim.spicenet()
            let vj = sim.velosim()
            let cj = sim.circuit_json()
            let deck = sim.spice_deck("op")
            sim.assert_true("exported_artifacts", #sn > 20 and #vj > 20 and #cj > 20 and #deck > 20)
            print($"fc={fc}")
            """;

        using var backend = new NativeCppBackend(enableAudits: true);
        var res = CameoSimulationHost.RunScript(script, backend);

        Assert.True(res.Succeeded, string.Join("; ", res.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal(4, res.Runs.Count);
        Assert.All(res.Assertions, a => Assert.True(a.Passed, $"{a.Label}: {a.Detail}"));
        Assert.True(res.Assertions.Count >= 10);
        Assert.Contains("fc=", res.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void CameoSimulationHost_RunsDeterministicParallelCornerSweepAndPreservesOrder()
    {
        const string script = """
            net.design("divider_corner_sweep")
            net.vsrc("V1", "in", "GND", 10.0)
            net.resistor("R1", "in", "out", 1000.0)
            net.resistor("R2", "out", "GND", 1000.0)

            let r2_corners = { 1000.0, 3000.0, 9000.0, 19000.0 }
            let v_outs = sim.sweep_op("R2", "ohms", r2_corners, "out", 4)
            sim.assert_near("c1_5v", v_outs[1], 5.0, 1e-6, 1e-4)
            sim.assert_near("c2_7p5v", v_outs[2], 7.5, 1e-6, 1e-4)
            sim.assert_near("c3_9v", v_outs[3], 9.0, 1e-6, 1e-4)
            sim.assert_near("c4_9p5v", v_outs[4], 9.5, 1e-6, 1e-4)
            """;

        using var backend = new NativeCppBackend(enableAudits: true);
        var res = CameoSimulationHost.RunScript(script, backend);

        Assert.True(res.Succeeded, string.Join("; ", res.Diagnostics.Select(d => d.Message)));
        Assert.Equal(4, res.Runs.Count);
        Assert.Equal(4, res.Assertions.Count);
        Assert.All(res.Assertions, a => Assert.True(a.Passed, $"{a.Label}: {a.Detail}"));
    }

    [Fact]
    public void ExportCameoScript_RoundTripsIntoLiveCameoSimulationHost()
    {
        var doc = new SchematicDocument(
            SchemaVersion: SchematicCompiler.CurrentSchemaVersion,
            Name: "exported_divider",
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: "Divider",
                Components:
                [
                    new PlacedComponent("V1", "spice.vsrc", 80, 120, 0, new Dictionary<string, double> { ["dc_volts"] = 12.0 }),
                    new PlacedComponent("R1", "spice.resistor", 180, 120, 0, new Dictionary<string, double> { ["ohms"] = 2000.0 }),
                    new PlacedComponent("R2", "spice.resistor", 180, 220, 0, new Dictionary<string, double> { ["ohms"] = 6000.0 })
                ],
                Nets:
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("R2", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("R2", "n")], IsGlobalGround: true)
                ]));

        var exportedCameo = SchematicExporter.ExportCameoScript(doc);
        var appended = exportedCameo + "\nsim.assert_near(\"v_out_9v\", sim.v(\"out\"), 9.0, 1e-6, 1e-4)\n";

        using var backend = new NativeCppBackend(enableAudits: true);
        var res = CameoSimulationHost.RunScript(appended, backend);

        Assert.True(res.Succeeded, string.Join("; ", res.Diagnostics.Select(d => d.Message)));
        Assert.Equal(2, res.Assertions.Count);
        Assert.All(res.Assertions, a => Assert.True(a.Passed));
    }

    [Fact]
    public void CameoScripting_FailsClosedOnTypeErrorRuntimeErrorStepExhaustionAssertionFailAndUnclosedBlocks()
    {
        using var backend = new NativeCppBackend(enableAudits: true);

        // 1. Empty & oversized scripts
        Assert.Contains(CameoNetlistScriptHost.Evaluate("").Diagnostics, d => d.Code == "empty_cameo_script");
        Assert.Contains(CameoNetlistScriptHost.Evaluate(new string('a', 100), new CameoNetlistScriptOptions(MaxScriptBytes: 32)).Diagnostics, d => d.Code == "cameo_script_too_large");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.design(\"x\")", new CameoNetlistScriptOptions(MaxSteps: 0)).Diagnostics, d => d.Code == "invalid_step_budget");

        // 2. Static type error caught by Cameo compiler before execution
        var typeErr = CameoNetlistScriptHost.Evaluate("""
            net.design("bad_type")
            let x: integer = "not_an_int"
            """);
        Assert.False(typeErr.Succeeded);
        Assert.Contains(typeErr.Diagnostics, d => d.Code == "cameo_compile_error");

        // 3. Infinite loop aborted cleanly by deterministic MaxSteps budget
        var loopAbort = CameoNetlistScriptHost.Evaluate("""
            net.design("inf_loop")
            while true {
                let x = 1
            }
            """, new CameoNetlistScriptOptions(MaxSteps: 5_000));
        Assert.False(loopAbort.Succeeded);
        Assert.Contains(loopAbort.Diagnostics, d => d.Code == "cameo_aborted");

        // 4. Runtime error (invalid SI unit in net.qty)
        var rtErr = CameoNetlistScriptHost.Evaluate("""
            net.design("bad_qty")
            let q = net.qty("10xyz")
            """);
        Assert.False(rtErr.Succeeded);
        Assert.Contains(rtErr.Diagnostics, d => d.Code == "cameo_runtime_error");

        // 5. Unclosed module and unclosed rtl blocks fail closed
        var unclosedMod = CameoNetlistScriptHost.Evaluate("""
            net.design("unclosed")
            net.module("M")
            net.resistor("R1", "a", "b", 1000.0)
            """);
        Assert.False(unclosedMod.Succeeded);
        Assert.Contains(unclosedMod.Diagnostics, d => d.Code == "unclosed_module");

        var unclosedRtl = CameoNetlistScriptHost.Evaluate("""
            net.design("unclosed_r")
            net.rtl("ctrl", "clk")
            """);
        Assert.False(unclosedRtl.Succeeded);
        Assert.Contains(unclosedRtl.Diagnostics, d => d.Code == "unclosed_rtl");

        // 6. Failing sim.assert_near flips Succeeded to false with cameo_assertion_failed
        var assertFail = CameoSimulationHost.RunScript("""
            net.design("assert_fail")
            net.vsrc("V1", "in", "GND", 5.0)
            net.resistor("R1", "in", "GND", 1000.0)
            sim.op()
            sim.assert_near("wrong_v", sim.v("in"), 99.0, 1e-4, 1e-4)
            """, backend);
        Assert.False(assertFail.Succeeded);
        Assert.Contains(assertFail.Diagnostics, d => d.Code == "cameo_assertion_failed");
    }

    [Fact]
    public async Task WorkbenchServerAndCli_RunCameoScriptsEndToEnd()
    {
        const string cameoScript = """
            net.design("cli_and_http_cameo")
            net.vsrc("V1", "in", "GND", 10.0)
            net.resistor("R1", "in", "out", net.qty("2k"))
            net.resistor("R2", "out", "GND", net.qty("2k"))
            let ok = sim.op()
            sim.assert_true("op_ok", ok)
            sim.assert_near("v_half", sim.v("out"), 5.0, 1e-6, 1e-4)
            """;

        const string pureNetlistCameo = """
            net.design("pure_netlist_cameo")
            net.vsrc("V1", "in", "GND", 10.0)
            net.resistor("R1", "in", "out", 1000.0)
            net.resistor("R2", "out", "GND", 3000.0)
            """;

        var tmpDir = Path.Combine(Path.GetTempPath(), $"spice_cameo_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // 1. CLI `spice-app cameo <file.cameo>` with full artifact outputs
            var scriptPath = Path.Combine(tmpDir, "test.cameo");
            await File.WriteAllTextAsync(scriptPath, cameoScript);
            var snOut = Path.Combine(tmpDir, "gen.spicenet");
            var polyOut = Path.Combine(tmpDir, "gen.spice.svg");
            var veloOut = Path.Combine(tmpDir, "gen.velosim.json");
            var cjOut = Path.Combine(tmpDir, "gen.circuit.json");
            var cirOut = Path.Combine(tmpDir, "gen.cir");

            Assert.Equal(0, await Program.Main([
                "cameo", scriptPath,
                "--spicenet", snOut,
                "--polyglot-svg", polyOut,
                "--velosim", veloOut,
                "--circuit-json", cjOut,
                "--spice", cirOut
            ]));
            Assert.True(File.Exists(snOut));
            Assert.True(File.Exists(polyOut));
            Assert.True(File.Exists(veloOut));
            Assert.True(File.Exists(cjOut));
            Assert.True(File.Exists(cirOut));

            // 2. CLI `spice-app schematic <pure.cameo>`
            var purePath = Path.Combine(tmpDir, "pure.cameo");
            await File.WriteAllTextAsync(purePath, pureNetlistCameo);
            Assert.Equal(0, await Program.Main(["schematic", purePath]));

            // 3. WorkbenchServer HTTP POST /api/cameo/run + POST /api/schematic/import-netlist (.cameo) + POST /api/schematic/export (cameo)
            await using var server = new WorkbenchServer(0, library: new ComponentLibraryStore(Path.Combine(tmpDir, "lib.json")));
            server.Start();
            using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

            var runResp = await http.PostAsJsonAsync("/api/cameo/run", new { script = cameoScript });
            Assert.Equal(HttpStatusCode.OK, runResp.StatusCode);
            using var runJson = JsonDocument.Parse(await runResp.Content.ReadAsStringAsync());
            Assert.True(runJson.RootElement.GetProperty("succeeded").GetBoolean());
            Assert.Equal("cli_and_http_cameo", runJson.RootElement.GetProperty("design").GetString());
            Assert.Equal(2, runJson.RootElement.GetProperty("assertions").GetArrayLength());

            var badRunResp = await http.PostAsJsonAsync("/api/cameo/run", new { script = "" });
            Assert.Equal(HttpStatusCode.BadRequest, badRunResp.StatusCode);

            var impResp = await http.PostAsJsonAsync("/api/schematic/import-netlist", new { content = pureNetlistCameo });
            Assert.Equal(HttpStatusCode.OK, impResp.StatusCode);
            using var impJson = JsonDocument.Parse(await impResp.Content.ReadAsStringAsync());
            Assert.Equal("cameo.script", impJson.RootElement.GetProperty("format").GetString());
            Assert.True(impJson.RootElement.GetProperty("imported").GetBoolean());

            var schemDoc = JsonSerializer.Deserialize<SchematicDocument>(
                impJson.RootElement.GetProperty("document").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var expResp = await http.PostAsJsonAsync("/api/schematic/export", new { document = schemDoc, format = "cameo" });
            Assert.Equal(HttpStatusCode.OK, expResp.StatusCode);
            Assert.Contains("net.design", await expResp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void CameoNetlistScriptHost_ExercisesAllPartHelpersGlobalsFromSpicenetAndValidationErrors()
    {
        // Full coverage of NetlistScriptBuilder helpers + host-injected NumericGlobals & StringGlobals + from_spicenet
        const string fullScript = """
            net.from_spicenet("netlist \"seed\" { circuit { part R0 \"spice.resistor\" ohms=100 net a { R0.p } net GND ground=true { R0.n } } }")
            net.design(design_title)

            net.module("Cell")
            net.param("r_p", 1000.0)
            net.port("p", "in", 1)
            net.port("n", "out", 1)
            net.part("Rx", "spice.resistor", "ohms=$r_p footprint=\"0402\" mpn=\"RC0402\" supplier=\"DK1\" value=\"1k\"")
            net.chain("p -> Rx -> n")
            net.end_module()

            net.vsrc("V1", "vdd", "GND", v_supply)
            net.isrc("I1", "n1", "GND", 1e-3, 0.1)
            net.inductor("L1", "vdd", "n1", 10e-6, "0805")
            net.diode("D1", "n1", "n2", "DMOD")
            net.nmos("M1", "n2", "g1", "GND", "GND", 2e-6, 65e-9, "NM")
            net.pmos("M2", "n2", "g1", "vdd", "vdd", 4e-6, 65e-9, "PM")
            net.bjt("Q1", "n2", "g1", "GND", "QMOD", 1.0)
            net.use_expr("u1", "Cell", { r_p = "2.2k" })
            net.wire("g1", { "u1.p", "u1.n" })
            net.model("DMOD", ".MODEL DMOD D(IS=1e-14)", "SpiceModelCard")
            net.layout(20.0, "dots")
            net.place("V1", 80.0, 120.0, 0, false, false)

            net.rtl("core", "clk")
            net.rtl_port("clk", "input", 1)
            net.rtl_json("{\"memories\":[],\"continuous\":[]}")
            net.end_rtl()
            """;

        var opts = new CameoNetlistScriptOptions(
            NumericGlobals: new Dictionary<string, double> { ["v_supply"] = 3.3 },
            StringGlobals: new Dictionary<string, string> { ["design_title"] = "full_cameo_helpers" });

        var ok = CameoNetlistScriptHost.Evaluate(fullScript, opts);
        Assert.True(ok.Succeeded, string.Join("; ", ok.Diagnostics.Select(d => d.Message)));
        Assert.Equal("full_cameo_helpers", ok.Document!.Name);
        Assert.NotNull(ok.Document.Layout);
        Assert.Single(ok.Document.Layout!.Placements);
        Assert.Single(ok.Document.Models);

        // Negative builder validation branches fail closed with structured codes
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.design(\"\")").Diagnostics, d => d.Code == "invalid_design_name");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.module(\"A\")\nnet.module(\"B\")\nnet.end_module()").Diagnostics, d => d.Code == "nested_module_block");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.end_module()").Diagnostics, d => d.Code == "unmatched_end_module");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.rtl(\"A\")\nnet.rtl(\"B\")\nnet.end_rtl()").Diagnostics, d => d.Code == "nested_rtl_block");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.end_rtl()").Diagnostics, d => d.Code == "unmatched_end_rtl");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.rtl_port(\"a\")").Diagnostics, d => d.Code == "no_active_rtl");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.bridge(\"b\", \"dac\", \"a\", \"b\")").Diagnostics, d => d.Code == "no_active_rtl");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.rtl_json(\"{}\")").Diagnostics, d => d.Code == "no_active_rtl");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.connect(\"n1\", \"badpin\")").Diagnostics, d => d.Code == "invalid_pin_ref");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.chain(\"only_one\")").Diagnostics, d => d.Code == "short_chain");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.part(\"R1\", \"spice.resistor\", \"ohms=badval\")").Diagnostics, d => d.Code == "invalid_part_attr");
        Assert.Contains(CameoNetlistScriptHost.Evaluate("net.use_expr(\"u1\", \"M\", { r = \"badval\" })").Diagnostics, d => d.Code == "invalid_use_arg");
    }

    [Fact]
    public void CameoSimulationHost_ExercisesRunSpiceDeckCurrentProbingWaveformStatsAndDeckOnlyMode()
    {
        // Deck-only Cameo script (no net.* calls required when running sim.run_spice_deck)
        const string deckScript = """
            let ok = sim.run_spice_deck("standalone_deck", "* div\nV1 in 0 DC 10\nR1 in out 1k\nR2 out 0 1k\n.END\n", "op")
            sim.assert_true("deck_ok", ok)
            sim.assert_true("status_succeeded", sim.status() == "Succeeded")
            sim.assert_true("run_count_1", sim.run_count() == 1)
            let sigs = sim.signals()
            sim.assert_true("has_signals", #sigs >= 2)
            sim.assert_near("v_out", sim.v("out"), 5.0, 1e-6, 1e-4)
            sim.assert_near("i_v1", sim.i("V1"), -5e-3, 1e-6, 1e-3)
            let t = sim.times()
            sim.assert_true("axis_len", #t == 1)
            sim.assert_near("min_eq_max", sim.min("v(out)"), sim.max("v(out)"), 1e-9, 1e-9)
            """;

        using var backend = new NativeCppBackend(enableAudits: true);
        var res = CameoSimulationHost.RunScript(deckScript, backend);
        Assert.True(res.Succeeded, string.Join("; ", res.Diagnostics.Select(d => d.Message)));
        Assert.Single(res.Runs);
        Assert.All(res.Assertions, a => Assert.True(a.Passed, $"{a.Label}: {a.Detail}"));

        // Calling sim.v() before any simulation fails cleanly as a runtime error
        var noRun = CameoSimulationHost.RunScript("let x = sim.v(\"out\")", backend);
        Assert.False(noRun.Succeeded);
        Assert.Contains(noRun.Diagnostics, d => d.Code == "cameo_runtime_error");
    }
}
