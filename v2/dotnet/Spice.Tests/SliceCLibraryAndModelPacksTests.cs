using System.Net.Http.Json;
using System.Text.Json;
using Spice.App;
using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;
using Xunit;

namespace Spice.Tests;

public sealed class SliceCLibraryAndModelPacksTests
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

    private static async Task<ResultManifestIr> SimulateSchematicDeckAsync(
        NativeCppBackend backend,
        ComponentLibraryStore library,
        SchematicDocument doc,
        AnalysisKind analysis = AnalysisKind.OperatingPoint,
        IReadOnlyDictionary<string, double>? analysisOptions = null)
    {
        var enriched = SchematicCompiler.EmbedReferencedLibraryAssets(doc, library);
        var compiled = SchematicCompiler.CompileToIr(enriched, library: library);
        Assert.True(compiled.Succeeded, "ERC failed: " + string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var deck = SchematicExporter.ExportNetlist(
            enriched,
            new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, analysis, analysisOptions),
            library: library);

        return await backend.RunSpiceDeckAsync(
            doc.Name,
            deck,
            analysis,
            new SpiceDeckExecutionOptions(NumericOptions: analysisOptions));
    }

    [Fact]
    public void FoundationLibrary_ExpandedModelPacks_AllValidateAndRenderSvg()
    {
        var store = new ComponentLibraryStore();
        var all = store.ListAll();

        // Expanded curated foundation packs across all 6 categories (34+ built-in parts).
        Assert.True(all.Count >= 34, $"Expected at least 34 built-in foundation parts, got {all.Count}.");

        // Backward compatibility: all original 9 foundation IDs remain present.
        string[] legacyIds =
        [
            "foundation.diode.1n4148",
            "foundation.diode.1n5819",
            "foundation.bjt.2n2222",
            "foundation.bjt.2n3906",
            "foundation.mos.bsim4_nch65",
            "foundation.mos.bsim4_pch65",
            "foundation.jfet.2n5457",
            "foundation.subckt.ideal_opamp",
            "foundation.subckt.rc_snubber"
        ];
        foreach (var id in legacyIds)
        {
            Assert.True(store.TryGet(id, out var legacyPart) && legacyPart is not null, $"Missing legacy foundation part '{id}'.");
        }

        // New Slice A / Slice C foundation parts present.
        string[] newIds =
        [
            "foundation.diode.1n4007",
            "foundation.diode.bzx84c5v1",
            "foundation.diode.bzx84c3v3",
            "foundation.diode.led_red",
            "foundation.bjt.2n3904",
            "foundation.bjt.bc547b",
            "foundation.bjt.bc557b",
            "foundation.bjt.tip31c",
            "foundation.bjt.tip32c",
            "foundation.vdmos.2n7002",
            "foundation.vdmos.irf540n",
            "foundation.vdmos.irf9540n",
            "foundation.vdmos.bss84",
            "foundation.jfet.j201",
            "foundation.jfet.2n5460",
            "foundation.mesfet.ne71000",
            "foundation.switch.sw_ideal",
            "foundation.switch.sw_hyst",
            "foundation.cswitch.csw_ideal",
            "foundation.core.ferrite_ja",
            "foundation.subckt.tl072",
            "foundation.subckt.lm358",
            "foundation.subckt.comparator",
            "foundation.subckt.reg_7805",
            "foundation.subckt.center_tap_xfmr"
        ];
        foreach (var id in newIds)
        {
            Assert.True(store.TryGet(id, out var part) && part is not null, $"Missing new foundation part '{id}'.");
        }

        // Every built-in part passes fail-closed validation, has content SHA-256, and exports crisp SVG.
        foreach (var part in all)
        {
            var errs = ComponentLibraryStore.Validate(part);
            Assert.True(errs.Count == 0, $"Foundation part '{part.Id}' failed validation: {string.Join("; ", errs.Select(e => e.Message))}");
            Assert.False(string.IsNullOrWhiteSpace(part.ContentSha256), $"Foundation part '{part.Id}' missing ContentSha256.");
            var svg = SchematicCompiler.ExportSymbolPreviewSvg(part);
            Assert.Contains("<svg ", svg, StringComparison.Ordinal);
            Assert.Contains("</svg>", svg, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FoundationPacks_LiveSimulateThroughNativeCppBackend()
    {
        using var backend = TryCreateNativeBackend();
        if (backend is null) return;

        var lib = new ComponentLibraryStore();

        // 1. BZX84C5V1 5.1V Zener Regulator (12V -> 1k -> out, cathode at out, anode at GND)
        var zenerDoc = new SchematicDocument(
            "2.1", "zener_5v1",
            new SchematicSheet(
                "root", "Zener 5.1V",
                [
                    new PlacedComponent("V1", "spice.vsrc", 80, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 12.0 }),
                    new PlacedComponent("R1", "spice.resistor", 200, 80, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("D1", "spice.diode", 320, 140, 90, new Dictionary<string, double>(), LibraryPartId: "foundation.diode.bzx84c5v1")
                ],
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("D1", "n")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("D1", "p")], IsGlobalGround: true)
                ]));
        var zenerRes = await SimulateSchematicDeckAsync(backend, lib, zenerDoc);
        Assert.Equal(RunTerminalStatus.Succeeded, zenerRes.Status);
        var vZener = zenerRes.RealData!.SignalValues["v(out)"][0];
        Assert.InRange(vZener, 5.05, 5.25);

        // 2. IRF540N Power VDMOS (N-Ch) ON vs OFF
        SchematicDocument MakeVdmosDoc(double vg) => new(
            "2.1", "irf540n_switch",
            new SchematicSheet(
                "root", "IRF540N Switch",
                [
                    new PlacedComponent("Vdd", "spice.vsrc", 60, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 24.0 }),
                    new PlacedComponent("Vg", "spice.vsrc", 60, 260, 90, new Dictionary<string, double> { ["dc_volts"] = vg }),
                    new PlacedComponent("Rload", "spice.resistor", 220, 80, 0, new Dictionary<string, double> { ["ohms"] = 10.0 }),
                    new PlacedComponent("M1", "spice.vdmos", 320, 160, 0, new Dictionary<string, double> { ["m_mult"] = 1.0 }, LibraryPartId: "foundation.vdmos.irf540n")
                ],
                [
                    new WireNet("vdd", [new PinRef("Vdd", "p"), new PinRef("Rload", "p")]),
                    new WireNet("out", [new PinRef("Rload", "n"), new PinRef("M1", "d")]),
                    new WireNet("gate", [new PinRef("Vg", "p"), new PinRef("M1", "g")]),
                    new WireNet("GND", [new PinRef("Vdd", "n"), new PinRef("Vg", "n"), new PinRef("M1", "s")], IsGlobalGround: true)
                ]));
        var vdmosOn = await SimulateSchematicDeckAsync(backend, lib, MakeVdmosDoc(10.0));
        var vdmosOff = await SimulateSchematicDeckAsync(backend, lib, MakeVdmosDoc(0.0));
        Assert.Equal(RunTerminalStatus.Succeeded, vdmosOn.Status);
        Assert.Equal(RunTerminalStatus.Succeeded, vdmosOff.Status);
        Assert.True(vdmosOn.RealData!.SignalValues["v(out)"][0] < 0.5, "IRF540N ON should pull drain below 0.5V.");
        Assert.True(vdmosOff.RealData!.SignalValues["v(out)"][0] > 23.5, "IRF540N OFF should hold drain near 24V.");

        // 3. IRF9540N P-Ch Power VDMOS high-side switch (Vs=24V, Vg=14V -> Vgs=-10V ON)
        var pvdmosDoc = new SchematicDocument(
            "2.1", "irf9540n_highside",
            new SchematicSheet(
                "root", "IRF9540N High-Side",
                [
                    new PlacedComponent("Vdd", "spice.vsrc", 60, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 24.0 }),
                    new PlacedComponent("Vg", "spice.vsrc", 60, 260, 90, new Dictionary<string, double> { ["dc_volts"] = 14.0 }),
                    new PlacedComponent("M1", "spice.vdmos", 220, 140, 0, new Dictionary<string, double> { ["m_mult"] = 1.0 }, LibraryPartId: "foundation.vdmos.irf9540n"),
                    new PlacedComponent("Rload", "spice.resistor", 340, 200, 90, new Dictionary<string, double> { ["ohms"] = 20.0 })
                ],
                [
                    new WireNet("vdd", [new PinRef("Vdd", "p"), new PinRef("M1", "s")]),
                    new WireNet("gate", [new PinRef("Vg", "p"), new PinRef("M1", "g")]),
                    new WireNet("out", [new PinRef("M1", "d"), new PinRef("Rload", "p")]),
                    new WireNet("GND", [new PinRef("Vdd", "n"), new PinRef("Vg", "n"), new PinRef("Rload", "n")], IsGlobalGround: true)
                ]));
        var pvdmosRes = await SimulateSchematicDeckAsync(backend, lib, pvdmosDoc);
        Assert.Equal(RunTerminalStatus.Succeeded, pvdmosRes.Status);
        Assert.True(pvdmosRes.RealData!.SignalValues["v(out)"][0] > 23.0, "IRF9540N ON should drive load above 23V.");

        // 4. NE71000 GaAs RF MESFET common-source DC bias
        var mesfetDoc = new SchematicDocument(
            "2.1", "ne71000_amp",
            new SchematicSheet(
                "root", "NE71000 RF Amp",
                [
                    new PlacedComponent("Vdd", "spice.vsrc", 60, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 5.0 }),
                    new PlacedComponent("Vg", "spice.vsrc", 60, 260, 90, new Dictionary<string, double> { ["dc_volts"] = -0.4 }),
                    new PlacedComponent("Rd", "spice.resistor", 220, 80, 0, new Dictionary<string, double> { ["ohms"] = 100.0 }),
                    new PlacedComponent("Z1", "spice.mesfet", 320, 160, 0, new Dictionary<string, double> { ["area"] = 1.0 }, LibraryPartId: "foundation.mesfet.ne71000")
                ],
                [
                    new WireNet("vdd", [new PinRef("Vdd", "p"), new PinRef("Rd", "p")]),
                    new WireNet("out", [new PinRef("Rd", "n"), new PinRef("Z1", "d")]),
                    new WireNet("gate", [new PinRef("Vg", "p"), new PinRef("Z1", "g")]),
                    new WireNet("GND", [new PinRef("Vdd", "n"), new PinRef("Vg", "n"), new PinRef("Z1", "s")], IsGlobalGround: true)
                ]));
        var mesfetRes = await SimulateSchematicDeckAsync(backend, lib, mesfetDoc);
        Assert.Equal(RunTerminalStatus.Succeeded, mesfetRes.Status);
        var vMesDrain = mesfetRes.RealData!.SignalValues["v(out)"][0];
        Assert.InRange(vMesDrain, 0.5, 4.5);

        // 5. 2N2222 full parasitic BJT amplifier
        var bjtDoc = new SchematicDocument(
            "2.1", "bjt_2n2222_full",
            new SchematicSheet(
                "root", "2N2222 Bias",
                [
                    new PlacedComponent("Vcc", "spice.vsrc", 60, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 12.0 }),
                    new PlacedComponent("Rb1", "spice.resistor", 160, 80, 90, new Dictionary<string, double> { ["ohms"] = 47000.0 }),
                    new PlacedComponent("Rb2", "spice.resistor", 160, 200, 90, new Dictionary<string, double> { ["ohms"] = 10000.0 }),
                    new PlacedComponent("Rc", "spice.resistor", 300, 80, 90, new Dictionary<string, double> { ["ohms"] = 2200.0 }),
                    new PlacedComponent("Re", "spice.resistor", 300, 220, 90, new Dictionary<string, double> { ["ohms"] = 470.0 }),
                    new PlacedComponent("Q1", "spice.bjt", 260, 150, 0, new Dictionary<string, double> { ["area"] = 1.0 }, LibraryPartId: "foundation.bjt.2n2222")
                ],
                [
                    new WireNet("vcc", [new PinRef("Vcc", "p"), new PinRef("Rb1", "p"), new PinRef("Rc", "p")]),
                    new WireNet("vb", [new PinRef("Rb1", "n"), new PinRef("Rb2", "p"), new PinRef("Q1", "b")]),
                    new WireNet("vc", [new PinRef("Rc", "n"), new PinRef("Q1", "c")]),
                    new WireNet("ve", [new PinRef("Q1", "e"), new PinRef("Re", "p")]),
                    new WireNet("GND", [new PinRef("Vcc", "n"), new PinRef("Rb2", "n"), new PinRef("Re", "n")], IsGlobalGround: true)
                ]));
        var bjtRes = await SimulateSchematicDeckAsync(backend, lib, bjtDoc);
        Assert.Equal(RunTerminalStatus.Succeeded, bjtRes.Status);
        Assert.InRange(bjtRes.RealData!.SignalValues["v(vc)"][0], 3.0, 9.0);

        // 6. SW Voltage Switch & CSW Current Switch
        var swDoc = new SchematicDocument(
            "2.1", "sw_and_csw",
            new SchematicSheet(
                "root", "Switches",
                [
                    new PlacedComponent("Vsup", "spice.vsrc", 60, 100, 90, new Dictionary<string, double> { ["dc_volts"] = 10.0 }),
                    new PlacedComponent("Vctrl", "spice.vsrc", 60, 220, 90, new Dictionary<string, double> { ["dc_volts"] = 5.0 }),
                    new PlacedComponent("Rsw", "spice.resistor", 180, 80, 0, new Dictionary<string, double> { ["ohms"] = 100.0 }),
                    new PlacedComponent("S1", "spice.switch", 280, 140, 0, new Dictionary<string, double>(), LibraryPartId: "foundation.switch.sw_ideal"),
                    new PlacedComponent("Vsense", "spice.vsrc", 60, 340, 0, new Dictionary<string, double> { ["dc_volts"] = 0.0 }),
                    new PlacedComponent("Rsense", "spice.resistor", 180, 340, 0, new Dictionary<string, double> { ["ohms"] = 500.0 }),
                    new PlacedComponent("Rload2", "spice.resistor", 320, 300, 0, new Dictionary<string, double> { ["ohms"] = 100.0 }),
                    new PlacedComponent("W1", "spice.cswitch", 420, 340, 0, new Dictionary<string, double>(), LibraryPartId: "foundation.cswitch.csw_ideal", ControlSourceId: "Vsense")
                ],
                [
                    new WireNet("vsup", [new PinRef("Vsup", "p"), new PinRef("Rsw", "p"), new PinRef("Vsense", "p"), new PinRef("Rload2", "p")]),
                    new WireNet("ctrl", [new PinRef("Vctrl", "p"), new PinRef("S1", "cp")]),
                    new WireNet("out_sw", [new PinRef("Rsw", "n"), new PinRef("S1", "p")]),
                    new WireNet("s_mid", [new PinRef("Vsense", "n"), new PinRef("Rsense", "p")]),
                    new WireNet("out_csw", [new PinRef("Rload2", "n"), new PinRef("W1", "p")]),
                    new WireNet("GND", [new PinRef("Vsup", "n"), new PinRef("Vctrl", "n"), new PinRef("S1", "n"), new PinRef("S1", "cn"), new PinRef("Rsense", "n"), new PinRef("W1", "n")], IsGlobalGround: true)
                ]));
        var swRes = await SimulateSchematicDeckAsync(backend, lib, swDoc);
        Assert.Equal(RunTerminalStatus.Succeeded, swRes.Status);
        // Both SW (Vctrl=5V > 2.5V) and CSW (Isense=20mA > 5mA) are ON (RON=1 Ohm against 100 Ohm -> ~0.099V)
        Assert.InRange(swRes.RealData!.SignalValues["v(out_sw)"][0], 0.05, 0.25);
        Assert.InRange(swRes.RealData!.SignalValues["v(out_csw)"][0], 0.05, 0.25);

        // 7. Ferrite Saturable Core (CORE) transient
        var coreDoc = new SchematicDocument(
            "2.1", "ferrite_core_tran",
            new SchematicSheet(
                "root", "Saturable Core",
                [
                    new PlacedComponent("V1", "spice.vsrc", 60, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 5.0 }),
                    new PlacedComponent("R1", "spice.resistor", 180, 80, 0, new Dictionary<string, double> { ["ohms"] = 10.0 }),
                    new PlacedComponent("L1", "spice.core", 300, 140, 90, new Dictionary<string, double> { ["turns"] = 24.0 }, LibraryPartId: "foundation.core.ferrite_ja")
                ],
                [
                    new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                    new WireNet("out", [new PinRef("R1", "n"), new PinRef("L1", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("L1", "n")], IsGlobalGround: true)
                ]));
        var coreRes = await SimulateSchematicDeckAsync(
            backend, lib, coreDoc, AnalysisKind.Transient,
            new Dictionary<string, double> { ["step_s"] = 1e-5, ["stop_s"] = 2e-4, ["uic"] = 1.0, ["method"] = 2.0 });
        Assert.Equal(RunTerminalStatus.Succeeded, coreRes.Status);
        Assert.True(coreRes.RealData!.AxisValues.Count >= 10);

        // 8. Foundation Subcircuits: TL072 (Gain=3), 7805 5V Reg, Comparator, Center-Tapped Transformer
        var tl072Doc = new SchematicDocument(
            "2.1", "tl072_gain3",
            new SchematicSheet(
                "root", "TL072 Gain 3",
                [
                    new PlacedComponent("Vin", "spice.vsrc", 60, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 1.0 }),
                    new PlacedComponent("X1", "spice.subckt", 220, 140, 0, new Dictionary<string, double>(), LibraryPartId: "foundation.subckt.tl072"),
                    new PlacedComponent("Rf", "spice.resistor", 220, 240, 0, new Dictionary<string, double> { ["ohms"] = 20000.0 }),
                    new PlacedComponent("Rg", "spice.resistor", 120, 240, 90, new Dictionary<string, double> { ["ohms"] = 10000.0 })
                ],
                [
                    new WireNet("in", [new PinRef("Vin", "p"), new PinRef("X1", "inp")]),
                    new WireNet("fb", [new PinRef("X1", "inn"), new PinRef("Rf", "p"), new PinRef("Rg", "p")]),
                    new WireNet("out", [new PinRef("X1", "out"), new PinRef("Rf", "n")]),
                    new WireNet("GND", [new PinRef("Vin", "n"), new PinRef("Rg", "n")], IsGlobalGround: true)
                ]));
        var tl072Res = await SimulateSchematicDeckAsync(backend, lib, tl072Doc);
        Assert.Equal(RunTerminalStatus.Succeeded, tl072Res.Status);
        Assert.InRange(tl072Res.RealData!.SignalValues["v(out)"][0], 2.99, 3.01);

        var reg7805Doc = new SchematicDocument(
            "2.1", "reg_7805_test",
            new SchematicSheet(
                "root", "7805 Macro",
                [
                    new PlacedComponent("Vin", "spice.vsrc", 60, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 12.0 }),
                    new PlacedComponent("U1", "spice.subckt", 220, 140, 0, new Dictionary<string, double>(), LibraryPartId: "foundation.subckt.reg_7805"),
                    new PlacedComponent("Rload", "spice.resistor", 360, 160, 90, new Dictionary<string, double> { ["ohms"] = 100.0 })
                ],
                [
                    new WireNet("vin", [new PinRef("Vin", "p"), new PinRef("U1", "vin")]),
                    new WireNet("vout", [new PinRef("U1", "vout"), new PinRef("Rload", "p")]),
                    new WireNet("GND", [new PinRef("Vin", "n"), new PinRef("U1", "gnd"), new PinRef("Rload", "n")], IsGlobalGround: true)
                ]));
        var regRes = await SimulateSchematicDeckAsync(backend, lib, reg7805Doc);
        Assert.Equal(RunTerminalStatus.Succeeded, regRes.Status);
        Assert.InRange(regRes.RealData!.SignalValues["v(vout)"][0], 4.95, 5.15);

        var compDoc = new SchematicDocument(
            "2.1", "comparator_test",
            new SchematicSheet(
                "root", "Comparator",
                [
                    new PlacedComponent("Vcc", "spice.vsrc", 60, 80, 90, new Dictionary<string, double> { ["dc_volts"] = 5.0 }),
                    new PlacedComponent("Vp", "spice.vsrc", 60, 180, 90, new Dictionary<string, double> { ["dc_volts"] = 3.0 }),
                    new PlacedComponent("Vn", "spice.vsrc", 60, 280, 90, new Dictionary<string, double> { ["dc_volts"] = 2.0 }),
                    new PlacedComponent("U1", "spice.subckt", 240, 180, 0, new Dictionary<string, double>(), LibraryPartId: "foundation.subckt.comparator"),
                    new PlacedComponent("Rload", "spice.resistor", 380, 200, 90, new Dictionary<string, double> { ["ohms"] = 10000.0 })
                ],
                [
                    new WireNet("vcc", [new PinRef("Vcc", "p"), new PinRef("U1", "vcc")]),
                    new WireNet("inp", [new PinRef("Vp", "p"), new PinRef("U1", "inp")]),
                    new WireNet("inn", [new PinRef("Vn", "p"), new PinRef("U1", "inn")]),
                    new WireNet("out", [new PinRef("U1", "out"), new PinRef("Rload", "p")]),
                    new WireNet("GND", [new PinRef("Vcc", "n"), new PinRef("Vp", "n"), new PinRef("Vn", "n"), new PinRef("Rload", "n")], IsGlobalGround: true)
                ]));
        var compRes = await SimulateSchematicDeckAsync(backend, lib, compDoc);
        Assert.Equal(RunTerminalStatus.Succeeded, compRes.Status);
        Assert.True(compRes.RealData!.SignalValues["v(out)"][0] > 4.9, "Comparator with inp > inn should drive high near 5V.");

        var xfmrDoc = new SchematicDocument(
            "2.1", "xfmr_ac",
            new SchematicSheet(
                "root", "Center-Tapped Transformer",
                [
                    new PlacedComponent("V1", "spice.vsrc", 60, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 0.0, ["ac_mag"] = 10.0 }),
                    new PlacedComponent("T1", "spice.subckt", 220, 140, 0, new Dictionary<string, double>(), LibraryPartId: "foundation.subckt.center_tap_xfmr"),
                    new PlacedComponent("Rtop", "spice.resistor", 360, 100, 90, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                    new PlacedComponent("Rbot", "spice.resistor", 360, 200, 90, new Dictionary<string, double> { ["ohms"] = 1000.0 })
                ],
                [
                    new WireNet("p1", [new PinRef("V1", "p"), new PinRef("T1", "p1")]),
                    new WireNet("s1", [new PinRef("T1", "s1"), new PinRef("Rtop", "p")]),
                    new WireNet("s2", [new PinRef("T1", "s2"), new PinRef("Rbot", "p")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("T1", "p2"), new PinRef("T1", "ct"), new PinRef("Rtop", "n"), new PinRef("Rbot", "n")], IsGlobalGround: true)
                ]));
        var xfmrRes = await SimulateSchematicDeckAsync(
            backend, lib, xfmrDoc, AnalysisKind.Ac,
            new Dictionary<string, double> { ["f_start_hz"] = 1000.0, ["f_stop_hz"] = 1000.0, ["points_per_decade"] = 1.0 });
        Assert.Equal(RunTerminalStatus.Succeeded, xfmrRes.Status);
        var s1Sample = xfmrRes.ComplexData!.SignalValues["v(s1)"][0];
        var vs1 = double.Hypot(s1Sample.Real, s1Sample.Imaginary);
        Assert.InRange(vs1, 9.0, 10.1);
    }

    [Fact]
    public async Task BulkSpiceLibraryImporter_ParsesMultiFormatLib_PersistsAndLiveSimulates_WithFailClosedDiagnostics()
    {
        const string vendorLib = """
            * Multi-vendor SPICE .LIB test deck
            .LIB DIODES_AND_FETS
            .MODEL VEND_ZENER6V2 D(IS=1e-12 N=1.05 RS=3.5
            + BV=6.2 IBV=5m CJO=100p) ; inline comment
            .MODEL VEND_LED_GRN D(IS=1e-22 N=2.1 RS=5.0)
            .MODEL VEND_MOS_PWR VDMOS(VTO=2.8 KP=18.0 RD=0.04
            + RS=0.015 RG=2.0 CGS=1.2n CGDMAX=900p CGDMIN=60p BV=80)
            .MODEL VEND_PMOS_PWR VDMOS(PCHAN VTO=-3.0 KP=10.0 RD=0.09 RS=0.03)
            .MODEL VEND_MESFET NMF(LEVEL=1 VTO=-1.2 BETA=0.05 ALPHA=3.0 LAMBDA=0.03)
            .ENDL DIODES_AND_FETS

            .LIB SWITCHES_AND_CORES
            .MODEL VEND_SW SW(VT=2.0 VH=0.1 RON=2.0 ROFF=1Meg)
            .MODEL VEND_CSW CSW(IT=10m IH=2m RON=2.0 ROFF=1Meg)
            .MODEL VEND_CORE CORE(MS=4e5 A=70 K=30 C=0.2 AREA=1e-4 PATH=0.05 GAP=1e-4 RW=0.2)
            .ENDL SWITCHES_AND_CORES

            * Subcircuit with PARAMS and op-amp detection
            .SUBCKT VEND_OPAMP_X inp inn out PARAMS: RVAL=10k
              Rin inp inn 10Meg
              E1 drv 0 inp inn 50000
              Rout drv out 100
            .ENDS VEND_OPAMP_X

            .SUBCKT VEND_ATTEN in out gnd
              R1 in mid 50
              R2 mid out 50
              R3 mid gnd 100
            .ENDS VEND_ATTEN

            * Deliberately malformed/unsupported entries (must fail closed without aborting valid entries)
            .MODEL BAD_UNSUP MYSTERYTYPE(A=1)
            .MODEL TRUNCATED_MODEL
            .SUBCKT NO_PORTS_SUB PARAMS: X=1
              R1 1 0 1k
            .ENDS NO_PORTS_SUB
            """;

        var tmpDir = Path.Combine(Path.GetTempPath(), $"spice-v2-slicec-imp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        var libPath = Path.Combine(tmpDir, "vendor-test.spice-lib.json");

        try
        {
            var store = new ComponentLibraryStore(libPath);

            // 1. Dry-run preview parses all 10 valid parts + 3 error diagnostics without mutating store.
            var dry = store.ImportSpiceLibraryText(vendorLib, defaultCategory: "Acme Semi", overwriteExisting: false, dryRun: true);
            Assert.True(dry.DryRun);
            Assert.Equal(13, dry.TotalFound);
            Assert.Equal(10, dry.ImportedCount);
            Assert.Equal(0, dry.SkippedCount);
            Assert.Equal(3, dry.ErrorCount);
            Assert.Empty(store.ListCustom());
            Assert.False(File.Exists(libPath));

            Assert.Contains(dry.Diagnostics, d => d.Code == "unsupported_model_type" && d.ItemName == "BAD_UNSUP" && d.LineNumber > 0);
            Assert.Contains(dry.Diagnostics, d => d.Code == "malformed_model" && d.LineNumber > 0);
            Assert.Contains(dry.Diagnostics, d => d.Code == "subckt_no_ports" && d.ItemName == "NO_PORTS_SUB" && d.LineNumber > 0);

            // Verify section categories and inferred symbol styles
            Assert.Contains(dry.Components, c => c.AttachedModel?.ModelName == "VEND_ZENER6V2" && c.SymbolStyle == "zener" && c.Category == "Acme Semi / DIODES_AND_FETS");
            Assert.Contains(dry.Components, c => c.AttachedModel?.ModelName == "VEND_LED_GRN" && c.SymbolStyle == "led");
            Assert.Contains(dry.Components, c => c.AttachedModel?.ModelName == "VEND_MOS_PWR" && c.BaseDeviceKindId == "spice.vdmos" && c.SymbolStyle == "vdmos");
            Assert.Contains(dry.Components, c => c.AttachedModel?.ModelName == "VEND_PMOS_PWR" && c.BaseDeviceKindId == "spice.vdmos" && c.SymbolStyle == "pvdmos");
            Assert.Contains(dry.Components, c => c.AttachedModel?.ModelName == "VEND_MESFET" && c.BaseDeviceKindId == "spice.mesfet" && c.SymbolStyle == "mesfet");
            Assert.Contains(dry.Components, c => c.AttachedModel?.ModelName == "VEND_CORE" && c.BaseDeviceKindId == "spice.core" && c.SymbolStyle == "core");
            Assert.Contains(dry.Components, c => c.AttachedModel?.ModelName == "VEND_OPAMP_X" && c.SymbolStyle == "opamp" && c.DefaultParameters.ContainsKey("RVAL"));
            Assert.Contains(dry.Components, c => c.AttachedModel?.ModelName == "VEND_ATTEN" && c.SymbolStyle == "ic_box" && c.Pins.Count == 3);

            // 2. Real import persists all 10 valid parts to disk.
            var real = store.ImportSpiceLibraryText(vendorLib, defaultCategory: "Acme Semi", overwriteExisting: false, dryRun: false);
            Assert.False(real.DryRun);
            Assert.Equal(10, real.ImportedCount);
            Assert.Equal(0, real.SkippedCount);
            Assert.Equal(3, real.ErrorCount);
            Assert.True(File.Exists(libPath));
            Assert.Equal(10, store.ListCustom().Count);

            // 3. Re-importing with overwrite=false skips all 10 duplicates with warnings.
            var dupSkip = store.ImportSpiceLibraryText(vendorLib, defaultCategory: "Acme Semi", overwriteExisting: false, dryRun: false);
            Assert.Equal(0, dupSkip.ImportedCount);
            Assert.Equal(10, dupSkip.SkippedCount);
            Assert.Equal(10, dupSkip.Diagnostics.Count(d => d.Code == "duplicate_part_skipped"));

            // 4. Re-importing with overwrite=true replaces all 10 cleanly.
            var dupReplace = store.ImportSpiceLibraryText(vendorLib, defaultCategory: "Acme Semi", overwriteExisting: true, dryRun: false);
            Assert.Equal(10, dupReplace.ImportedCount);
            Assert.Equal(0, dupReplace.SkippedCount);

            // 5. Reload from disk into fresh store and live-simulate imported vendor VDMOS + .SUBCKT!
            var reloaded = new ComponentLibraryStore(libPath);
            Assert.Equal(10, reloaded.ListCustom().Count);
            Assert.True(reloaded.TryFindByModelName("VEND_MOS_PWR", out var vendMos) && vendMos is not null);
            Assert.True(reloaded.TryFindByModelName("VEND_ATTEN", out var vendAtten) && vendAtten is not null);

            using var backend = TryCreateNativeBackend();
            if (backend is null) return;

            var simDoc = new SchematicDocument(
                "2.1", "imported_vendor_live",
                new SchematicSheet(
                    "root", "Imported Parts Live",
                    [
                        new PlacedComponent("V1", "spice.vsrc", 60, 120, 90, new Dictionary<string, double> { ["dc_volts"] = 10.0 }),
                        new PlacedComponent("X1", "spice.subckt", 200, 120, 0, new Dictionary<string, double>(), LibraryPartId: vendAtten!.Id),
                        new PlacedComponent("Rload", "spice.resistor", 340, 160, 90, new Dictionary<string, double> { ["ohms"] = 100.0 })
                    ],
                    [
                        new WireNet("in", [new PinRef("V1", "p"), new PinRef("X1", "in")]),
                        new WireNet("out", [new PinRef("X1", "out"), new PinRef("Rload", "p")]),
                        new WireNet("GND", [new PinRef("V1", "n"), new PinRef("X1", "gnd"), new PinRef("Rload", "n")], IsGlobalGround: true)
                    ]));
            var simRes = await SimulateSchematicDeckAsync(backend, reloaded, simDoc);
            Assert.Equal(RunTerminalStatus.Succeeded, simRes.Status);
            // T-network: 50 Ohm series, 100 Ohm shunt || (50 + 100) = 60 Ohm -> Vmid = 10 * 60/110 = 5.4545V -> Vout = Vmid * 100/150 = 3.6364V
            Assert.Equal(10.0 * 60.0 / 110.0 * 100.0 / 150.0, simRes.RealData!.SignalValues["v(out)"][0], precision: 4);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task WorkbenchServer_BulkLibraryImportExportAndSliceASchematicSim_WorkEndToEnd()
    {
        using var backend = TryCreateNativeBackend();
        if (backend is null) return;

        var tmpDir = Path.Combine(Path.GetTempPath(), $"spice-v2-slicec-srv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var store1 = new ComponentLibraryStore(Path.Combine(tmpDir, "lib1.spice-lib.json"));
            await using var server1 = new WorkbenchServer(port: 0, backend: backend, library: store1);
            server1.Start();
            using var http1 = new HttpClient { BaseAddress = new Uri(server1.BaseUrl) };

            // 1. UI HTML includes bulk importer and JSON export/import controls
            var html = await http1.GetStringAsync("/");
            Assert.Contains("id=\"bulk-spice-text\"", html, StringComparison.Ordinal);
            Assert.Contains("id=\"btn-bulk-preview\"", html, StringComparison.Ordinal);
            Assert.Contains("id=\"btn-lib-export-json\"", html, StringComparison.Ordinal);
            Assert.Contains("value=\"spice.vdmos\"", html, StringComparison.Ordinal);

            const string miniLib = """
                .MODEL SRV_ZENER D(IS=1e-12 N=1.05 RS=2.0 BV=4.7 IBV=5m)
                .MODEL SRV_VDMOS VDMOS(VTO=2.5 KP=15.0 RD=0.05 RS=0.02 RG=2.0)
                """;

            // 2. Dry-run preview via POST /api/library/import-spice
            var dryResp = await http1.PostAsJsonAsync("/api/library/import-spice", new
            {
                spiceText = miniLib,
                defaultCategory = "HTTP Vendor",
                overwrite = false,
                dryRun = true
            });
            dryResp.EnsureSuccessStatusCode();
            using var dryJson = JsonDocument.Parse(await dryResp.Content.ReadAsStringAsync());
            Assert.True(dryJson.RootElement.GetProperty("dry_run").GetBoolean());
            Assert.Equal(2, dryJson.RootElement.GetProperty("imported_count").GetInt32());
            Assert.Empty(store1.ListCustom());
            Assert.Contains("<svg ", dryJson.RootElement.GetProperty("components")[0].GetProperty("preview_svg").GetString()!, StringComparison.Ordinal);

            // 3. Real import via POST /api/library/import-spice
            var impResp = await http1.PostAsJsonAsync("/api/library/import-spice", new
            {
                spiceText = miniLib,
                defaultCategory = "HTTP Vendor",
                overwrite = false,
                dryRun = false
            });
            impResp.EnsureSuccessStatusCode();
            using var impJson = JsonDocument.Parse(await impResp.Content.ReadAsStringAsync());
            Assert.False(impJson.RootElement.GetProperty("dry_run").GetBoolean());
            Assert.Equal(2, impJson.RootElement.GetProperty("imported_count").GetInt32());
            Assert.Equal(2, store1.ListCustom().Count);

            // 4. Export custom JSON via GET /api/library/export and import into a fresh WorkbenchServer via POST /api/library/import-json
            var exportedJson = await http1.GetStringAsync("/api/library/export");
            Assert.Contains("SRV_ZENER", exportedJson, StringComparison.Ordinal);
            Assert.Contains("SRV_VDMOS", exportedJson, StringComparison.Ordinal);

            var store2 = new ComponentLibraryStore(Path.Combine(tmpDir, "lib2.spice-lib.json"));
            await using var server2 = new WorkbenchServer(port: 0, backend: backend, library: store2);
            server2.Start();
            using var http2 = new HttpClient { BaseAddress = new Uri(server2.BaseUrl) };

            var jsonImpResp = await http2.PostAsJsonAsync("/api/library/import-json", new { content = exportedJson });
            jsonImpResp.EnsureSuccessStatusCode();
            using var jsonImpDoc = JsonDocument.Parse(await jsonImpResp.Content.ReadAsStringAsync());
            Assert.Equal(2, jsonImpDoc.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(2, store2.ListCustom().Count);

            // 5. Simulate a Slice A Zener schematic directly through /api/schematic/simulate on server2
            var zenerSchem = new SchematicDocument(
                "2.1", "srv_zener_live",
                new SchematicSheet(
                    "root", "Live Zener",
                    [
                        new PlacedComponent("V1", "spice.vsrc", 80, 140, 90, new Dictionary<string, double> { ["dc_volts"] = 10.0 }),
                        new PlacedComponent("R1", "spice.resistor", 200, 80, 0, new Dictionary<string, double> { ["ohms"] = 1000.0 }),
                        new PlacedComponent("D1", "spice.diode", 320, 140, 90, new Dictionary<string, double>(), LibraryPartId: "imported.d.srv_zener")
                    ],
                    [
                        new WireNet("in", [new PinRef("V1", "p"), new PinRef("R1", "p")]),
                        new WireNet("out", [new PinRef("R1", "n"), new PinRef("D1", "n")]),
                        new WireNet("GND", [new PinRef("V1", "n"), new PinRef("D1", "p")], IsGlobalGround: true)
                    ]));
            var simResp = await http2.PostAsJsonAsync("/api/schematic/simulate", new
            {
                document = zenerSchem,
                analysis = "op",
                showOp = true
            });
            simResp.EnsureSuccessStatusCode();
            using var simJson = JsonDocument.Parse(await simResp.Content.ReadAsStringAsync());
            Assert.True(simJson.RootElement.GetProperty("erc_ok").GetBoolean());
            Assert.Equal("Succeeded", simJson.RootElement.GetProperty("status").GetString());
            var vOut = simJson.RootElement.GetProperty("op_voltages").GetProperty("out").GetDouble();
            Assert.InRange(vOut, 4.65, 4.85);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
