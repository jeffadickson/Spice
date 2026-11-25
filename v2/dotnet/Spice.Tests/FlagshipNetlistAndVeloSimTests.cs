using System.Net.Http.Json;
using System.Text.Json;
using Cameo.Netlist;
using Spice.App;
using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;
using Xunit;

namespace Spice.Tests;

public sealed class FlagshipNetlistAndVeloSimTests
{
    private static NativeCppBackend? TryCreateNativeBackend()
    {
        try
        {
            return new NativeCppBackend(enableAudits: true);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [Fact]
    public void CameoNetlist_StandaloneZeroDependencyAssembly_RoundTripsWithoutSpiceReferences()
    {
        // Prove Cameo.Netlist has ZERO references to Spice.* assemblies (safe to consume in VeloSim / FieldworkPCB / PCB projects)
        var referencedNames = typeof(FlagshipNetlist).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .ToArray();
        Assert.DoesNotContain(referencedNames, n => n.StartsWith("Spice.", StringComparison.OrdinalIgnoreCase));

        const string standalone = """
            netlist "standalone_cameo_rc" format="spice.netlist.v3" {
              module Rc(r=1k, c=100nF) {
                ports { in:in, out:out, gnd:power }
                part R1 "spice.resistor" ohms=$r footprint="0603"
                part C1 "spice.capacitor" farads=$c footprint="0603"
                chain in -> R1 -> out -> C1 -> gnd
              }
              circuit {
                part V1 "spice.vsrc" dc_volts=5V
                use f1 = Rc(r=2.2k, c=47nF)
                net in  { V1.p; f1.in }
                net out { f1.out }
                net GND ground=true { V1.n; f1.gnd }
              }
            }
            """;

        var parsed = FlagshipNetlist.Parse(standalone);
        Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));
        var elab = FlagshipNetlist.Elaborate(parsed.Document!);
        Assert.True(elab.Succeeded);
        Assert.Equal(3, elab.Parts.Count);

        var velosim = VeloSimIngestion.ExportVeloSimNetlistJson(parsed.Document!);
        var reimp = VeloSimIngestion.ImportVeloSimNetlistJson(velosim, verifyModelHashes: true);
        Assert.True(reimp.Succeeded);

        var circuitJson = VeloSimIngestion.ExportCircuitJson(parsed.Document!);
        Assert.Contains("\"source_component\"", circuitJson, StringComparison.Ordinal);
        Assert.Contains("\"pcb_component\"", circuitJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlagshipNetlist_HierarchicalModules_Chains_AutoLayout_AndLiveCppSim()
    {
        const string source = """
            // Two-stage hierarchical resistive divider + Zener clamp (zero coordinates!)
            netlist "hier_divider_zener" format="spice.netlist.v3" {
              module DividerCell(r_series=1k, r_shunt=2k) {
                ports { in:in, out:out, gnd:power }
                part Rser "spice.resistor" ohms=$r_series footprint="0603" mpn="RC0603FR-071KL" lcsc="C21190"
                part Rsh  "spice.resistor" ohms=$r_shunt  footprint="0603"
                chain in -> Rser -> out -> Rsh -> gnd
              }

              circuit {
                port in:in
                port out:out

                part V1 "spice.vsrc" dc_volts=12V ac_mag=1V
                use stage1 = DividerCell(r_series=1k, r_shunt=10k)
                part D1 "spice.diode" lib="foundation.diode.bzx84c5v1" footprint="SOT-23" mpn="BZX84C5V1"

                net in  { V1.p; stage1.in }
                net out { stage1.out; D1.n }
                net GND ground=true { V1.n; stage1.gnd; D1.p }

                directive ".TEMP 25"
              }

              pcb {
                board width=45mm height=25mm layers=2
                rule net="GND" clearance=0.2mm width=0.5mm
              }

              verify op {
                assert "v(out) in 5.05V..5.25V"
              }
            }
            """;

        var lib = new ComponentLibraryStore();
        var parsed = CameoNetlistBridge.Parse(source, lib);
        Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.NotNull(parsed.Document);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Document!.ElectricalSha256));
        Assert.False(string.IsNullOrWhiteSpace(parsed.Document.LayoutSha256));

        var elab = CameoNetlistBridge.Elaborate(parsed.Document, lib);
        Assert.True(elab.Succeeded, string.Join("; ", elab.Diagnostics.Select(d => d.Message)));
        Assert.Contains(elab.Parts, p => p.Id == "Rser_stage1" && Math.Abs(p.Parameters["ohms"] - 1000.0) < 1e-9);
        Assert.Contains(elab.Parts, p => p.Id == "Rsh_stage1" && Math.Abs(p.Parameters["ohms"] - 10000.0) < 1e-9);
        Assert.Contains(elab.Parts, p => p.Id == "D1" && p.ModelName == "BZX84C5V1");

        // Zero-coordinate input synthesizes clean grid-snapped signal-flow layout & valid SVG
        var schem = CameoNetlistBridge.ToSchematicDocument(parsed.Document, lib);
        Assert.Equal(4, schem.RootSheet.Components.Count);
        foreach (var comp in schem.RootSheet.Components)
        {
            Assert.Equal(0.0, comp.X % 20.0);
            Assert.Equal(0.0, comp.Y % 20.0);
        }
        var v1 = schem.RootSheet.Components.Single(c => c.InstanceId == "V1");
        var d1 = schem.RootSheet.Components.Single(c => c.InstanceId == "D1");
        Assert.True(v1.X < d1.X, "Signal-flow auto-placer should place source V1 left of load D1.");

        var svg = SchematicCompiler.ExportSvg(schem, library: lib);
        Assert.Contains("<svg ", svg, StringComparison.Ordinal);
        Assert.Contains("Rser_stage1", svg, StringComparison.Ordinal);

        // Live C++20 simulation of elaborated hierarchical .spicenet
        using var backend = TryCreateNativeBackend();
        if (backend is null) return;

        var deck = SchematicExporter.ExportNetlist(
            schem,
            new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, AnalysisKind.OperatingPoint),
            library: lib);
        var sim = await backend.RunSpiceDeckAsync("hier_divider_zener", deck, AnalysisKind.OperatingPoint);
        Assert.Equal(RunTerminalStatus.Succeeded, sim.Status);
        var vOut = sim.RealData!.SignalValues["v(out)"][0];
        Assert.InRange(vOut, 5.05, 5.25);
    }

    [Fact]
    public void FlagshipNetlist_DualSha256_Invariance_AndFailClosedCycleAndTamperDetection()
    {
        const string baseNetlist = """
            netlist "hash_test" format="spice.netlist.v3" {
              circuit {
                part V1 "spice.vsrc" dc_volts=10V
                part R1 "spice.resistor" ohms=1k
                part R2 "spice.resistor" ohms=2k
                chain V1.p -> R1 -> out -> R2 -> GND
                net GND ground=true { V1.n }
              }
              layout grid=20 style="lines" {
                place V1 at=(80, 140) rot=90
                place R1 at=(200, 80) rot=0
                place R2 at=(320, 140) rot=90
              }
            }
            """;

        var p1 = FlagshipNetlist.Parse(baseNetlist);
        Assert.True(p1.Succeeded);
        var formatted1 = FlagshipNetlist.Format(p1.Document!);

        // Move R1 and R2 visually in layout — electrical_sha256 MUST stay identical, layout_sha256 MUST change!
        var movedDoc = p1.Document! with
        {
            Layout = new FlagshipLayout(20.0, "dots",
            [
                new FlagshipPlacement("V1", 100, 180, 90),
                new FlagshipPlacement("R1", 260, 120, 0),
                new FlagshipPlacement("R2", 400, 180, 90)
            ])
        };
        var formatted2 = FlagshipNetlist.Format(movedDoc);
        var p2 = FlagshipNetlist.Parse(formatted2, verifyHashes: true);
        Assert.True(p2.Succeeded);

        Assert.Equal(p1.Document.ElectricalSha256, p2.Document!.ElectricalSha256);
        Assert.NotEqual(p1.Document.LayoutSha256, p2.Document.LayoutSha256);

        // Tampering electrical value without updating electrical_sha256 fails closed when verifyHashes=true
        var tampered = formatted1.Replace("ohms=1k", "ohms=4.7k", StringComparison.Ordinal);
        var badHash = FlagshipNetlist.Parse(tampered, verifyHashes: true);
        Assert.False(badHash.Succeeded);
        Assert.Contains(badHash.Diagnostics, d => d.Code == "electrical_sha256_mismatch");

        // Cyclic module hierarchy fails closed cleanly
        const string cyclic = """
            netlist "cycle_test" format="spice.netlist.v3" {
              module A { use b = B() }
              module B { use a = A() }
              circuit { use top = A() }
            }
            """;
        var parsedCycle = FlagshipNetlist.Parse(cyclic);
        Assert.True(parsedCycle.Succeeded);
        var elabCycle = FlagshipNetlist.Elaborate(parsedCycle.Document!);
        Assert.False(elabCycle.Succeeded);
        Assert.Contains(elabCycle.Diagnostics, d => d.Code == "cyclic_module_hierarchy");
    }

    [Fact]
    public void FlagshipNetlist_PolyglotSpiceSvg_RoundTripsLosslessly()
    {
        var lib = new ComponentLibraryStore();
        var schem = new SchematicDocument(
            "2.1", "polyglot_rc",
            new SchematicSheet(
                "root", "Polyglot RC",
                [
                    new PlacedComponent("V1", "spice.vsrc", 80, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 5.0 }),
                    new PlacedComponent("R1", "spice.resistor", 200, 80, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("C1", "spice.capacitor", 320, 140, 90, new Dictionary<string, double> { ["farads"] = 1e-6 })
                ],
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("C1", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("C1", "n")], IsGlobalGround: true)
                ]));

        var original = CameoNetlistBridge.FromSchematic(schem, lib);
        var polySvg = CameoNetlistBridge.ExportPolyglotSvg(schem, library: lib);

        Assert.Contains("<svg ", polySvg, StringComparison.Ordinal);
        Assert.Contains("<metadata>", polySvg, StringComparison.Ordinal);
        Assert.Contains("spice.netlist.v3", polySvg, StringComparison.Ordinal);

        var extracted = CameoNetlistBridge.ExtractFromPolyglotSvg(polySvg, lib, verifyHashes: true);
        Assert.True(extracted.Succeeded, string.Join("; ", extracted.Diagnostics.Select(d => d.Message)));
        Assert.Equal(original.ElectricalSha256, extracted.Document!.ElectricalSha256);
        Assert.Equal(original.LayoutSha256, extracted.Document.LayoutSha256);
        Assert.Equal(3, extracted.Document.Circuit.Parts.Count);
    }

    [Fact]
    public async Task VeloSimIngestion_MixedSignalExport_RoundTrip_CircuitJson_AndTamperRejection()
    {
        const string mixedSource = """
            netlist "buck_mixed_signal" format="spice.netlist.v3" {
              rtl "pwm_ctrl" format="velosim.rtl.v1" clock="clk" {
                port clk:input[1]
                port en:input[1]
                port duty:input[8]
                port pwm_out:output[1]
                port fb_high:input[1]
                bridge pwm_drv kind="dac" digital="pwm_out" analog="gate" vlow=0V vhigh=10V vth=1.65V trise=5ns tfall=5ns
                bridge fb_sense kind="adc" digital="fb_high" analog="vout" vlow=0V vhigh=3.3V vth=2.5V trise=2ns tfall=2ns
              }

              circuit {
                part Vdd   "spice.vsrc"   dc_volts=24V
                part Vgate "spice.vsrc"   dc_volts=10V
                part Rload "spice.resistor" ohms=10 footprint="1206" mpn="ERJ-8GEYJ100V" lcsc="C17902"
                part M1    "spice.vdmos"  lib="foundation.vdmos.irf540n" footprint="TO-220" mpn="IRF540N"
                part D1    "spice.diode"  lib="foundation.diode.bzx84c5v1" footprint="SOT-23"
                part L1    "spice.core"   lib="foundation.core.ferrite_ja" turns=24
                use  ctrl  = pwm_ctrl

                net vdd  { Vdd.p; Rload.p }
                net sw   { Rload.n; M1.d; L1.p }
                net vout { L1.n; D1.n }
                net gate { Vgate.p; M1.g }
                net GND ground=true { Vdd.n; Vgate.n; M1.s; D1.p }
              }

              pcb {
                board width=60mm height=40mm layers=4
                rule net="vdd" clearance=0.3mm width=1.0mm
                rule net="GND" clearance=0.2mm width=1.2mm
              }
            }
            """;

        var lib = new ComponentLibraryStore();
        var parsed = CameoNetlistBridge.Parse(mixedSource, lib);
        Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));

        // 1. Deterministic VeloSim ingestion export (zero timestamps -> identical bytes)
        var velosimJson1 = CameoNetlistBridge.ExportVeloSimNetlistJson(parsed.Document!, lib);
        var velosimJson2 = CameoNetlistBridge.ExportVeloSimNetlistJson(parsed.Document!, lib);
        Assert.Equal(velosimJson1, velosimJson2);

        using var doc = JsonDocument.Parse(velosimJson1);
        var root = doc.RootElement;
        Assert.Equal("velosim.netlist.v1", root.GetProperty("format").GetString());
        Assert.Equal("spice.netlist.v3", root.GetProperty("sourceFormat").GetString());
        Assert.Equal(parsed.Document!.ElectricalSha256, root.GetProperty("electricalSha256").GetString());

        var analog = root.GetProperty("analog");
        Assert.Equal("0", analog.GetProperty("nodes")[0].GetString());
        var elements = analog.GetProperty("elements").EnumerateArray().ToList();
        Assert.Contains(elements, e => e.GetProperty("id").GetString() == "M1" && e.GetProperty("velosimKind").GetString() == "Nvdmos");
        Assert.Contains(elements, e => e.GetProperty("id").GetString() == "D1" && e.GetProperty("velosimKind").GetString() == "Diode");
        Assert.Contains(elements, e => e.GetProperty("id").GetString() == "L1" && e.GetProperty("velosimKind").GetString() == "SaturableInductor");
        Assert.Contains("IRF540N", analog.GetProperty("spiceDeck").GetString()!, StringComparison.Ordinal);

        var digital = root.GetProperty("digital").EnumerateArray().ToList();
        Assert.Single(digital);
        Assert.Equal("velosim.rtl.v1", digital[0].GetProperty("format").GetString());
        Assert.Equal("clk", digital[0].GetProperty("clock").GetString());
        Assert.Equal(5, digital[0].GetProperty("signals").GetArrayLength());
        Assert.Equal(2, root.GetProperty("bridges").GetArrayLength());
        Assert.True(root.GetProperty("connectivity").GetArrayLength() >= 4);

        // 2. Fail-closed tamper detection on pinned model SHA-256
        var tamperedVelo = velosimJson1.Replace("BV=5.1", "BV=9.1", StringComparison.Ordinal);
        var badImport = CameoNetlistBridge.ImportVeloSimNetlistJson(tamperedVelo, lib, verifyModelHashes: true);
        Assert.False(badImport.Succeeded);
        Assert.Contains(badImport.Diagnostics, d => d.Code == "model_sha256_mismatch");

        // 3. Valid round-trip import & live C++20 simulation
        var reimported = CameoNetlistBridge.ImportVeloSimNetlistJson(velosimJson1, lib, verifyModelHashes: true);
        Assert.True(reimported.Succeeded, string.Join("; ", reimported.Diagnostics.Select(d => d.Message)));
        Assert.Single(reimported.Document!.RtlBlocks);
        Assert.Equal(6, reimported.Document.Circuit.Parts.Count);

        using var backend = TryCreateNativeBackend();
        if (backend is not null)
        {
            var reSchem = CameoNetlistBridge.ToSchematicDocument(reimported.Document, lib);
            var deck = SchematicExporter.ExportNetlist(
                reSchem,
                new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, AnalysisKind.OperatingPoint),
                library: lib);
            var sim = await backend.RunSpiceDeckAsync("velosim_reimport", deck, AnalysisKind.OperatingPoint);
            Assert.Equal(RunTerminalStatus.Succeeded, sim.Status);
        }

        // 4. Derived tscircuit-aligned Circuit JSON view
        var circuitJson = CameoNetlistBridge.ExportCircuitJson(parsed.Document!, lib);
        using var cjDoc = JsonDocument.Parse(circuitJson);
        var cjItems = cjDoc.RootElement.EnumerateArray().ToList();
        Assert.Contains(cjItems, x => x.GetProperty("type").GetString() == "pcb_board");
        Assert.Contains(cjItems, x => x.GetProperty("type").GetString() == "source_component" && x.GetProperty("name").GetString() == "Rload");
        Assert.Contains(cjItems, x => x.GetProperty("type").GetString() == "source_port");
        Assert.Contains(cjItems, x => x.GetProperty("type").GetString() == "source_net");
        Assert.Contains(cjItems, x => x.GetProperty("type").GetString() == "source_trace");
        Assert.Contains(cjItems, x => x.GetProperty("type").GetString() == "schematic_component");
        Assert.Contains(cjItems, x => x.GetProperty("type").GetString() == "pcb_component" && x.GetProperty("footprint").GetString() == "TO-220");
    }

    [Fact]
    public async Task WorkbenchServer_FlagshipNetlistPolyglotSvgAndVeloSimEndpoints_WorkEndToEnd()
    {
        using var backend = TryCreateNativeBackend();
        if (backend is null) return;

        var tmpDir = Path.Combine(Path.GetTempPath(), $"spice-v2-flagship-srv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var store = new ComponentLibraryStore(Path.Combine(tmpDir, "lib.spice-lib.json"));
            await using var server = new WorkbenchServer(port: 0, backend: backend, library: store);
            server.Start();
            using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

            var uiHtml = await http.GetStringAsync("/");
            Assert.Contains("id=\"btn-import-spicenet\"", uiHtml, StringComparison.Ordinal);
            Assert.Contains("value=\"spicenet\"", uiHtml, StringComparison.Ordinal);
            Assert.Contains("value=\"polyglot_svg\"", uiHtml, StringComparison.Ordinal);
            Assert.Contains("value=\"velosim\"", uiHtml, StringComparison.Ordinal);
            Assert.Contains("value=\"circuit_json\"", uiHtml, StringComparison.Ordinal);

            const string netlist = """
                netlist "srv_flagship_rc" format="spice.netlist.v3" {
                  module Stage(r=1k, c=1uF) {
                    ports { in:in, out:out, gnd:power }
                    part R1 "spice.resistor" ohms=$r
                    part C1 "spice.capacitor" farads=$c
                    chain in -> R1 -> out -> C1 -> gnd
                  }
                  circuit {
                    part V1 "spice.vsrc" dc_volts=10V
                    part Rload "spice.resistor" ohms=1k
                    use s1 = Stage(r=1k, c=1uF)
                    net in  { V1.p; s1.in }
                    net out { s1.out; Rload.p }
                    net GND ground=true { V1.n; s1.gnd; Rload.n }
                  }
                }
                """;

            var impResp = await http.PostAsJsonAsync("/api/schematic/import-netlist", new { content = netlist });
            impResp.EnsureSuccessStatusCode();
            using var impJson = JsonDocument.Parse(await impResp.Content.ReadAsStringAsync());
            Assert.True(impJson.RootElement.GetProperty("imported").GetBoolean());
            Assert.Equal("spice.netlist.v3", impJson.RootElement.GetProperty("format").GetString());
            var docEl = impJson.RootElement.GetProperty("document");

            var simResp = await http.PostAsJsonAsync("/api/schematic/simulate", new { document = docEl, analysis = "op", showOp = true });
            simResp.EnsureSuccessStatusCode();
            using var simJson = JsonDocument.Parse(await simResp.Content.ReadAsStringAsync());
            Assert.Equal("Succeeded", simJson.RootElement.GetProperty("status").GetString());
            Assert.Equal(5.0, simJson.RootElement.GetProperty("op_voltages").GetProperty("out").GetDouble(), precision: 5);

            async Task<string> ExportFmt(string fmt)
            {
                var r = await http.PostAsJsonAsync("/api/schematic/export", new { document = docEl, format = fmt, analysis = "op" });
                r.EnsureSuccessStatusCode();
                using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return j.RootElement.GetProperty("content").GetString()!;
            }

            var spicenetOut = await ExportFmt("spicenet");
            var polySvgOut = await ExportFmt("polyglot_svg");
            var velosimOut = await ExportFmt("velosim");
            var circuitJsonOut = await ExportFmt("circuit_json");

            Assert.Contains("format=\"spice.netlist.v3\"", spicenetOut, StringComparison.Ordinal);
            Assert.Contains("<spice-netlist format=\"spice.netlist.v3\">", polySvgOut, StringComparison.Ordinal);
            Assert.Contains("\"velosim.netlist.v1\"", velosimOut, StringComparison.Ordinal);
            Assert.Contains("\"source_component\"", circuitJsonOut, StringComparison.Ordinal);

            var polyReimp = await http.PostAsJsonAsync("/api/schematic/import-netlist", new { content = polySvgOut, verifyHashes = true });
            polyReimp.EnsureSuccessStatusCode();
            using var polyJson = JsonDocument.Parse(await polyReimp.Content.ReadAsStringAsync());
            Assert.Equal("polyglot_svg", polyJson.RootElement.GetProperty("format").GetString());

            var veloReimp = await http.PostAsJsonAsync("/api/schematic/import-netlist", new { content = velosimOut, verifyHashes = true });
            veloReimp.EnsureSuccessStatusCode();
            using var veloJson = JsonDocument.Parse(await veloReimp.Content.ReadAsStringAsync());
            Assert.Equal("velosim.netlist.v1", veloJson.RootElement.GetProperty("format").GetString());
            Assert.Equal(
                polyJson.RootElement.GetProperty("electrical_sha256").GetString(),
                veloJson.RootElement.GetProperty("electrical_sha256").GetString());
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
