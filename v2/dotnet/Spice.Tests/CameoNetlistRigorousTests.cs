using System.Text;
using System.Text.Json;
using Cameo.Netlist;
using Xunit;

namespace Spice.Tests;

/// <summary>
/// Rigorous unit, boundary, malformed-input, dual-hash invariance, hierarchical elaboration,
/// VeloSim ingestion, Circuit JSON, seeded property, and crash-freedom fuzz test suite
/// for the standalone zero-dependency Cameo.Netlist library.
/// </summary>
public sealed class CameoNetlistRigorousTests
{
    // ==================== 1. SI QUANTITY & UNIT PARSING ====================

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("42", 42.0)]
    [InlineData("-3.3V", -3.3)]
    [InlineData("+5.1V", 5.1)]
    [InlineData("1.5e-3", 1.5e-3)]
    [InlineData("10a", 10e-18)]
    [InlineData("10aF", 10e-18)]
    [InlineData("10aA", 10e-18)]
    [InlineData("10A", 10.0)]
    [InlineData("10f", 10e-15)]
    [InlineData("10fF", 10e-15)]
    [InlineData("10F", 10.0)]
    [InlineData("22pF", 22e-12)]
    [InlineData("100nF", 100e-9)]
    [InlineData("4.7uF", 4.7e-6)]
    [InlineData("4.7µF", 4.7e-6)]
    [InlineData("10mH", 10e-3)]
    [InlineData("1k", 1e3)]
    [InlineData("4.7kOhm", 4.7e3)]
    [InlineData("10kOhms", 10e3)]
    [InlineData("2.2Meg", 2.2e6)]
    [InlineData("10MegOhm", 10e6)]
    [InlineData("2.4GHz", 2.4e9)]
    [InlineData("1THz", 1e12)]
    [InlineData("5ns", 5e-9)]
    [InlineData("100ms", 100e-3)]
    [InlineData("25mA", 25e-3)]
    [InlineData("100uA", 100e-6)]
    [InlineData("10mil", 254e-6)]
    public void TryParseQuantity_ParsesAllSiPrefixesAndEngineeringUnits(string raw, double expected)
    {
        Assert.True(FlagshipNetlist.TryParseQuantity(raw, out var actual), $"Expected '{raw}' to parse.");
        Assert.Equal(expected, actual, tolerance: Math.Max(1e-18, Math.Abs(expected) * 1e-12));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("10xyz")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("--5")]
    [InlineData("k10")]
    public void TryParseQuantity_RejectsInvalidAndNonFiniteInputs(string raw)
    {
        Assert.False(FlagshipNetlist.TryParseQuantity(raw, out _), $"Expected '{raw}' to be rejected.");
    }

    // ==================== 2. LEXER & PARSER EDGE CASES / MALFORMED INPUTS ====================

    [Fact]
    public void Parse_SupportsAllCommentStyles_StringEscapes_AndMultilineCrlfDedent()
    {
        var text =
            "// line comment\r\n" +
            "# hash comment\r\n" +
            "/* block\r\n   comment */\r\n" +
            "netlist \"esc_\\\"name\\\"\" format=\"spice.netlist.v3\" schema=\"3.0\" {\r\n" +
            "  circuit {\r\n" +
            "    part V1 \"spice.vsrc\" dc_volts=5V display=\"5V\\tmain\"\r\n" +
            "    part D1 \"spice.diode\" model=\"MYD\"\r\n" +
            "    net in { V1.p, D1.p }\r\n" +
            "    net GND ground=true { V1.n; D1.n }\r\n" +
            "    directive \".TEMP 85\"\r\n" +
            "  }\r\n" +
            "  model \"MYD\" kind=\"SpiceModelCard\" \"\"\"\r\n" +
            "      .MODEL MYD D(\r\n" +
            "      + IS=1e-14 N=1.1)\r\n" +
            "  \"\"\"\r\n" +
            "}\r\n";

        var res = FlagshipNetlist.Parse(text);
        Assert.True(res.Succeeded, string.Join("; ", res.Diagnostics.Select(d => d.Message)));
        Assert.Equal("esc_\"name\"", res.Document!.Name);
        Assert.Single(res.Document.Models);
        Assert.StartsWith(".MODEL MYD D(", res.Document.Models[0].SpiceText, StringComparison.Ordinal);
        Assert.Contains("+ IS=1e-14 N=1.1)", res.Document.Models[0].SpiceText, StringComparison.Ordinal);
        Assert.Equal("5V\tmain", res.Document.Circuit.Parts.Single(p => p.Id == "V1").DisplayValue);
    }

    [Theory]
    [InlineData("", "empty_netlist")]
    [InlineData("   \r\n\t ", "empty_netlist")]
    [InlineData("circuit { }", "missing_netlist_header")]
    [InlineData("netlist { }", "missing_netlist_name")]
    [InlineData("netlist \"a\" format=\"bogus.v9\" { circuit { } }", "unsupported_format")]
    [InlineData("netlist \"a\" { }", "missing_circuit")]
    [InlineData("netlist \"a\" { circuit { } circuit { } }", "duplicate_circuit")]
    [InlineData("netlist \"a\" { module M { } module M { } circuit { } }", "duplicate_module")]
    [InlineData("netlist \"a\" { bogus_block { } circuit { } }", "unknown_section")]
    [InlineData("netlist \"a\" { circuit { part R1 \"spice.resistor\" ohms=1k\n part R1 \"spice.resistor\" ohms=2k } }", "duplicate_part")]
    [InlineData("netlist \"a\" { circuit { part R1 \"spice.resistor\" ohms=notanumber } }", "invalid_part_param")]
    [InlineData("netlist \"a\" { circuit { net out { R1_no_dot } } }", "invalid_pin_ref")]
    [InlineData("netlist \"a\" { circuit { net out { .leading } } }", "invalid_pin_ref")]
    [InlineData("netlist \"a\" { circuit { net out { trailing. } } }", "invalid_pin_ref")]
    [InlineData("netlist \"a\" { circuit { chain solo } }", "short_chain")]
    [InlineData("netlist \"a\" { circuit { unknown_kw 123 } }", "unknown_statement")]
    [InlineData("netlist \"a\" { circuit { part V1 \"spice.vsrc\" display=\"unterminated } }", "unterminated_string")]
    [InlineData("netlist \"a\" { circuit { } model \"M1\" \"\"\" unterminated }", "unterminated_multiline_string")]
    [InlineData("netlist \"a\" { circuit { } model \"M1\" 123 }", "missing_model_body")]
    [InlineData("netlist \"a\" { rtl \"r1\" { badstmt 1 } circuit { } }", "unknown_rtl_stmt")]
    [InlineData("netlist \"a\" { module M(x=bad) { } circuit { } }", "invalid_param_value")]
    [InlineData("netlist \"a\" { module M(x=1) { } circuit { use u1 = M(x=bad) } }", "invalid_use_arg")]
    public void Parse_FailsClosedWithExpectedDiagnosticCode(string input, string expectedCode)
    {
        var res = FlagshipNetlist.Parse(input);
        Assert.False(res.Succeeded, $"Expected parse failure '{expectedCode}' for input: {input}");
        Assert.Contains(res.Diagnostics, d => d.Code == expectedCode);
    }

    [Fact]
    public void Parse_RejectsOversizedNetlistExceeding4MiB()
    {
        var huge = "netlist \"huge\" { circuit { directive \"" + new string('A', FlagshipNetlist.MaxInputBytes + 16) + "\" } }";
        var res = FlagshipNetlist.Parse(huge);
        Assert.False(res.Succeeded);
        Assert.Contains(res.Diagnostics, d => d.Code == "netlist_too_large");
    }

    // ==================== 3. DUAL SHA-256 INVARIANCE & MUTATION MATRIX ====================

    [Fact]
    public void DualHashes_ElectricalHashIsInvariantToFormattingOrderingLayoutAndPcb_YetSensitiveToEveryElectricalChange()
    {
        const string canonical = """
            netlist "inv_test" format="spice.netlist.v3" {
              rtl "ctrl" format="velosim.rtl.v1" clock="clk" {
                port clk:input[1]
                port pwm:output[1]
                bridge drv kind="dac" digital="pwm" analog="gate" vlow=0V vhigh=3.3V vth=1.65V trise=5ns tfall=5ns
              }
              circuit {
                part V1 "spice.vsrc" dc_volts=5V
                part R1 "spice.resistor" ohms=1k
                part R2 "spice.resistor" ohms=2k
                net in { V1.p; R1.p }
                net out { R1.n; R2.p }
                net GND ground=true { V1.n; R2.n }
                directive ".TEMP 25"
              }
              layout grid=20 style="lines" {
                place V1 at=(80, 140) rot=90
                place R1 at=(200, 80) rot=0
                place R2 at=(320, 140) rot=90
              }
              pcb {
                board width=50mm height=30mm layers=2
                rule net="GND" clearance=0.2mm width=0.5mm
              }
            }
            """;

        // Same electrical circuit, completely reordered declarations + comments + moved layout + changed PCB
        const string reorderedAndMoved = """
            // extra comment!
            netlist "inv_test" format="spice.netlist.v3" {
              pcb {
                board width=100mm height=80mm layers=4
                rule net="GND" clearance=0.5mm width=1.0mm
              }
              layout grid=10 style="dots" {
                place R2 at=(500, 300) rot=180 mirror_x=true
                place V1 at=(20, 40) rot=0 mirror_y=true
                place R1 at=(120, 40) rot=270
              }
              circuit {
                directive ".TEMP 25"
                net GND ground=true { R2.n; V1.n }
                net out { R2.p; R1.n }
                net in { R1.p; V1.p }
                part R2 "spice.resistor" ohms=2000
                part R1 "spice.resistor" ohms=1000
                part V1 "spice.vsrc" dc_volts=5
              }
              rtl "ctrl" format="velosim.rtl.v1" clock="clk" {
                port pwm:output[1]
                port clk:input[1]
                bridge drv kind="dac" digital="pwm" analog="gate" vlow=0V vhigh=3.3V vth=1.65V trise=5ns tfall=5ns
              }
            }
            """;

        var d1 = FlagshipNetlist.Parse(canonical).Document!;
        var d2 = FlagshipNetlist.Parse(reorderedAndMoved).Document!;

        Assert.Equal(d1.ElectricalSha256, d2.ElectricalSha256);
        Assert.NotEqual(d1.LayoutSha256, d2.LayoutSha256);

        // Single-bit electrical mutations MUST each alter electrical_sha256
        string[] mutatedInputs =
        [
            canonical.Replace("ohms=1k", "ohms=1.001k", StringComparison.Ordinal),
            canonical.Replace("dc_volts=5V", "dc_volts=5.1V", StringComparison.Ordinal),
            canonical.Replace(".TEMP 25", ".TEMP 85", StringComparison.Ordinal),
            canonical.Replace("vhigh=3.3V", "vhigh=5.0V", StringComparison.Ordinal),
            canonical.Replace("port pwm:output[1]", "port pwm:output[2]", StringComparison.Ordinal),
            canonical.Replace("clock=\"clk\"", "clock=\"clk2\"", StringComparison.Ordinal)
        ];
        foreach (var m in mutatedInputs)
        {
            var dm = FlagshipNetlist.Parse(m).Document!;
            Assert.NotEqual(d1.ElectricalSha256, dm.ElectricalSha256);
        }

        // Verify tampered layout_sha256 is caught when verifyHashes=true
        var formatted = FlagshipNetlist.Format(d1);
        var tamperedLayout = formatted.Replace("at=(200, 80)", "at=(240, 80)", StringComparison.Ordinal);
        var badLayout = FlagshipNetlist.Parse(tamperedLayout, verifyHashes: true);
        Assert.False(badLayout.Succeeded);
        Assert.Contains(badLayout.Diagnostics, d => d.Code == "layout_sha256_mismatch");
    }

    // ==================== 4. DEEP HIERARCHY, CHAINS & FAILURE MODES ====================

    [Fact]
    public void Elaborate_3LevelNestedHierarchy_CascadingParams_ConsecutivePartChains_AndScopedControlSource()
    {
        const string deepHier = """
            netlist "three_level" format="spice.netlist.v3" {
              module LeafRes(r_val=1k) {
                ports { a:in, b:out }
                part Ra "spice.resistor" ohms=$r_val
                part Rb "spice.resistor" ohms=$r_val
                // Consecutive 2-pin parts synthesize an automatic intermediate net
                chain a -> Ra -> Rb -> b
              }
              module MidStage(stage_r=2k) {
                ports { in:in, out:out, gnd:power }
                use leaf = LeafRes(r_val=$stage_r)
                part Vs "spice.vsrc" dc_volts=0V
                part W1 "spice.cswitch" ctrl="Vs"
                net in  { leaf.a; Vs.p }
                net mid { Vs.n; W1.p }
                net out { leaf.b; W1.n }
                net GND ground=true { }
              }
              circuit {
                part V1 "spice.vsrc" dc_volts=10V
                use m1 = MidStage(stage_r=500)
                use m2 = MidStage()
                net src { V1.p; m1.in }
                net bus { m1.out; m2.in }
                net fin { m2.out }
                net GND ground=true { V1.n; m1.gnd; m2.gnd }
              }
            }
            """;

        var parsed = FlagshipNetlist.Parse(deepHier);
        Assert.True(parsed.Succeeded, string.Join("; ", parsed.Diagnostics.Select(d => d.Message)));

        var elab = FlagshipNetlist.Elaborate(parsed.Document!);
        Assert.True(elab.Succeeded, string.Join("; ", elab.Diagnostics.Select(d => d.Message)));

        // m1 overrides stage_r=500 -> LeafRes r_val=500; m2 uses default stage_r=2k -> LeafRes r_val=2000
        Assert.Equal(500.0, elab.Parts.Single(p => p.Id == "Ra_m1_leaf").Parameters["ohms"]);
        Assert.Equal(500.0, elab.Parts.Single(p => p.Id == "Rb_m1_leaf").Parameters["ohms"]);
        Assert.Equal(2000.0, elab.Parts.Single(p => p.Id == "Ra_m2_leaf").Parameters["ohms"]);
        Assert.Equal(2000.0, elab.Parts.Single(p => p.Id == "Rb_m2_leaf").Parameters["ohms"]);

        // Scoped control source ID inside module instances
        Assert.Equal("Vs_m1", elab.Parts.Single(p => p.Id == "W1_m1").ControlSourceId);
        Assert.Equal("Vs_m2", elab.Parts.Single(p => p.Id == "W1_m2").ControlSourceId);

        // Consecutive chain Ra -> Rb shares an internal net connecting Ra.n to Rb.p
        Assert.Contains(elab.Nets, n =>
            n.Pins.Contains(new NetlistPinRef("Ra_m1_leaf", "n")) &&
            n.Pins.Contains(new NetlistPinRef("Rb_m1_leaf", "p")));
    }

    [Fact]
    public void Elaborate_FailsClosedOnUnknownModule_UnresolvedParams_IndirectCycles_AndExcessDepth()
    {
        // Unknown module
        var unk = FlagshipNetlist.Elaborate(FlagshipNetlist.Parse(
            "netlist \"u\" { circuit { use x = DoesNotExist() } }").Document!);
        Assert.False(unk.Succeeded);
        Assert.Contains(unk.Diagnostics, d => d.Code == "unknown_module");

        // Unresolved $param on part
        var badPartParam = FlagshipNetlist.Elaborate(FlagshipNetlist.Parse(
            "netlist \"p\" { module M { part R1 \"spice.resistor\" ohms=$missing } circuit { use m1 = M() } }").Document!);
        Assert.False(badPartParam.Succeeded);
        Assert.Contains(badPartParam.Diagnostics, d => d.Code == "unresolved_param");

        // Unresolved $param forwarded into child use
        var badUseParam = FlagshipNetlist.Elaborate(FlagshipNetlist.Parse(
            "netlist \"up\" { module Leaf(r=1k) { part R1 \"spice.resistor\" ohms=$r } module Wrap { use l = Leaf(r=$nope) } circuit { use w = Wrap() } }").Document!);
        Assert.False(badUseParam.Succeeded);
        Assert.Contains(badUseParam.Diagnostics, d => d.Code == "unresolved_use_param");

        // 3-node indirect cycle A -> B -> C -> A
        var triCycle = FlagshipNetlist.Elaborate(FlagshipNetlist.Parse(
            "netlist \"c3\" { module A { use b = B() } module B { use c = C() } module C { use a = A() } circuit { use top = A() } }").Document!);
        Assert.False(triCycle.Succeeded);
        Assert.Contains(triCycle.Diagnostics, d => d.Code == "cyclic_module_hierarchy");

        // Excessive non-cyclic depth (> MaxHierarchyDepth = 16)
        var sb = new StringBuilder("netlist \"deep\" {\n");
        for (int i = 0; i <= FlagshipNetlist.MaxHierarchyDepth + 2; ++i)
        {
            sb.AppendLine(i == FlagshipNetlist.MaxHierarchyDepth + 2
                ? $"  module M{i} {{ part R1 \"spice.resistor\" ohms=1k }}"
                : $"  module M{i} {{ use next = M{i + 1}() }}");
        }
        sb.AppendLine("  circuit { use root = M0() }\n}");
        var tooDeep = FlagshipNetlist.Elaborate(FlagshipNetlist.Parse(sb.ToString()).Document!);
        Assert.False(tooDeep.Succeeded);
        Assert.Contains(tooDeep.Diagnostics, d => d.Code == "hierarchy_depth_exceeded");
    }

    // ==================== 5. POLYGLOT SVG EDGE CASES ====================

    [Fact]
    public void PolyglotSvg_HandlesCdataEscapeSequences_NoDefsSvg_AndFailClosedErrors()
    {
        const string nl = """
            netlist "cdata_test" format="spice.netlist.v3" {
              circuit {
                part V1 "spice.vsrc" dc_volts=5V
                part R1 "spice.resistor" ohms=1k
                net in { V1.p; R1.p }
                net GND ground=true { V1.n; R1.n }
                directive "* tricky ]]> marker inside directive"
              }
            }
            """;
        var doc = FlagshipNetlist.Parse(nl).Document!;

        // SVG without <defs> tag + CDATA end sequence `]]>` inside directive
        var bareSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"10\" height=\"10\"/></svg>";
        var poly = FlagshipNetlist.EmbedIntoSvg(bareSvg, doc);
        Assert.Contains("]]]]><![CDATA[>", poly, StringComparison.Ordinal);

        var extracted = FlagshipNetlist.ExtractFromPolyglotSvg(poly, verifyHashes: true);
        Assert.True(extracted.Succeeded, string.Join("; ", extracted.Diagnostics.Select(d => d.Message)));
        Assert.Equal(doc.ElectricalSha256, extracted.Document!.ElectricalSha256);
        Assert.Contains("* tricky ]]> marker inside directive", extracted.Document.Circuit.Directives);

        // Error paths
        Assert.Contains(FlagshipNetlist.ExtractFromPolyglotSvg("").Diagnostics, d => d.Code == "empty_svg");
        Assert.Contains(FlagshipNetlist.ExtractFromPolyglotSvg("<svg></svg>").Diagnostics, d => d.Code == "missing_embedded_netlist");
        Assert.Contains(FlagshipNetlist.ExtractFromPolyglotSvg("<svg><spice-netlist format=\"spice.netlist.v3\"><![CDATA[netlist \"a\" {}</svg>").Diagnostics,
            d => d.Code == "unterminated_embedded_netlist");
    }

    // ==================== 6. VELOSIM INGESTION & CIRCUIT JSON EXHAUSTIVE COVERAGE ====================

    [Theory]
    [InlineData("spice.vsrc", null, null, "Supply", "simple_power_source")]
    [InlineData("spice.isrc", null, null, "Current", "simple_current_source")]
    [InlineData("spice.resistor", null, null, "Resistor", "simple_resistor")]
    [InlineData("spice.capacitor", null, null, "Capacitor", "simple_capacitor")]
    [InlineData("spice.inductor", null, null, "Inductor", "simple_inductor")]
    [InlineData("spice.core", null, null, "SaturableInductor", "simple_inductor")]
    [InlineData("spice.diode", null, null, "Diode", "simple_diode")]
    [InlineData("spice.bjt", "QN", ".MODEL QN NPN(BF=100)", "Npn", "simple_transistor")]
    [InlineData("spice.bjt", "QP", ".MODEL QP PNP(BF=100)", "Pnp", "simple_transistor")]
    [InlineData("spice.mosfet", "MN", ".MODEL MN NMOS(LEVEL=54 VTH0=0.4)", "Nmos", "simple_mosfet")]
    [InlineData("spice.mosfet", "MP", ".MODEL MP PMOS(LEVEL=54 VTH0=-0.4)", "Pmos", "simple_mosfet")]
    [InlineData("spice.vdmos", "VN", ".MODEL VN VDMOS(VTO=3 KP=10)", "Nvdmos", "simple_mosfet")]
    [InlineData("spice.vdmos", "VP", ".MODEL VP VDMOS(PCHAN VTO=-3 KP=10)", "Pvdmos", "simple_mosfet")]
    [InlineData("spice.jfet", "JN", ".MODEL JN NJF(VTO=-2 BETA=1m)", "Njfet", "simple_transistor")]
    [InlineData("spice.jfet", "JP", ".MODEL JP PJF(VTO=-2 BETA=1m)", "Pjfet", "simple_transistor")]
    [InlineData("spice.mesfet", "ZN", ".MODEL ZN NMF(VTO=-1.5 BETA=0.05)", "Nmesfet", "simple_transistor")]
    [InlineData("spice.mesfet", "ZP", ".MODEL ZP PMF(VTO=-1.5 BETA=0.05)", "Pmesfet", "simple_transistor")]
    [InlineData("spice.vcvs", null, null, "Vcvs", "simple_chip")]
    [InlineData("spice.vccs", null, null, "Vccs", "simple_chip")]
    [InlineData("spice.switch", null, null, "Switch", "simple_switch")]
    [InlineData("spice.cswitch", null, null, "CurrentSwitch", "simple_switch")]
    [InlineData("spice.subckt", "SUB1", ".SUBCKT SUB1 a b\n R1 a b 1k\n.ENDS", "CellInstance", "simple_chip")]
    [InlineData("spice.osdi", "OSDI1", ".MODEL OSDI1 D(IS=1e-14)", "CellInstance", "simple_chip")]
    public void VeloSimAndCircuitJson_MapEveryDeviceKindCorrectly(
        string kindId, string? modelName, string? modelText, string expectedVeloKind, string expectedCircuitFtype)
    {
        var models = new List<NetlistModelDef>();
        if (modelName is not null && modelText is not null)
        {
            models.Add(new NetlistModelDef(
                kindId == "spice.subckt" ? NetlistModelKind.Subcircuit : NetlistModelKind.SpiceModelCard,
                modelName,
                modelText));
        }

        var circuit = new FlagshipModuleDef(
            "__circuit__",
            new Dictionary<string, double>(),
            [],
            [new FlagshipPartDef("U1", kindId, new Dictionary<string, double>(), ModelName: modelName, Footprint: "PKG1")],
            [],
            [
                new FlagshipNetDef("a", [new NetlistPinRef("U1", "p")]),
                new FlagshipNetDef("GND", [new NetlistPinRef("U1", "n")], IsGround: true)
            ],
            [],
            []);
        var doc = FlagshipNetlist.WithHashes(new FlagshipNetlistDocument("map_test", circuit, [], [], models));

        var veloJson = VeloSimIngestion.ExportVeloSimNetlistJson(doc);
        using var vd = JsonDocument.Parse(veloJson);
        Assert.Equal(expectedVeloKind, vd.RootElement.GetProperty("analog").GetProperty("elements")[0].GetProperty("velosimKind").GetString());

        var cjJson = VeloSimIngestion.ExportCircuitJson(doc);
        using var cj = JsonDocument.Parse(cjJson);
        var srcComp = cj.RootElement.EnumerateArray().Single(x => x.GetProperty("type").GetString() == "source_component");
        Assert.Equal(expectedCircuitFtype, srcComp.GetProperty("ftype").GetString());
    }

    [Fact]
    public void VeloSimIngestion_PreservesRawRtlJsonAndRejectsMalformedOversizedAndWrongFormatPackages()
    {
        const string rtlWithBody = """"
            netlist "rtl_pass" format="spice.netlist.v3" {
              rtl "fifo" format="velosim.rtl.v1" clock="clk" {
                port clk:in[1]
                port wr_data:input[16]
                port rd_data:output[16]
                port debug_bus:inout[99]
                rtl_json """
                  {
                    "memories": [{"name": "mem", "width": 16, "depth": 8, "lowerBound": 0}],
                    "continuous": [],
                    "body": {"kind": "block", "items": [{"kind": "nop"}]}
                  }
                """
              }
              circuit {
                part V1 "spice.vsrc" dc_volts=3.3V
                part R1 "spice.resistor" ohms=10k
                net vcc { V1.p; R1.p }
                net GND ground=true { V1.n; R1.n }
              }
            }
            """";

        var doc = FlagshipNetlist.Parse(rtlWithBody).Document!;
        var json = VeloSimIngestion.ExportVeloSimNetlistJson(doc);
        using var parsed = JsonDocument.Parse(json);
        var dig = parsed.RootElement.GetProperty("digital")[0];
        Assert.Equal(1, dig.GetProperty("memories").GetArrayLength());
        Assert.Equal("mem", dig.GetProperty("memories")[0].GetProperty("name").GetString());
        Assert.Equal("nop", dig.GetProperty("body").GetProperty("items")[0].GetProperty("kind").GetString());

        // Direction normalization & 64-bit width clamping (matching VeloSim 64-bit word limit)
        var sigs = dig.GetProperty("signals").EnumerateArray().ToList();
        Assert.Equal("input", sigs.Single(s => s.GetProperty("name").GetString() == "clk").GetProperty("direction").GetString());
        Assert.Equal("internal", sigs.Single(s => s.GetProperty("name").GetString() == "debug_bus").GetProperty("direction").GetString());
        Assert.Equal(64, sigs.Single(s => s.GetProperty("name").GetString() == "debug_bus").GetProperty("width").GetInt32());

        // Fail-closed import checks
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson("").Diagnostics, d => d.Code == "empty_velosim_json");
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson("{ bad json").Diagnostics, d => d.Code == "malformed_velosim_json");
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson("{\"format\":\"wrong.v9\"}").Diagnostics, d => d.Code == "unsupported_velosim_format");
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson("{\"format\":\"velosim.netlist.v1\"}").Diagnostics, d => d.Code == "missing_analog_domain");

        var oversized = "{\"format\":\"velosim.netlist.v1\",\"pad\":\"" + new string('x', VeloSimIngestion.MaxIngestionBytes + 32) + "\"}";
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson(oversized).Diagnostics, d => d.Code == "velosim_json_too_large");

        // Excessive JSON nesting (> MaxJsonDepth = 64) rejected cleanly
        var deepJson = new string('[', 80) + "0" + new string(']', 80);
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson(deepJson).Diagnostics, d => d.Code == "malformed_velosim_json");
    }

    // ==================== 7. SEEDED PROPERTY & RANDOMIZED TESTS ====================

    [Fact]
    public void SeededPropertyTest_50RandomHierarchicalCircuits_FormatIdempotence_HashInvariance_AndVeloSimRoundTrip()
    {
        const int seed = 20261125;
        var rng = new Random(seed);

        for (int trial = 0; trial < 50; ++trial)
        {
            int stages = rng.Next(1, 5);
            double vSup = Math.Round(1.0 + rng.NextDouble() * 23.0, 2);
            var sb = new StringBuilder();
            sb.AppendLine($"netlist \"prop_case_{trial}\" format=\"spice.netlist.v3\" {{");
            sb.AppendLine("  module Cell(r_val=1k, c_val=100nF) {");
            sb.AppendLine("    ports { in:in, out:out, gnd:power }");
            sb.AppendLine("    part R1 \"spice.resistor\" ohms=$r_val footprint=\"0603\"");
            sb.AppendLine("    part C1 \"spice.capacitor\" farads=$c_val footprint=\"0603\"");
            sb.AppendLine("    chain in -> R1 -> out -> C1 -> gnd");
            sb.AppendLine("  }");
            sb.AppendLine("  circuit {");
            sb.AppendLine($"    part V1 \"spice.vsrc\" dc_volts={vSup.ToString(System.Globalization.CultureInfo.InvariantCulture)}V");

            for (int s = 0; s < stages; ++s)
            {
                double rVal = Math.Round(100.0 * Math.Pow(10.0, rng.NextDouble() * 3.0), 1);
                double cNf = Math.Round(1.0 + rng.NextDouble() * 999.0, 1);
                sb.AppendLine($"    use st{s} = Cell(r_val={rVal.ToString(System.Globalization.CultureInfo.InvariantCulture)}, c_val={cNf.ToString(System.Globalization.CultureInfo.InvariantCulture)}nF)");
            }

            sb.AppendLine("    net n0 { V1.p; st0.in }");
            for (int s = 0; s < stages - 1; ++s)
            {
                sb.AppendLine($"    net n{s + 1} {{ st{s}.out; st{s + 1}.in }}");
            }
            sb.AppendLine($"    net out {{ st{stages - 1}.out }}");
            var gndPins = new List<string> { "V1.n" };
            for (int s = 0; s < stages; ++s) gndPins.Add($"st{s}.gnd");
            sb.AppendLine($"    net GND ground=true {{ {string.Join("; ", gndPins)} }}");
            sb.AppendLine("  }");
            sb.AppendLine("}");

            var parsed1 = FlagshipNetlist.Parse(sb.ToString());
            Assert.True(parsed1.Succeeded, $"Trial {trial} (seed {seed}) failed parse.");

            // Property 1: Format -> Parse(verifyHashes:true) -> Format is strictly idempotent
            var fmt1 = FlagshipNetlist.Format(parsed1.Document!);
            var parsed2 = FlagshipNetlist.Parse(fmt1, verifyHashes: true);
            Assert.True(parsed2.Succeeded, $"Trial {trial} failed hash-verified re-parse.");
            var fmt2 = FlagshipNetlist.Format(parsed2.Document!);
            Assert.Equal(fmt1, fmt2);

            // Property 2: Random layout shifts NEVER change electrical_sha256
            var elab = FlagshipNetlist.Elaborate(parsed1.Document!);
            Assert.True(elab.Succeeded);
            Assert.Equal(1 + 2 * stages, elab.Parts.Count);
            var randLayout = new FlagshipLayout(20.0, "lines", elab.Parts.Select(p =>
                new FlagshipPlacement(p.Id, rng.Next(0, 20) * 20.0, rng.Next(0, 20) * 20.0, rng.Next(0, 4) * 90)).ToList());
            var shifted = FlagshipNetlist.WithHashes(parsed1.Document! with { Layout = randLayout });
            Assert.Equal(parsed1.Document!.ElectricalSha256, shifted.ElectricalSha256);

            // Property 3: VeloSim ingestion export -> re-import preserves exact part & net counts
            var velo = VeloSimIngestion.ExportVeloSimNetlistJson(parsed1.Document!);
            var reimp = VeloSimIngestion.ImportVeloSimNetlistJson(velo, verifyModelHashes: true);
            Assert.True(reimp.Succeeded, $"Trial {trial} VeloSim re-import failed.");
            Assert.Equal(elab.Parts.Count, reimp.Document!.Circuit.Parts.Count);
            Assert.Equal(elab.Nets.Count, reimp.Document.Circuit.Nets.Count);
        }
    }

    // ==================== 8. SEEDED CRASH-FREEDOM FUZZ SUITE ====================

    [Fact]
    public void SeededFuzzTest_300MutatedAndTruncatedInputs_NeverThrowUnhandledExceptions()
    {
        const int seed = 20261125;
        var rng = new Random(seed);

        const string validSeedNetlist = """"
            netlist "fuzz_seed" format="spice.netlist.v3" {
              module M(r=1k) { ports { a:in, b:out } part R1 "spice.resistor" ohms=$r chain a -> R1 -> b }
              rtl "r" format="velosim.rtl.v1" clock="clk" { port clk:input[1] bridge b1 kind="dac" digital="clk" analog="in" }
              circuit { part V1 "spice.vsrc" dc_volts=5V use m1 = M() net in { V1.p; m1.a } net GND ground=true { V1.n; m1.b } }
              layout grid=20 style="lines" { place V1 at=(80, 140) rot=90 }
              pcb { board width=40mm height=30mm layers=2 rule net="GND" clearance=0.2mm }
              verify op { assert "v(in) == 5V" }
              model "D1" kind="SpiceModelCard" """
                .MODEL D1 D(IS=1e-14)
              """
            }
            """";

        var validVelo = VeloSimIngestion.ExportVeloSimNetlistJson(FlagshipNetlist.Parse(validSeedNetlist).Document!);
        var validPoly = FlagshipNetlist.EmbedIntoSvg("<svg><defs></defs></svg>", FlagshipNetlist.Parse(validSeedNetlist).Document!);
        const string alphabet = "abcXYZ0123{}()[]=;:,.->\"#/*$\\n\\t \r\n@!?";

        string Mutate(string src)
        {
            int mode = rng.Next(4);
            if (mode == 0 && src.Length > 1)
            {
                // Truncate at random point
                return src[..rng.Next(0, src.Length)];
            }
            if (mode == 1 && src.Length > 2)
            {
                // Corrupt 1..6 characters
                var chars = src.ToCharArray();
                int count = rng.Next(1, 7);
                for (int k = 0; k < count; ++k)
                {
                    chars[rng.Next(chars.Length)] = alphabet[rng.Next(alphabet.Length)];
                }
                return new string(chars);
            }
            if (mode == 2 && src.Length > 4)
            {
                // Delete a random slice
                int a = rng.Next(0, src.Length - 1);
                int len = rng.Next(1, Math.Min(40, src.Length - a));
                return src.Remove(a, len);
            }
            // Pure random garbage
            int n = rng.Next(0, 120);
            var sb = new StringBuilder(n);
            for (int k = 0; k < n; ++k) sb.Append(alphabet[rng.Next(alphabet.Length)]);
            return sb.ToString();
        }

        for (int i = 0; i < 100; ++i)
        {
            var mNet = Mutate(validSeedNetlist);
            var rNet = FlagshipNetlist.Parse(mNet, verifyHashes: (i & 1) == 0);
            if (rNet.Succeeded && rNet.Document is not null)
            {
                _ = FlagshipNetlist.Elaborate(rNet.Document);
            }

            var mPoly = Mutate(validPoly);
            _ = FlagshipNetlist.ExtractFromPolyglotSvg(mPoly, verifyHashes: true);

            var mVelo = Mutate(validVelo);
            _ = VeloSimIngestion.ImportVeloSimNetlistJson(mVelo, verifyModelHashes: true);
        }
    }

    [Fact]
    public void VeloSimIngestion_MinimizedFuzzRegression_NullCollectionsAndFieldsFailClosedOrDefaultSafely()
    {
        // Minimized from seed=20261125 fuzz failure where mutated JSON omitted "parameters",
        // producing null Dictionary<string,double> that previously crashed AppendCanonicalModule.
        const string nullParamsJson = """
            {
              "format": "velosim.netlist.v1",
              "designName": "min_fuzz",
              "analog": {
                "elements": [{ "id": "R1", "kindId": "spice.resistor", "parameters": null }]
              },
              "connectivity": null,
              "digital": null,
              "bridges": null,
              "pcb": { "rules": [null, { "netName": null, "clearanceMm": 0.2 }] }
            }
            """;
        var ok = VeloSimIngestion.ImportVeloSimNetlistJson(nullParamsJson);
        Assert.True(ok.Succeeded);
        Assert.NotNull(ok.Document);
        Assert.Single(ok.Document!.Circuit.Parts);

        // Missing elements array fails closed with clean diagnostic
        const string missingElements = """
            { "format": "velosim.netlist.v1", "designName": "no_el", "analog": { "elements": null } }
            """;
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson(missingElements).Diagnostics, d => d.Code == "missing_analog_elements");

        // Null/empty element id or kindId fails closed cleanly
        const string badElement = """
            { "format": "velosim.netlist.v1", "designName": "bad_el", "analog": { "elements": [{ "id": "", "kindId": null }] } }
            """;
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson(badElement).Diagnostics, d => d.Code == "malformed_velosim_element");

        // Malformed model entry with null spiceText fails closed cleanly
        const string badModel = """
            { "format": "velosim.netlist.v1", "designName": "bad_m", "analog": { "models": [{ "name": "M1", "spiceText": null }], "elements": [] } }
            """;
        Assert.Contains(VeloSimIngestion.ImportVeloSimNetlistJson(badModel).Diagnostics, d => d.Code == "malformed_velosim_model");
    }
}
