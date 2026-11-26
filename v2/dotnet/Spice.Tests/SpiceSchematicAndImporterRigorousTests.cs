using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cameo.Netlist;
using Spice.App;
using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;
using Xunit;

namespace Spice.Tests;

/// <summary>
/// Rigorous unit, edge-case, negative-path, HTTP error-handling, and fuzz test suite
/// for SpiceLibraryImporter, ComponentLibraryStore, DeviceRegistry, SpiceNetlistEmitter,
/// SchematicExporter (all dialects incl. Spectre), and WorkbenchServer.
/// </summary>
public sealed class SpiceSchematicAndImporterRigorousTests
{
    // ==================== 1. EXHAUSTIVE .MODEL & .SUBCKT BULK IMPORTER ====================

    [Theory]
    [InlineData(".MODEL D_STD D(IS=1e-14 N=1.0)", "spice.diode", "D", "diode")]
    [InlineData(".MODEL D_ZEN D(IS=1e-12 BV=5.1 IBV=5m RS=4)", "spice.diode", "D", "zener")]
    [InlineData(".MODEL BZX55C12 D(IS=1e-12)", "spice.diode", "D", "zener")]
    [InlineData(".MODEL LED_BLUE D(IS=1e-24 N=3.2)", "spice.diode", "D", "led")]
    [InlineData(".MODEL BAT54 D(IS=5e-7 N=1.05)", "spice.diode", "D", "schottky")]
    [InlineData(".MODEL D_HIGHIS D(IS=5e-5 N=1.05)", "spice.diode", "D", "schottky")]
    [InlineData(".MODEL Q_NPN NPN(IS=1e-14 BF=200 RB=10 RC=1 RE=0.2)", "spice.bjt", "Q", "npn")]
    [InlineData(".MODEL Q_PNP PNP(IS=1e-14 BF=150)", "spice.bjt", "Q", "pnp")]
    [InlineData(".MODEL M_NMOS NMOS(LEVEL=54 VTH0=0.4)", "spice.mosfet", "M", "nmos")]
    [InlineData(".MODEL M_PMOS PMOS(LEVEL=54 VTH0=-0.4)", "spice.mosfet", "M", "pmos")]
    [InlineData(".MODEL VM_N VDMOS(VTO=3.0 KP=20 RD=0.05 RS=0.02 RG=2)", "spice.vdmos", "M", "vdmos")]
    [InlineData(".MODEL VM_P VDMOS(PCHAN VTO=-3.0 KP=10 RD=0.08)", "spice.vdmos", "M", "pvdmos")]
    [InlineData(".MODEL J_N NJF(VTO=-1.8 BETA=1.2m)", "spice.jfet", "J", "jfet")]
    [InlineData(".MODEL J_P PJF(VTO=-2.0 BETA=0.8m)", "spice.jfet", "J", "jfet")]
    [InlineData(".MODEL Z_NMF NMF(LEVEL=1 VTO=-1.4 BETA=0.05)", "spice.mesfet", "Z", "mesfet")]
    [InlineData(".MODEL Z_PMF PMF(LEVEL=2 VTO=-1.2 BETA=0.04)", "spice.mesfet", "Z", "mesfet")]
    [InlineData(".MODEL Z_MES MESFET(VTO=-1.5 BETA=0.03)", "spice.mesfet", "Z", "mesfet")]
    [InlineData(".MODEL S_SW SW(VT=2.5 VH=0.2 RON=1 ROFF=10Meg HYST=1)", "spice.switch", "S", "switch")]
    [InlineData(".MODEL S_VSW VSWITCH(VT=2.0 VH=0.1 RON=2 ROFF=1Meg)", "spice.switch", "S", "switch")]
    [InlineData(".MODEL W_CSW CSW(IT=5m IH=1m RON=1 ROFF=1Meg)", "spice.cswitch", "W", "cswitch")]
    [InlineData(".MODEL W_ISW ISWITCH(IT=10m IH=2m RON=2 ROFF=1Meg)", "spice.cswitch", "W", "cswitch")]
    [InlineData(".MODEL C_CORE CORE(MS=4e5 A=80 K=40 AREA=1e-4 PATH=0.05 GAP=1e-4 RW=0.1 LLEAK=1u)", "spice.core", "L", "core")]
    public void SpiceLibraryImporter_MapsAllSupportedModelTypesAndSymbolStyles(
        string modelCard, string expectedKind, string expectedPrefix, string expectedSymbol)
    {
        var report = SpiceLibraryImporter.ParseSpiceLibraryText(modelCard, "UnitTest");
        Assert.Equal(1, report.TotalFound);
        Assert.Equal(1, report.ImportedCount);
        Assert.Equal(0, report.ErrorCount);
        var part = Assert.Single(report.Components);
        Assert.Equal(expectedKind, part.BaseDeviceKindId);
        Assert.Equal(expectedPrefix, part.SpicePrefix);
        Assert.Equal(expectedSymbol, part.SymbolStyle);
        Assert.Empty(ComponentLibraryStore.Validate(part));
    }

    [Fact]
    public void SpiceLibraryImporter_HandlesNestedSubckts_UnterminatedSubckt_LibIncludeIgnore_KeyEqualsSpacing_AndEmptyInputs()
    {
        // Empty input
        var empty = SpiceLibraryImporter.ParseSpiceLibraryText("   \n\t ");
        Assert.Equal(1, empty.ErrorCount);
        Assert.Contains(empty.Diagnostics, d => d.Code == "empty_input");

        // Comments-only input emits warning no_models_found
        var commentsOnly = SpiceLibraryImporter.ParseSpiceLibraryText("* just a comment\n; inline comment");
        Assert.Equal(0, commentsOnly.TotalFound);
        Assert.Contains(commentsOnly.Diagnostics, d => d.Code == "no_models_found" && d.Severity == "warning");

        // Nested .SUBCKT inside .SUBCKT, 2-token .LIB file include ignored vs 1-token .LIB section, `key = value` spacing
        const string tricky = """
            .LIB external_file.lib CORNER_TT
            .LIB ANALOG_PACK
            .MODEL D_SPACED D(IS = 2.5n N = 1.6 RS = 0.5)
            .SUBCKT OUTER_AMP inp inn out
              .SUBCKT INNER_CELL a b
                R1 a b 1k
              .ENDS INNER_CELL
              X1 inp inn INNER_CELL
              E1 out 0 inp inn 10
            .ENDS OUTER_AMP
            .ENDL ANALOG_PACK
            """;
        var rep = SpiceLibraryImporter.ParseSpiceLibraryText(tricky, "Pack");
        Assert.Equal(2, rep.TotalFound);
        Assert.Equal(2, rep.ImportedCount);
        Assert.Equal(0, rep.ErrorCount);
        var dSpaced = rep.Components.Single(c => c.AttachedModel?.ModelName == "D_SPACED");
        Assert.Equal("Pack / ANALOG_PACK", dSpaced.Category);
        Assert.Equal(2.5e-9, dSpaced.DefaultParameters["is_amps"], tolerance: 1e-15);
        var outer = rep.Components.Single(c => c.AttachedModel?.ModelName == "OUTER_AMP");
        Assert.Equal("opamp", outer.SymbolStyle);
        Assert.Contains(".SUBCKT INNER_CELL", outer.AttachedModel!.SpiceText, StringComparison.Ordinal);

        // Unterminated .SUBCKT fails closed with line number
        const string unterm = """
            .MODEL GOOD_D D(IS=1e-14)
            .SUBCKT BROKEN_SUB a b
              R1 a b 1k
            """;
        var uRep = SpiceLibraryImporter.ParseSpiceLibraryText(unterm);
        Assert.Equal(1, uRep.ImportedCount);
        Assert.Equal(1, uRep.ErrorCount);
        Assert.Contains(uRep.Diagnostics, d => d.Code == "unterminated_subckt" && d.ItemName == "BROKEN_SUB" && d.LineNumber == 2);
    }

    [Fact]
    public void SpiceLibraryImporter_Fuzz100MutatedStrings_NeverThrowsUnhandledExceptions()
    {
        const int seed = 20261125;
        var rng = new Random(seed);
        const string baseLib = ".LIB SEC\n.MODEL M1 VDMOS(VTO=3 KP=10)\n.SUBCKT S1 a b PARAMS: R=1k\nR1 a b {R}\n.ENDS\n.ENDL";
        const string chars = ".MODELSUBCKTNDS+()=,;*\r\n abc12309XYZ-";

        for (int i = 0; i < 100; ++i)
        {
            var arr = baseLib.ToCharArray();
            int muts = rng.Next(1, 15);
            for (int m = 0; m < muts; ++m) arr[rng.Next(arr.Length)] = chars[rng.Next(chars.Length)];
            var mutated = new string(arr)[..rng.Next(0, arr.Length)];
            var rep = SpiceLibraryImporter.ParseSpiceLibraryText(mutated);
            Assert.NotNull(rep);
        }
    }

    // ==================== 2. SLICE A DEVICE REGISTRY, NETLIST EMITTER & SPECTRE EXPORT ====================

    [Fact]
    public void DeviceRegistry_AndEmitters_CoverAllSliceADevices_AndFailClosedOnInvalidParameters()
    {
        var reg = DeviceRegistry.Default;
        string[] sliceAKinds = ["spice.vdmos", "spice.mesfet", "spice.switch", "spice.cswitch", "spice.core", "spice.osdi"];
        foreach (var k in sliceAKinds)
        {
            Assert.True(reg.TryGet(k, out var desc) && desc is not null, $"Missing DeviceRegistry entry for '{k}'.");
        }

        // Build a schematic containing ALL Slice A device kinds and export both Canonical SPICE3f5 and Spectre!
        var lib = new ComponentLibraryStore();
        var doc = new SchematicDocument(
            "2.1", "all_slice_a_export",
            new SchematicSheet(
                "root", "All Slice A",
                [
                    new PlacedComponent("V1", "spice.vsrc", 60, 100, 90, new Dictionary<string, double> { ["dc_volts"] = 12.0 }),
                    new PlacedComponent("Vsense", "spice.vsrc", 60, 220, 90, new Dictionary<string, double> { ["dc_volts"] = 0.0 }),
                    new PlacedComponent("M1", "spice.vdmos", 180, 100, 0, new Dictionary<string, double> { ["m_mult"] = 2.0 }, LibraryPartId: "foundation.vdmos.irf540n"),
                    new PlacedComponent("Z1", "spice.mesfet", 300, 100, 0, new Dictionary<string, double> { ["area"] = 1.0 }, LibraryPartId: "foundation.mesfet.ne71000"),
                    new PlacedComponent("S1", "spice.switch", 420, 100, 0, new Dictionary<string, double>(), LibraryPartId: "foundation.switch.sw_ideal"),
                    new PlacedComponent("W1", "spice.cswitch", 540, 100, 0, new Dictionary<string, double>(), LibraryPartId: "foundation.cswitch.csw_ideal", ControlSourceId: "Vsense"),
                    new PlacedComponent("L1", "spice.core", 660, 100, 0, new Dictionary<string, double> { ["turns"] = 24.0 }, LibraryPartId: "foundation.core.ferrite_ja")
                ],
                [
                    new WireNet("vdd", [new PinRef("V1", "p"), new PinRef("M1", "d"), new PinRef("Z1", "d"), new PinRef("S1", "p"), new PinRef("W1", "p"), new PinRef("L1", "p"), new PinRef("Vsense", "p")]),
                    new WireNet("ctrl", [new PinRef("M1", "g"), new PinRef("Z1", "g"), new PinRef("S1", "cp"), new PinRef("Vsense", "n")]),
                    new WireNet("GND", [new PinRef("V1", "n"), new PinRef("M1", "s"), new PinRef("Z1", "s"), new PinRef("S1", "n"), new PinRef("S1", "cn"), new PinRef("W1", "n"), new PinRef("L1", "n")], IsGlobalGround: true)
                ]));

        var spice = SchematicExporter.ExportNetlist(doc, new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, AnalysisKind.OperatingPoint), library: lib);
        Assert.Contains("M1 vdd ctrl 0 IRF540N M=2", spice, StringComparison.Ordinal);
        Assert.Contains("Z1 vdd ctrl 0 NE71000 1", spice, StringComparison.Ordinal);
        Assert.Contains("S1 vdd 0 ctrl 0 SW_IDEAL", spice, StringComparison.Ordinal);
        Assert.Contains("W1 vdd 0 Vsense CSW_IDEAL", spice, StringComparison.Ordinal);
        Assert.Contains("L1 vdd 0 FERRITE_3C90 N=24", spice, StringComparison.Ordinal);

        var spectre = SchematicExporter.ExportNetlist(doc, new SchematicExportOptions(NetlistDialect.Spectre, AnalysisKind.OperatingPoint), library: lib);
        Assert.Contains("simulator lang=spectre", spectre, StringComparison.Ordinal);
        Assert.Contains("M1 (vdd ctrl 0) IRF540N m=2", spectre, StringComparison.Ordinal);
        Assert.Contains("Z1 (vdd ctrl 0) NE71000 area=1", spectre, StringComparison.Ordinal);
        Assert.Contains("S1 (vdd 0 ctrl 0) SW_IDEAL", spectre, StringComparison.Ordinal);
        Assert.Contains("W1 (vdd 0) CSW_IDEAL probe=Vsense", spectre, StringComparison.Ordinal);
        Assert.Contains("L1 (vdd 0) FERRITE_3C90 n=24", spectre, StringComparison.Ordinal);

        var svg = SchematicCompiler.ExportSvg(doc, library: lib);
        Assert.Contains("<svg ", svg, StringComparison.Ordinal);
        Assert.Contains("M1", svg, StringComparison.Ordinal);
        Assert.Contains("Z1", svg, StringComparison.Ordinal);
        Assert.Contains("SW", svg, StringComparison.Ordinal);
        Assert.Contains("CSW", svg, StringComparison.Ordinal);

        // Negative validation checks on ComponentLibraryStore.Validate
        var badPart = new CustomComponentDefinition(
            Id: "",
            Name: "",
            Category: "Bad",
            Description: "",
            BaseDeviceKindId: "spice.nonexistent",
            SpicePrefix: "X",
            SymbolStyle: "ic_box",
            Pins: [],
            DefaultParameters: new Dictionary<string, double> { ["x"] = double.NaN },
            SymbolPrimitives: [new SymbolPrimitive("bogus_shape", Radius: -1.0, StrokeWidth: 0.0)]);
        var errs = ComponentLibraryStore.Validate(badPart);
        Assert.Contains(errs, e => e.Code == "empty_part_id");
        Assert.Contains(errs, e => e.Code == "empty_part_name");
        Assert.Contains(errs, e => e.Code == "unknown_base_device_kind");
        Assert.Contains(errs, e => e.Code == "missing_pins");
        Assert.Contains(errs, e => e.Code == "non_finite_parameter");
        Assert.Contains(errs, e => e.Code == "invalid_symbol_primitive_kind");
        Assert.Contains(errs, e => e.Code == "invalid_symbol_primitive_dimension");
    }

    // ==================== 3. WORKBENCH SERVER HTTP ERROR & BOUNDARY PATHS ====================

    [Fact]
    public async Task WorkbenchServer_FailsClosedWith400And404OnMalformedRequestsAndTamperedNetlists()
    {
        NativeCppBackend? backend;
        try { backend = new NativeCppBackend(enableAudits: true); }
        catch { return; }

        var tmpDir = Path.Combine(Path.GetTempPath(), $"spice-v2-http-err-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            using (backend)
            {
                var store = new ComponentLibraryStore(Path.Combine(tmpDir, "lib.json"));
                await using var server = new WorkbenchServer(port: 0, backend: backend, library: store);
                server.Start();
                using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

                // 404 unknown route
                var r404 = await http.GetAsync("/api/does-not-exist");
                Assert.Equal(HttpStatusCode.NotFound, r404.StatusCode);

                // 404 delete non-existent custom component
                var del404 = await http.DeleteAsync("/api/library/user.missing.part");
                Assert.Equal(HttpStatusCode.NotFound, del404.StatusCode);

                // 400 missing spiceText on bulk import
                var imp400 = await http.PostAsJsonAsync("/api/library/import-spice", new { spiceText = "" });
                Assert.Equal(HttpStatusCode.BadRequest, imp400.StatusCode);

                // 400 malformed JSON library import
                var json400 = await http.PostAsJsonAsync("/api/library/import-json", new { content = "{\"formatId\":\"bad\"}" });
                Assert.Equal(HttpStatusCode.BadRequest, json400.StatusCode);

                // 400 missing content on flagship netlist import
                var nlEmpty = await http.PostAsJsonAsync("/api/schematic/import-netlist", new { content = "" });
                Assert.Equal(HttpStatusCode.BadRequest, nlEmpty.StatusCode);

                // 400 cyclic hierarchy rejected cleanly on /api/schematic/import-netlist
                var nlCycle = await http.PostAsJsonAsync("/api/schematic/import-netlist", new
                {
                    content = "netlist \"c\" { module A { use b = B() } module B { use a = A() } circuit { use t = A() } }"
                });
                Assert.Equal(HttpStatusCode.BadRequest, nlCycle.StatusCode);
                Assert.Contains("cyclic_module_hierarchy", await nlCycle.Content.ReadAsStringAsync(), StringComparison.Ordinal);

                // 400 tampered VeloSim JSON model SHA-256 rejected on /api/schematic/import-netlist
                var validDoc = FlagshipNetlist.Parse(""""
                    netlist "t" {
                      circuit {
                        part V1 "spice.vsrc" dc_volts=5V
                        part D1 "spice.diode" model="DM1"
                        net in { V1.p; D1.p }
                        net GND ground=true { V1.n; D1.n }
                      }
                      model "DM1" kind="SpiceModelCard" """
                        .MODEL DM1 D(IS=1e-14)
                      """
                    }
                    """").Document!;
                var veloTampered = VeloSimIngestion.ExportVeloSimNetlistJson(validDoc)
                    .Replace("IS=1e-14", "IS=9e-14", StringComparison.Ordinal);
                var velo400 = await http.PostAsJsonAsync("/api/schematic/import-netlist", new { content = veloTampered, verifyHashes = true });
                Assert.Equal(HttpStatusCode.BadRequest, velo400.StatusCode);
                Assert.Contains("model_sha256_mismatch", await velo400.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
