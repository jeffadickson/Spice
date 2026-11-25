using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cameo.Netlist;

/// <summary>
/// Diagnostic emitted during parsing, hash verification, or hierarchical elaboration
/// of a Flagship Hierarchical Netlist (.spicenet, format spice.netlist.v3).
/// </summary>
public sealed record FlagshipDiagnostic(
    string Severity,
    string Code,
    string Message,
    int? LineNumber = null,
    string? SubjectId = null);

/// <summary>
/// Electrical pin endpoint `(InstanceId, PinName)` independent of schematic geometry.
/// </summary>
public sealed record NetlistPinRef(string InstanceId, string PinName);

public enum NetlistModelKind
{
    SpiceModelCard,
    Subcircuit,
    Osdi
}

/// <summary>
/// Standalone pinned SPICE .MODEL, .SUBCKT, or .OSDI definition carried in a netlist.
/// </summary>
public sealed record NetlistModelDef(
    NetlistModelKind Kind,
    string ModelName,
    string SpiceText,
    string? OsdiLibraryPath = null,
    string? Description = null);

/// <summary>
/// Optional callback payload returned by an external component library (such as Spice.Schematic)
/// to resolve default parameters and pinned models for a `lib="..."` part reference.
/// </summary>
public sealed record NetlistPartDefaults(
    IReadOnlyDictionary<string, double> Parameters,
    NetlistModelDef? Model = null);

public sealed record FlagshipPortDef(
    string Name,
    string Direction = "inout",
    int Width = 1);

public sealed record FlagshipPartDef(
    string Id,
    string KindId,
    IReadOnlyDictionary<string, double> Parameters,
    IReadOnlyDictionary<string, string>? ParameterExpressions = null,
    string? ModelName = null,
    string? LibraryPartId = null,
    string? ControlSourceId = null,
    string? Footprint = null,
    string? Mpn = null,
    string? SupplierPartNumber = null,
    string? DisplayValue = null);

public sealed record FlagshipUseDef(
    string InstanceId,
    string ModuleName,
    IReadOnlyDictionary<string, double> Arguments,
    IReadOnlyDictionary<string, string>? ArgumentExpressions = null);

public sealed record FlagshipNetDef(
    string Name,
    IReadOnlyList<NetlistPinRef> Pins,
    bool IsGround = false);

/// <summary>
/// Signal-flow chain statement (e.g. `chain in -> R1 -> out -> C1 -> gnd`)
/// expanded deterministically into electrical nets during elaboration.
/// </summary>
public sealed record FlagshipChainDef(IReadOnlyList<string> Steps);

/// <summary>
/// Reusable hierarchical circuit module with typed default parameters, named ports,
/// internal parts, nested module instantiations, explicit nets, and signal-flow chains.
/// </summary>
public sealed record FlagshipModuleDef(
    string Name,
    IReadOnlyDictionary<string, double> DefaultParameters,
    IReadOnlyList<FlagshipPortDef> Ports,
    IReadOnlyList<FlagshipPartDef> Parts,
    IReadOnlyList<FlagshipUseDef> Uses,
    IReadOnlyList<FlagshipNetDef> Nets,
    IReadOnlyList<FlagshipChainDef> Chains,
    IReadOnlyList<string> Directives);

/// <summary>
/// Mixed-signal ADC/DAC boundary bridge connecting an analog net to a digital RTL port
/// for VeloSim co-simulation ingestion.
/// </summary>
public sealed record FlagshipMixedBridge(
    string Name,
    string Kind,
    string DigitalSignal,
    string AnalogNet,
    double VLowVolts = 0.0,
    double VHighVolts = 3.3,
    double VThresholdVolts = 1.65,
    double TRiseSeconds = 5e-9,
    double TFallSeconds = 5e-9);

/// <summary>
/// Digital RTL module interface and optional structural velosim.rtl.v1 payload
/// embedded cleanly alongside analog topology for VeloSim ingestion.
/// </summary>
public sealed record FlagshipRtlBlock(
    string Name,
    string Format,
    string? Clock,
    IReadOnlyList<FlagshipPortDef> Ports,
    IReadOnlyList<FlagshipMixedBridge> Bridges,
    string? RawRtlJson = null);

public sealed record FlagshipPlacement(
    string InstanceId,
    double X,
    double Y,
    int RotationDegrees = 0,
    bool MirrorX = false,
    bool MirrorY = false);

public sealed record FlagshipLayout(
    double GridPitch,
    string GridStyle,
    IReadOnlyList<FlagshipPlacement> Placements);

public sealed record FlagshipPcbRule(
    string NetName,
    double? ClearanceMm = null,
    double? TraceWidthMm = null);

public sealed record FlagshipPcbSection(
    double? BoardWidthMm = null,
    double? BoardHeightMm = null,
    int? LayerCount = null,
    IReadOnlyList<FlagshipPcbRule>? Rules = null);

public sealed record FlagshipVerifyBlock(
    string Analysis,
    IReadOnlyList<string> Expressions);

/// <summary>
/// Topology-first, Git-diffable, hierarchical circuit and schematic document
/// (format: spice.netlist.v3 / .spicenet) separating electrical truth from visual
/// layout and carrying independent electrical_sha256 and layout_sha256 integrity hashes.
/// </summary>
public sealed record FlagshipNetlistDocument(
    string Name,
    FlagshipModuleDef Circuit,
    IReadOnlyList<FlagshipModuleDef> Modules,
    IReadOnlyList<FlagshipRtlBlock> RtlBlocks,
    IReadOnlyList<NetlistModelDef> Models,
    FlagshipLayout? Layout = null,
    FlagshipPcbSection? Pcb = null,
    IReadOnlyList<FlagshipVerifyBlock>? Verifications = null,
    string FormatId = FlagshipNetlist.FormatId,
    string SchemaVersion = FlagshipNetlist.CurrentSchemaVersion,
    string? ElectricalSha256 = null,
    string? LayoutSha256 = null);

public sealed record FlagshipParseResult(
    bool Succeeded,
    FlagshipNetlistDocument? Document,
    IReadOnlyList<FlagshipDiagnostic> Diagnostics);

public sealed record ElaboratedNetlist(
    string Name,
    IReadOnlyList<FlagshipPartDef> Parts,
    IReadOnlyList<FlagshipNetDef> Nets,
    IReadOnlyList<FlagshipPortDef> Ports,
    IReadOnlyList<string> Directives,
    IReadOnlyList<NetlistModelDef> Models,
    IReadOnlyList<FlagshipRtlBlock> RtlBlocks,
    IReadOnlyList<FlagshipMixedBridge> Bridges,
    IReadOnlyList<FlagshipDiagnostic> Diagnostics)
{
    public bool Succeeded => !Diagnostics.Any(d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Standalone, zero-dependency parser, canonical formatter, dual-SHA256 hasher,
/// hierarchical elaborator, signal-flow auto-placer, and polyglot .spice.svg metadata
/// codec for the Flagship Hierarchical Netlist format (spice.netlist.v3 / .spicenet).
/// </summary>
public static class FlagshipNetlist
{
    public const string FormatId = "spice.netlist.v3";
    public const string CurrentSchemaVersion = "3.0";
    public const int MaxInputBytes = 4 * 1024 * 1024;
    public const int MaxHierarchyDepth = 16;

    private const string PolyglotStartMarker = "<spice-netlist format=\"spice.netlist.v3\"><![CDATA[";
    private const string PolyglotEndMarker = "]]></spice-netlist>";

    // ==================== DUAL CRYPTOGRAPHIC HASHING ====================

    public static FlagshipNetlistDocument WithHashes(FlagshipNetlistDocument doc)
    {
        var elecHash = ComputeElectricalSha256(doc);
        var layHash = ComputeLayoutSha256(doc);
        return doc with
        {
            FormatId = FormatId,
            SchemaVersion = CurrentSchemaVersion,
            ElectricalSha256 = elecHash,
            LayoutSha256 = layHash
        };
    }

    /// <summary>
    /// Computes SHA-256 strictly over electrical truth (modules, RTL interfaces, circuit
    /// topology/parameters/models). Visual layout coordinates, PCB rules, and comments are
    /// deliberately excluded so visual tidy-ups never change electrical_sha256.
    /// </summary>
    public static string ComputeElectricalSha256(FlagshipNetlistDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append("spice.netlist.v3|elec|").AppendLine(doc.Name);
        foreach (var m in doc.Modules.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            AppendCanonicalModule(sb, m);
        }
        foreach (var r in doc.RtlBlocks.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            sb.Append("rtl|").Append(r.Name).Append('|').Append(r.Format).Append('|').AppendLine(r.Clock ?? "");
            foreach (var p in r.Ports.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                sb.Append("rport|").Append(p.Name).Append('|').Append(p.Direction).Append('|').AppendLine(p.Width.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var b in r.Bridges.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                sb.Append("bridge|").Append(b.Name).Append('|').Append(b.Kind).Append('|').Append(b.DigitalSignal).Append('|').Append(b.AnalogNet)
                  .Append('|').Append(FmtCanon(b.VLowVolts)).Append('|').Append(FmtCanon(b.VHighVolts))
                  .Append('|').Append(FmtCanon(b.VThresholdVolts)).Append('|').Append(FmtCanon(b.TRiseSeconds))
                  .Append('|').AppendLine(FmtCanon(b.TFallSeconds));
            }
            if (!string.IsNullOrWhiteSpace(r.RawRtlJson))
            {
                sb.Append("rtljson|").AppendLine(r.RawRtlJson.Trim());
            }
        }
        AppendCanonicalModule(sb, doc.Circuit);
        foreach (var mdl in doc.Models.OrderBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("model|").Append(mdl.ModelName).Append('|').Append(mdl.Kind).Append('|')
              .Append(mdl.OsdiLibraryPath ?? "").Append('|')
              .AppendLine(NormalizeNewlines(mdl.SpiceText).Trim());
        }
        return Sha256Hex(sb.ToString());
    }

    /// <summary>
    /// Computes SHA-256 strictly over visual layout placements and optional PCB rules.
    /// </summary>
    public static string ComputeLayoutSha256(FlagshipNetlistDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append("spice.netlist.v3|layout|");
        if (doc.Layout is not null)
        {
            sb.Append(FmtCanon(doc.Layout.GridPitch)).Append('|').AppendLine(doc.Layout.GridStyle);
            foreach (var p in doc.Layout.Placements.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
            {
                sb.Append("place|").Append(p.InstanceId).Append('|')
                  .Append(FmtCanon(p.X)).Append(',').Append(FmtCanon(p.Y)).Append('|')
                  .Append(p.RotationDegrees.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(p.MirrorX ? '1' : '0').AppendLine(p.MirrorY ? "1" : "0");
            }
        }
        if (doc.Pcb is not null)
        {
            sb.Append("pcb|").Append(doc.Pcb.BoardWidthMm.HasValue ? FmtCanon(doc.Pcb.BoardWidthMm.Value) : "")
              .Append('|').Append(doc.Pcb.BoardHeightMm.HasValue ? FmtCanon(doc.Pcb.BoardHeightMm.Value) : "")
              .Append('|').AppendLine(doc.Pcb.LayerCount?.ToString(CultureInfo.InvariantCulture) ?? "");
            if (doc.Pcb.Rules is not null)
            {
                foreach (var rule in doc.Pcb.Rules.OrderBy(x => x.NetName, StringComparer.Ordinal))
                {
                    sb.Append("rule|").Append(rule.NetName).Append('|')
                      .Append(rule.ClearanceMm.HasValue ? FmtCanon(rule.ClearanceMm.Value) : "").Append('|')
                      .AppendLine(rule.TraceWidthMm.HasValue ? FmtCanon(rule.TraceWidthMm.Value) : "");
                }
            }
        }
        return Sha256Hex(sb.ToString());
    }

    private static void AppendCanonicalModule(StringBuilder sb, FlagshipModuleDef m)
    {
        sb.Append("mod|").AppendLine(m.Name);
        foreach (var kv in m.DefaultParameters.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append("param|").Append(kv.Key).Append('=').AppendLine(FmtCanon(kv.Value));
        }
        foreach (var p in m.Ports.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            sb.Append("port|").Append(p.Name).Append('|').Append(p.Direction).Append('|').AppendLine(p.Width.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var part in m.Parts.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            sb.Append("part|").Append(part.Id).Append('|').Append(part.KindId)
              .Append('|').Append(part.ModelName ?? "").Append('|').Append(part.LibraryPartId ?? "")
              .Append('|').Append(part.ControlSourceId ?? "")
              .Append('|').Append(part.Footprint ?? "").Append('|').Append(part.Mpn ?? "")
              .Append('|').AppendLine(part.SupplierPartNumber ?? "");
            foreach (var kv in part.Parameters.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                sb.Append("  p|").Append(kv.Key).Append('=').AppendLine(FmtCanon(kv.Value));
            }
            if (part.ParameterExpressions is not null)
            {
                foreach (var kv in part.ParameterExpressions.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.Append("  pe|").Append(kv.Key).Append('=').AppendLine(kv.Value);
                }
            }
        }
        foreach (var u in m.Uses.OrderBy(x => x.InstanceId, StringComparer.Ordinal))
        {
            sb.Append("use|").Append(u.InstanceId).Append('=').AppendLine(u.ModuleName);
            foreach (var kv in u.Arguments.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                sb.Append("  a|").Append(kv.Key).Append('=').AppendLine(FmtCanon(kv.Value));
            }
            if (u.ArgumentExpressions is not null)
            {
                foreach (var kv in u.ArgumentExpressions.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.Append("  ae|").Append(kv.Key).Append('=').AppendLine(kv.Value);
                }
            }
        }
        foreach (var net in m.Nets.OrderBy(x => x.IsGround ? "" : x.Name, StringComparer.Ordinal))
        {
            var pins = net.Pins
                .OrderBy(p => p.InstanceId, StringComparer.Ordinal)
                .ThenBy(p => p.PinName, StringComparer.Ordinal)
                .Select(p => $"{p.InstanceId}.{p.PinName}");
            sb.Append("net|").Append(net.Name).Append('|').Append(net.IsGround ? '1' : '0').Append('|')
              .AppendLine(string.Join(",", pins));
        }
        foreach (var ch in m.Chains)
        {
            sb.Append("chain|").AppendLine(string.Join("->", ch.Steps));
        }
        foreach (var d in m.Directives)
        {
            sb.Append("dir|").AppendLine(d.Trim());
        }
    }

    // ==================== CANONICAL FORMATTER (.spicenet) ====================

    public static string Format(FlagshipNetlistDocument document)
    {
        var doc = WithHashes(document);
        var sb = new StringBuilder();
        sb.AppendLine("// Cameo / Spice V2 Flagship Hierarchical Netlist (spice.netlist.v3)");
        sb.AppendLine($"netlist {Quote(doc.Name)} format={Quote(FormatId)} schema={Quote(CurrentSchemaVersion)} electrical_sha256={Quote(doc.ElectricalSha256!)} layout_sha256={Quote(doc.LayoutSha256!)} {{");

        foreach (var mod in doc.Modules.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            sb.AppendLine();
            FormatModule(sb, mod, isTopCircuit: false);
        }

        foreach (var rtl in doc.RtlBlocks.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            sb.AppendLine();
            var clkAttr = string.IsNullOrWhiteSpace(rtl.Clock) ? "" : $" clock={Quote(rtl.Clock)}";
            sb.AppendLine($"  rtl {Quote(rtl.Name)} format={Quote(rtl.Format)}{clkAttr} {{");
            foreach (var p in rtl.Ports)
            {
                sb.AppendLine($"    port {p.Name}:{p.Direction}[{p.Width}]");
            }
            foreach (var b in rtl.Bridges)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"    bridge {b.Name} kind={Quote(b.Kind)} digital={Quote(b.DigitalSignal)} analog={Quote(b.AnalogNet)} vlow={FmtUnit(b.VLowVolts, "V")} vhigh={FmtUnit(b.VHighVolts, "V")} vth={FmtUnit(b.VThresholdVolts, "V")} trise={FmtUnit(b.TRiseSeconds, "s")} tfall={FmtUnit(b.TFallSeconds, "s")}"));
            }
            if (!string.IsNullOrWhiteSpace(rtl.RawRtlJson))
            {
                sb.AppendLine("    rtl_json \"\"\"");
                foreach (var line in NormalizeNewlines(rtl.RawRtlJson).Trim().Split('\n'))
                {
                    sb.Append("      ").AppendLine(line);
                }
                sb.AppendLine("    \"\"\"");
            }
            sb.AppendLine("  }");
        }

        sb.AppendLine();
        FormatModule(sb, doc.Circuit, isTopCircuit: true);

        if (doc.Layout is not null && doc.Layout.Placements.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(FormattableString.Invariant(
                $"  layout grid={FmtCanon(doc.Layout.GridPitch)} style={Quote(doc.Layout.GridStyle)} {{"));
            foreach (var pl in doc.Layout.Placements.OrderBy(p => p.InstanceId, StringComparer.Ordinal))
            {
                var mx = pl.MirrorX ? " mirror_x=true" : "";
                var my = pl.MirrorY ? " mirror_y=true" : "";
                sb.AppendLine(FormattableString.Invariant(
                    $"    place {pl.InstanceId} at=({FmtCanon(pl.X)}, {FmtCanon(pl.Y)}) rot={pl.RotationDegrees}{mx}{my}"));
            }
            sb.AppendLine("  }");
        }

        if (doc.Pcb is not null)
        {
            sb.AppendLine();
            sb.AppendLine("  pcb {");
            if (doc.Pcb.BoardWidthMm.HasValue || doc.Pcb.BoardHeightMm.HasValue || doc.Pcb.LayerCount.HasValue)
            {
                var parts = new List<string>();
                if (doc.Pcb.BoardWidthMm.HasValue) parts.Add($"width={FmtCanon(doc.Pcb.BoardWidthMm.Value)}mm");
                if (doc.Pcb.BoardHeightMm.HasValue) parts.Add($"height={FmtCanon(doc.Pcb.BoardHeightMm.Value)}mm");
                if (doc.Pcb.LayerCount.HasValue) parts.Add($"layers={doc.Pcb.LayerCount.Value}");
                sb.AppendLine("    board " + string.Join(" ", parts));
            }
            if (doc.Pcb.Rules is not null)
            {
                foreach (var r in doc.Pcb.Rules.OrderBy(x => x.NetName, StringComparer.Ordinal))
                {
                    var attrs = new List<string> { $"net={Quote(r.NetName)}" };
                    if (r.ClearanceMm.HasValue) attrs.Add($"clearance={FmtCanon(r.ClearanceMm.Value)}mm");
                    if (r.TraceWidthMm.HasValue) attrs.Add($"width={FmtCanon(r.TraceWidthMm.Value)}mm");
                    sb.AppendLine("    rule " + string.Join(" ", attrs));
                }
            }
            sb.AppendLine("  }");
        }

        if (doc.Verifications is { Count: > 0 })
        {
            foreach (var v in doc.Verifications)
            {
                sb.AppendLine();
                sb.AppendLine($"  verify {v.Analysis} {{");
                foreach (var expr in v.Expressions)
                {
                    sb.AppendLine($"    assert {Quote(expr)}");
                }
                sb.AppendLine("  }");
            }
        }

        if (doc.Models.Count > 0)
        {
            foreach (var mdl in doc.Models.OrderBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                var osdiAttr = string.IsNullOrWhiteSpace(mdl.OsdiLibraryPath) ? "" : $" osdi={Quote(mdl.OsdiLibraryPath)}";
                sb.AppendLine($"  model {Quote(mdl.ModelName)} kind={Quote(mdl.Kind.ToString())}{osdiAttr} \"\"\"");
                foreach (var line in NormalizeNewlines(mdl.SpiceText).Trim().Split('\n'))
                {
                    sb.Append("    ").AppendLine(line.TrimEnd());
                }
                sb.AppendLine("  \"\"\"");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void FormatModule(StringBuilder sb, FlagshipModuleDef mod, bool isTopCircuit)
    {
        if (isTopCircuit)
        {
            sb.AppendLine("  circuit {");
        }
        else
        {
            var parms = mod.DefaultParameters.Count > 0
                ? "(" + string.Join(", ", mod.DefaultParameters.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={FmtValue(kv.Key, kv.Value)}")) + ")"
                : "";
            sb.AppendLine($"  module {mod.Name}{parms} {{");
        }

        if (mod.Ports.Count > 0)
        {
            var portItems = mod.Ports.Select(p => p.Width > 1 ? $"{p.Name}:{p.Direction}[{p.Width}]" : $"{p.Name}:{p.Direction}");
            sb.AppendLine("    ports { " + string.Join(", ", portItems) + " }");
        }

        foreach (var part in mod.Parts.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var tokens = new List<string> { "part", part.Id, Quote(part.KindId) };
            foreach (var kv in part.Parameters.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                tokens.Add($"{kv.Key}={FmtValue(kv.Key, kv.Value)}");
            }
            if (part.ParameterExpressions is not null)
            {
                foreach (var kv in part.ParameterExpressions.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    tokens.Add($"{kv.Key}={kv.Value}");
                }
            }
            if (!string.IsNullOrWhiteSpace(part.LibraryPartId)) tokens.Add($"lib={Quote(part.LibraryPartId)}");
            if (!string.IsNullOrWhiteSpace(part.ModelName)) tokens.Add($"model={Quote(part.ModelName)}");
            if (!string.IsNullOrWhiteSpace(part.ControlSourceId)) tokens.Add($"ctrl={Quote(part.ControlSourceId)}");
            if (!string.IsNullOrWhiteSpace(part.Footprint)) tokens.Add($"footprint={Quote(part.Footprint)}");
            if (!string.IsNullOrWhiteSpace(part.Mpn)) tokens.Add($"mpn={Quote(part.Mpn)}");
            if (!string.IsNullOrWhiteSpace(part.SupplierPartNumber)) tokens.Add($"lcsc={Quote(part.SupplierPartNumber)}");
            sb.AppendLine("    " + string.Join(" ", tokens));
        }

        foreach (var use in mod.Uses.OrderBy(u => u.InstanceId, StringComparer.Ordinal))
        {
            var args = new List<string>();
            foreach (var kv in use.Arguments.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                args.Add($"{kv.Key}={FmtValue(kv.Key, kv.Value)}");
            }
            if (use.ArgumentExpressions is not null)
            {
                foreach (var kv in use.ArgumentExpressions.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    args.Add($"{kv.Key}={kv.Value}");
                }
            }
            var argList = args.Count > 0 ? $"({string.Join(", ", args)})" : "()";
            sb.AppendLine($"    use {use.InstanceId} = {use.ModuleName}{argList}");
        }

        foreach (var ch in mod.Chains)
        {
            sb.AppendLine("    chain " + string.Join(" -> ", ch.Steps));
        }

        foreach (var net in mod.Nets.OrderBy(n => n.IsGround ? "" : n.Name, StringComparer.Ordinal))
        {
            var gndAttr = net.IsGround ? " ground=true" : "";
            var pins = net.Pins
                .OrderBy(p => p.InstanceId, StringComparer.Ordinal)
                .ThenBy(p => p.PinName, StringComparer.Ordinal)
                .Select(p => $"{p.InstanceId}.{p.PinName}");
            sb.AppendLine($"    net {net.Name}{gndAttr} {{ {string.Join("; ", pins)} }}");
        }

        foreach (var dir in mod.Directives)
        {
            sb.AppendLine($"    directive {Quote(dir)}");
        }

        sb.AppendLine("  }");
    }

    // ==================== PARSER (.spicenet) ====================

    private enum TokKind { Word, String, MultilineString, LBrace, RBrace, LParen, RParen, LBracket, RBracket, Eq, Colon, Comma, Semi, Arrow, Eof }
    private sealed record Token(TokKind Kind, string Text, int Line);

    public static FlagshipParseResult Parse(
        string text,
        Func<string, NetlistPartDefaults?>? libraryResolver = null,
        bool verifyHashes = false)
    {
        var diags = new List<FlagshipDiagnostic>();
        if (string.IsNullOrWhiteSpace(text))
        {
            diags.Add(new FlagshipDiagnostic("error", "empty_netlist", "Flagship netlist text is empty."));
            return new FlagshipParseResult(false, null, diags);
        }
        if (Encoding.UTF8.GetByteCount(text) > MaxInputBytes)
        {
            diags.Add(new FlagshipDiagnostic("error", "netlist_too_large", $"Netlist exceeds maximum bounded size of {MaxInputBytes} bytes."));
            return new FlagshipParseResult(false, null, diags);
        }

        var tokens = Tokenize(text, diags);
        if (diags.Any(d => d.Severity == "error"))
        {
            return new FlagshipParseResult(false, null, diags);
        }

        var parser = new ParserState(tokens, diags);
        var doc = parser.ParseRoot();
        if (doc is null || diags.Any(d => d.Severity == "error"))
        {
            return new FlagshipParseResult(false, null, diags);
        }

        if (verifyHashes)
        {
            var actualElec = ComputeElectricalSha256(doc);
            if (!string.IsNullOrWhiteSpace(doc.ElectricalSha256) &&
                !string.Equals(doc.ElectricalSha256, actualElec, StringComparison.OrdinalIgnoreCase))
            {
                diags.Add(new FlagshipDiagnostic(
                    "error",
                    "electrical_sha256_mismatch",
                    $"Electrical SHA-256 mismatch: expected '{doc.ElectricalSha256}', computed '{actualElec}'."));
                return new FlagshipParseResult(false, null, diags);
            }
            var actualLayout = ComputeLayoutSha256(doc);
            if (!string.IsNullOrWhiteSpace(doc.LayoutSha256) &&
                !string.Equals(doc.LayoutSha256, actualLayout, StringComparison.OrdinalIgnoreCase))
            {
                diags.Add(new FlagshipDiagnostic(
                    "error",
                    "layout_sha256_mismatch",
                    $"Layout SHA-256 mismatch: expected '{doc.LayoutSha256}', computed '{actualLayout}'."));
                return new FlagshipParseResult(false, null, diags);
            }
        }

        if (libraryResolver is not null)
        {
            doc = AttachMissingLibraryModels(doc, libraryResolver);
        }

        var stamped = WithHashes(doc);
        return new FlagshipParseResult(true, stamped, diags);
    }

    private static FlagshipNetlistDocument AttachMissingLibraryModels(
        FlagshipNetlistDocument doc,
        Func<string, NetlistPartDefaults?> libraryResolver)
    {
        var modelMap = doc.Models.ToDictionary(m => m.ModelName, StringComparer.OrdinalIgnoreCase);
        void ScanModule(FlagshipModuleDef m)
        {
            foreach (var p in m.Parts)
            {
                if (!string.IsNullOrWhiteSpace(p.LibraryPartId))
                {
                    var defaults = libraryResolver(p.LibraryPartId);
                    if (defaults?.Model is not null && !modelMap.ContainsKey(defaults.Model.ModelName))
                    {
                        modelMap[defaults.Model.ModelName] = defaults.Model;
                    }
                }
            }
        }
        foreach (var m in doc.Modules) ScanModule(m);
        ScanModule(doc.Circuit);
        return doc with { Models = modelMap.Values.OrderBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase).ToList() };
    }

    private static List<Token> Tokenize(string text, List<FlagshipDiagnostic> diags)
    {
        var list = new List<Token>();
        int i = 0, line = 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '#' || (c == '/' && i + 1 < text.Length && text[i + 1] == '/'))
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    if (text[i] == '\n') line++;
                    i++;
                }
                i = Math.Min(text.Length, i + 2);
                continue;
            }
            if (c == '-' && i + 1 < text.Length && text[i + 1] == '>')
            {
                list.Add(new Token(TokKind.Arrow, "->", line));
                i += 2;
                continue;
            }
            if (c == '"')
            {
                int startLine = line;
                if (i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
                {
                    i += 3;
                    var sb = new StringBuilder();
                    bool closed = false;
                    while (i < text.Length)
                    {
                        if (i + 2 < text.Length && text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"')
                        {
                            i += 3;
                            closed = true;
                            break;
                        }
                        if (text[i] == '\n') line++;
                        sb.Append(text[i++]);
                    }
                    if (!closed)
                    {
                        diags.Add(new FlagshipDiagnostic("error", "unterminated_multiline_string", "Unterminated raw multiline string '\"\"\"'.", startLine));
                        break;
                    }
                    list.Add(new Token(TokKind.MultilineString, Dedent(sb.ToString()), startLine));
                    continue;
                }
                else
                {
                    i++;
                    var sb = new StringBuilder();
                    bool closed = false;
                    while (i < text.Length)
                    {
                        char ch = text[i++];
                        if (ch == '\\' && i < text.Length)
                        {
                            char esc = text[i++];
                            sb.Append(esc switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\', _ => esc });
                        }
                        else if (ch == '"')
                        {
                            closed = true;
                            break;
                        }
                        else
                        {
                            if (ch == '\n') line++;
                            sb.Append(ch);
                        }
                    }
                    if (!closed)
                    {
                        diags.Add(new FlagshipDiagnostic("error", "unterminated_string", "Unterminated quoted string.", startLine));
                        break;
                    }
                    list.Add(new Token(TokKind.String, sb.ToString(), startLine));
                    continue;
                }
            }

            switch (c)
            {
                case '{': list.Add(new Token(TokKind.LBrace, "{", line)); i++; continue;
                case '}': list.Add(new Token(TokKind.RBrace, "}", line)); i++; continue;
                case '(': list.Add(new Token(TokKind.LParen, "(", line)); i++; continue;
                case ')': list.Add(new Token(TokKind.RParen, ")", line)); i++; continue;
                case '[': list.Add(new Token(TokKind.LBracket, "[", line)); i++; continue;
                case ']': list.Add(new Token(TokKind.RBracket, "]", line)); i++; continue;
                case '=': list.Add(new Token(TokKind.Eq, "=", line)); i++; continue;
                case ':': list.Add(new Token(TokKind.Colon, ":", line)); i++; continue;
                case ',': list.Add(new Token(TokKind.Comma, ",", line)); i++; continue;
                case ';': list.Add(new Token(TokKind.Semi, ";", line)); i++; continue;
            }

            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) &&
                   text[i] is not ('{' or '}' or '(' or ')' or '[' or ']' or '=' or ':' or ',' or ';' or '"' or '#') &&
                   !(text[i] == '-' && i + 1 < text.Length && text[i + 1] == '>') &&
                   !(text[i] == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*')))
            {
                i++;
            }
            if (i == start)
            {
                diags.Add(new FlagshipDiagnostic("error", "unexpected_char", $"Unexpected character '{text[i]}'.", line));
                i++;
                continue;
            }
            list.Add(new Token(TokKind.Word, text[start..i], line));
        }
        list.Add(new Token(TokKind.Eof, "", line));
        return list;
    }

    private sealed class ParserState(List<Token> tokens, List<FlagshipDiagnostic> diags)
    {
        private int _pos;
        private Token Cur => tokens[_pos];
        private Token Peek(int offset = 1) => _pos + offset < tokens.Count ? tokens[_pos + offset] : tokens[^1];
        private Token Advance() => tokens[_pos++];

        private bool Match(TokKind k)
        {
            if (Cur.Kind == k) { _pos++; return true; }
            return false;
        }

        private bool Expect(TokKind k, string msg)
        {
            if (Cur.Kind == k) { _pos++; return true; }
            diags.Add(new FlagshipDiagnostic("error", "syntax_error", $"{msg}, got '{Cur.Text}'", Cur.Line));
            return false;
        }

        public FlagshipNetlistDocument? ParseRoot()
        {
            if (Cur.Kind != TokKind.Word || !Cur.Text.Equals("netlist", StringComparison.OrdinalIgnoreCase))
            {
                diags.Add(new FlagshipDiagnostic("error", "missing_netlist_header", "Expected top-level 'netlist \"<name>\" { ... }' block.", Cur.Line));
                return null;
            }
            Advance();
            if (Cur.Kind is not (TokKind.String or TokKind.Word))
            {
                diags.Add(new FlagshipDiagnostic("error", "missing_netlist_name", "Expected netlist name.", Cur.Line));
                return null;
            }
            var name = Advance().Text;
            string formatId = FormatId;
            string schemaVer = CurrentSchemaVersion;
            string? elecSha = null;
            string? layoutSha = null;

            while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
            {
                var k = Advance().Text.ToLowerInvariant();
                Advance(); // '='
                var v = Advance().Text;
                switch (k)
                {
                    case "format": formatId = v; break;
                    case "schema": schemaVer = v; break;
                    case "electrical_sha256": elecSha = v; break;
                    case "layout_sha256": layoutSha = v; break;
                }
            }

            if (!string.Equals(formatId, FormatId, StringComparison.Ordinal))
            {
                diags.Add(new FlagshipDiagnostic("error", "unsupported_format", $"Unsupported netlist format '{formatId}'. Expected '{FormatId}'.", Cur.Line));
                return null;
            }

            if (!Expect(TokKind.LBrace, "Expected '{' after netlist header")) return null;

            var modules = new List<FlagshipModuleDef>();
            var moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rtlBlocks = new List<FlagshipRtlBlock>();
            var models = new List<NetlistModelDef>();
            var verifications = new List<FlagshipVerifyBlock>();
            FlagshipModuleDef? topCircuit = null;
            FlagshipLayout? layout = null;
            FlagshipPcbSection? pcb = null;

            while (Cur.Kind is not (TokKind.RBrace or TokKind.Eof))
            {
                if (Cur.Kind != TokKind.Word)
                {
                    diags.Add(new FlagshipDiagnostic("error", "unexpected_token", $"Unexpected token '{Cur.Text}' in netlist body.", Cur.Line));
                    Advance();
                    continue;
                }
                var kw = Cur.Text.ToLowerInvariant();
                switch (kw)
                {
                    case "module":
                    {
                        var m = ParseModuleBlock(isTopCircuit: false);
                        if (m is not null)
                        {
                            if (!moduleNames.Add(m.Name))
                                diags.Add(new FlagshipDiagnostic("error", "duplicate_module", $"Duplicate module '{m.Name}'.", Cur.Line, m.Name));
                            else
                                modules.Add(m);
                        }
                        break;
                    }
                    case "circuit":
                    {
                        if (topCircuit is not null)
                        {
                            diags.Add(new FlagshipDiagnostic("error", "duplicate_circuit", "Only one top-level 'circuit { ... }' block is allowed.", Cur.Line));
                        }
                        topCircuit = ParseModuleBlock(isTopCircuit: true);
                        break;
                    }
                    case "rtl":
                    {
                        var rtl = ParseRtlBlock();
                        if (rtl is not null) rtlBlocks.Add(rtl);
                        break;
                    }
                    case "layout":
                        layout = ParseLayoutBlock();
                        break;
                    case "pcb":
                        pcb = ParsePcbBlock();
                        break;
                    case "verify":
                    {
                        var v = ParseVerifyBlock();
                        if (v is not null) verifications.Add(v);
                        break;
                    }
                    case "model":
                    {
                        var mdl = ParseModelBlock();
                        if (mdl is not null) models.Add(mdl);
                        break;
                    }
                    default:
                        diags.Add(new FlagshipDiagnostic("error", "unknown_section", $"Unknown top-level section '{Cur.Text}'.", Cur.Line));
                        Advance();
                        break;
                }
            }

            Expect(TokKind.RBrace, "Expected closing '}' for netlist");

            if (topCircuit is null)
            {
                diags.Add(new FlagshipDiagnostic("error", "missing_circuit", "Netlist must contain a top-level 'circuit { ... }' block."));
                return null;
            }

            return new FlagshipNetlistDocument(
                Name: name,
                Circuit: topCircuit,
                Modules: modules,
                RtlBlocks: rtlBlocks,
                Models: models,
                Layout: layout,
                Pcb: pcb,
                Verifications: verifications.Count > 0 ? verifications : null,
                FormatId: formatId,
                SchemaVersion: schemaVer,
                ElectricalSha256: elecSha,
                LayoutSha256: layoutSha);
        }

        private FlagshipModuleDef? ParseModuleBlock(bool isTopCircuit)
        {
            Advance(); // 'module' or 'circuit'
            string modName = "__circuit__";
            var defaultParams = new Dictionary<string, double>(StringComparer.Ordinal);

            if (!isTopCircuit)
            {
                if (Cur.Kind is not (TokKind.Word or TokKind.String))
                {
                    diags.Add(new FlagshipDiagnostic("error", "missing_module_name", "Expected module name.", Cur.Line));
                    return null;
                }
                modName = Advance().Text;
                if (Match(TokKind.LParen))
                {
                    while (Cur.Kind is not (TokKind.RParen or TokKind.Eof))
                    {
                        var pk = Advance().Text;
                        double pv = 0.0;
                        if (Match(TokKind.Eq))
                        {
                            var rawVal = Advance().Text;
                            if (!TryParseQuantity(rawVal, out pv))
                                diags.Add(new FlagshipDiagnostic("error", "invalid_param_value", $"Invalid default parameter value '{rawVal}' for '{pk}'.", Cur.Line, modName));
                        }
                        defaultParams[pk] = pv;
                        Match(TokKind.Comma);
                    }
                    Expect(TokKind.RParen, "Expected ')' after module parameter list");
                }
            }

            if (!Expect(TokKind.LBrace, $"Expected '{{' for {(isTopCircuit ? "circuit" : "module")} body")) return null;

            var ports = new List<FlagshipPortDef>();
            var parts = new List<FlagshipPartDef>();
            var partIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uses = new List<FlagshipUseDef>();
            var nets = new List<FlagshipNetDef>();
            var chains = new List<FlagshipChainDef>();
            var directives = new List<string>();

            while (Cur.Kind is not (TokKind.RBrace or TokKind.Eof))
            {
                if (Cur.Kind != TokKind.Word)
                {
                    diags.Add(new FlagshipDiagnostic("error", "unexpected_token", $"Unexpected token '{Cur.Text}' in {modName}.", Cur.Line));
                    Advance();
                    continue;
                }
                var kw = Cur.Text.ToLowerInvariant();
                switch (kw)
                {
                    case "ports":
                        Advance();
                        if (Expect(TokKind.LBrace, "Expected '{' after 'ports'"))
                        {
                            while (Cur.Kind is not (TokKind.RBrace or TokKind.Eof))
                            {
                                ports.Add(ParsePortSpec());
                                Match(TokKind.Comma);
                                Match(TokKind.Semi);
                            }
                            Expect(TokKind.RBrace, "Expected '}' after ports");
                        }
                        break;
                    case "port":
                        Advance();
                        ports.Add(ParsePortSpec());
                        Match(TokKind.Semi);
                        break;
                    case "part":
                    {
                        var p = ParsePartStmt();
                        if (p is not null)
                        {
                            if (!partIds.Add(p.Id))
                                diags.Add(new FlagshipDiagnostic("error", "duplicate_part", $"Duplicate part '{p.Id}' in '{modName}'.", Cur.Line, p.Id));
                            else
                                parts.Add(p);
                        }
                        break;
                    }
                    case "use":
                    {
                        var u = ParseUseStmt();
                        if (u is not null)
                        {
                            if (!partIds.Add(u.InstanceId))
                                diags.Add(new FlagshipDiagnostic("error", "duplicate_instance", $"Duplicate instance '{u.InstanceId}' in '{modName}'.", Cur.Line, u.InstanceId));
                            else
                                uses.Add(u);
                        }
                        break;
                    }
                    case "net":
                    {
                        var n = ParseNetStmt();
                        if (n is not null) nets.Add(n);
                        break;
                    }
                    case "chain":
                    {
                        var c = ParseChainStmt();
                        if (c is not null) chains.Add(c);
                        break;
                    }
                    case "directive":
                        Advance();
                        directives.Add(Advance().Text);
                        Match(TokKind.Semi);
                        break;
                    default:
                        diags.Add(new FlagshipDiagnostic("error", "unknown_statement", $"Unknown statement '{Cur.Text}' in '{modName}'.", Cur.Line));
                        Advance();
                        break;
                }
            }

            Expect(TokKind.RBrace, $"Expected closing '}}' for {modName}");
            return new FlagshipModuleDef(modName, defaultParams, ports, parts, uses, nets, chains, directives);
        }

        private FlagshipPortDef ParsePortSpec()
        {
            var name = Advance().Text;
            string dir = "inout";
            int width = 1;
            if (Match(TokKind.Colon))
            {
                dir = Advance().Text.ToLowerInvariant();
            }
            if (Match(TokKind.LBracket))
            {
                if (int.TryParse(Advance().Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w >= 1)
                    width = w;
                Expect(TokKind.RBracket, "Expected ']' after port width");
            }
            return new FlagshipPortDef(name, dir, width);
        }

        private FlagshipPartDef? ParsePartStmt()
        {
            int lineNo = Cur.Line;
            Advance(); // 'part'
            if (Cur.Kind is not (TokKind.Word or TokKind.String))
            {
                diags.Add(new FlagshipDiagnostic("error", "missing_part_id", "Expected part identifier.", lineNo));
                return null;
            }
            var id = Advance().Text;
            if (Cur.Kind is not (TokKind.String or TokKind.Word))
            {
                diags.Add(new FlagshipDiagnostic("error", "missing_part_kind", $"Expected device kind for part '{id}'.", lineNo, id));
                return null;
            }
            var kindId = Advance().Text;

            var parms = new Dictionary<string, double>(StringComparer.Ordinal);
            var exprs = new Dictionary<string, string>(StringComparer.Ordinal);
            string? model = null, lib = null, ctrl = null, footprint = null, mpn = null, lcsc = null, displayVal = null;

            while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
            {
                var k = Advance().Text;
                Advance(); // '='
                var valTok = Advance();
                var v = valTok.Text;
                switch (k.ToLowerInvariant())
                {
                    case "model": model = v; break;
                    case "lib": lib = v; break;
                    case "ctrl": ctrl = v; break;
                    case "footprint": footprint = v; break;
                    case "mpn": mpn = v; break;
                    case "lcsc":
                    case "supplier_part": lcsc = v; break;
                    case "display": displayVal = v; break;
                    default:
                        if (v.StartsWith('$'))
                        {
                            exprs[k] = v;
                        }
                        else if (TryParseQuantity(v, out var num))
                        {
                            parms[k] = num;
                        }
                        else
                        {
                            diags.Add(new FlagshipDiagnostic("error", "invalid_part_param", $"Invalid numeric value '{v}' for parameter '{k}' on part '{id}'.", valTok.Line, id));
                        }
                        break;
                }
            }
            Match(TokKind.Semi);
            return new FlagshipPartDef(id, kindId, parms, exprs.Count > 0 ? exprs : null, model, lib, ctrl, footprint, mpn, lcsc, displayVal);
        }

        private FlagshipUseDef? ParseUseStmt()
        {
            Advance(); // 'use'
            var instId = Advance().Text;
            if (!Expect(TokKind.Eq, $"Expected '=' in 'use {instId} = <Module>'")) return null;
            var modName = Advance().Text;
            var args = new Dictionary<string, double>(StringComparer.Ordinal);
            var exprs = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Match(TokKind.LParen))
            {
                while (Cur.Kind is not (TokKind.RParen or TokKind.Eof))
                {
                    var k = Advance().Text;
                    if (!Expect(TokKind.Eq, $"Expected '=' for argument '{k}' on 'use {instId}'")) break;
                    var valTok = Advance();
                    if (valTok.Text.StartsWith('$'))
                    {
                        exprs[k] = valTok.Text;
                    }
                    else if (TryParseQuantity(valTok.Text, out var num))
                    {
                        args[k] = num;
                    }
                    else
                    {
                        diags.Add(new FlagshipDiagnostic("error", "invalid_use_arg", $"Invalid value '{valTok.Text}' for argument '{k}' on '{instId}'.", valTok.Line, instId));
                    }
                    Match(TokKind.Comma);
                }
                Expect(TokKind.RParen, $"Expected ')' after arguments on 'use {instId}'");
            }
            Match(TokKind.Semi);
            return new FlagshipUseDef(instId, modName, args, exprs.Count > 0 ? exprs : null);
        }

        private FlagshipNetDef? ParseNetStmt()
        {
            int lineNo = Cur.Line;
            Advance(); // 'net'
            var netName = Advance().Text;
            bool isGnd = string.Equals(netName, "0", StringComparison.Ordinal) ||
                         string.Equals(netName, "GND", StringComparison.OrdinalIgnoreCase);

            while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
            {
                var k = Advance().Text.ToLowerInvariant();
                Advance();
                var v = Advance().Text;
                if (k == "ground") isGnd = v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
            }

            if (!Expect(TokKind.LBrace, $"Expected '{{' for net '{netName}'")) return null;
            var pins = new List<NetlistPinRef>();
            while (Cur.Kind is not (TokKind.RBrace or TokKind.Eof))
            {
                var raw = Advance().Text;
                var dot = raw.IndexOf('.');
                if (dot <= 0 || dot == raw.Length - 1)
                {
                    diags.Add(new FlagshipDiagnostic("error", "invalid_pin_ref", $"Expected 'Instance.Pin' in net '{netName}', got '{raw}'.", lineNo, netName));
                }
                else
                {
                    pins.Add(new NetlistPinRef(raw[..dot], raw[(dot + 1)..]));
                }
                Match(TokKind.Semi);
                Match(TokKind.Comma);
            }
            Expect(TokKind.RBrace, $"Expected '}}' for net '{netName}'");
            return new FlagshipNetDef(netName, pins, isGnd);
        }

        private FlagshipChainDef? ParseChainStmt()
        {
            int lineNo = Cur.Line;
            Advance(); // 'chain'
            var steps = new List<string> { Advance().Text };
            while (Match(TokKind.Arrow))
            {
                steps.Add(Advance().Text);
            }
            Match(TokKind.Semi);
            if (steps.Count < 2)
            {
                diags.Add(new FlagshipDiagnostic("error", "short_chain", "'chain' requires at least two elements separated by '->'.", lineNo));
                return null;
            }
            return new FlagshipChainDef(steps);
        }

        private FlagshipRtlBlock? ParseRtlBlock()
        {
            Advance(); // 'rtl'
            var name = Advance().Text;
            string fmt = "velosim.rtl.v1";
            string? clk = null;
            while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
            {
                var k = Advance().Text.ToLowerInvariant();
                Advance();
                var v = Advance().Text;
                if (k == "format") fmt = v;
                else if (k == "clock") clk = v;
            }
            if (!Expect(TokKind.LBrace, $"Expected '{{' for rtl '{name}'")) return null;

            var ports = new List<FlagshipPortDef>();
            var bridges = new List<FlagshipMixedBridge>();
            string? rawJson = null;

            while (Cur.Kind is not (TokKind.RBrace or TokKind.Eof))
            {
                var kw = Cur.Text.ToLowerInvariant();
                if (kw == "port")
                {
                    Advance();
                    ports.Add(ParsePortSpec());
                    Match(TokKind.Semi);
                }
                else if (kw == "bridge")
                {
                    Advance();
                    var bName = Advance().Text;
                    string kind = "dac", dig = bName, ana = bName;
                    double vlow = 0.0, vhigh = 3.3, vth = 1.65, tr = 5e-9, tf = 5e-9;
                    while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
                    {
                        var k = Advance().Text.ToLowerInvariant();
                        Advance();
                        var v = Advance().Text;
                        switch (k)
                        {
                            case "kind": kind = v.ToLowerInvariant(); break;
                            case "digital": dig = v; break;
                            case "analog": ana = v; break;
                            case "vlow": TryParseQuantity(v, out vlow); break;
                            case "vhigh": TryParseQuantity(v, out vhigh); break;
                            case "vth": TryParseQuantity(v, out vth); break;
                            case "trise": TryParseQuantity(v, out tr); break;
                            case "tfall": TryParseQuantity(v, out tf); break;
                        }
                    }
                    Match(TokKind.Semi);
                    bridges.Add(new FlagshipMixedBridge(bName, kind, dig, ana, vlow, vhigh, vth, tr, tf));
                }
                else if (kw == "rtl_json")
                {
                    Advance();
                    rawJson = Advance().Text;
                }
                else
                {
                    diags.Add(new FlagshipDiagnostic("error", "unknown_rtl_stmt", $"Unknown statement '{Cur.Text}' in rtl '{name}'.", Cur.Line, name));
                    Advance();
                }
            }
            Expect(TokKind.RBrace, $"Expected '}}' for rtl '{name}'");
            return new FlagshipRtlBlock(name, fmt, clk, ports, bridges, rawJson);
        }

        private FlagshipLayout ParseLayoutBlock()
        {
            Advance(); // 'layout'
            double grid = 20.0;
            string style = "lines";
            while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
            {
                var k = Advance().Text.ToLowerInvariant();
                Advance();
                var v = Advance().Text;
                if (k == "grid" && TryParseQuantity(v, out var g)) grid = g;
                else if (k == "style") style = v;
            }
            Expect(TokKind.LBrace, "Expected '{' for layout block");
            var placements = new List<FlagshipPlacement>();
            while (Cur.Kind is not (TokKind.RBrace or TokKind.Eof))
            {
                if (Cur.Text.Equals("place", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    var id = Advance().Text;
                    double x = 0, y = 0;
                    int rot = 0;
                    bool mx = false, my = false;
                    while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
                    {
                        var k = Advance().Text.ToLowerInvariant();
                        Advance();
                        if (k == "at" && Match(TokKind.LParen))
                        {
                            TryParseQuantity(Advance().Text, out x);
                            Match(TokKind.Comma);
                            TryParseQuantity(Advance().Text, out y);
                            Expect(TokKind.RParen, "Expected ')' after coordinates");
                        }
                        else
                        {
                            var v = Advance().Text;
                            if (k == "rot") int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out rot);
                            else if (k == "mirror_x") mx = v.Equals("true", StringComparison.OrdinalIgnoreCase);
                            else if (k == "mirror_y") my = v.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    Match(TokKind.Semi);
                    placements.Add(new FlagshipPlacement(id, x, y, rot, mx, my));
                }
                else
                {
                    Advance();
                }
            }
            Expect(TokKind.RBrace, "Expected '}' for layout block");
            return new FlagshipLayout(grid, style, placements);
        }

        private FlagshipPcbSection ParsePcbBlock()
        {
            Advance(); // 'pcb'
            Expect(TokKind.LBrace, "Expected '{' for pcb block");
            double? bw = null, bh = null;
            int? layers = null;
            var rules = new List<FlagshipPcbRule>();
            while (Cur.Kind is not (TokKind.RBrace or TokKind.Eof))
            {
                var kw = Cur.Text.ToLowerInvariant();
                if (kw == "board")
                {
                    Advance();
                    while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
                    {
                        var k = Advance().Text.ToLowerInvariant();
                        Advance();
                        var v = Advance().Text;
                        if (k == "width" && TryParseMillimeters(v, out var w)) bw = w;
                        else if (k == "height" && TryParseMillimeters(v, out var h)) bh = h;
                        else if (k == "layers" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) layers = l;
                    }
                    Match(TokKind.Semi);
                }
                else if (kw == "rule")
                {
                    Advance();
                    string netName = "";
                    double? clr = null, w = null;
                    while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
                    {
                        var k = Advance().Text.ToLowerInvariant();
                        Advance();
                        var v = Advance().Text;
                        if (k == "net") netName = v;
                        else if (k == "clearance" && TryParseMillimeters(v, out var c)) clr = c;
                        else if (k == "width" && TryParseMillimeters(v, out var tw)) w = tw;
                    }
                    Match(TokKind.Semi);
                    if (!string.IsNullOrWhiteSpace(netName)) rules.Add(new FlagshipPcbRule(netName, clr, w));
                }
                else
                {
                    Advance();
                }
            }
            Expect(TokKind.RBrace, "Expected '}' for pcb block");
            return new FlagshipPcbSection(bw, bh, layers, rules.Count > 0 ? rules : null);
        }

        private FlagshipVerifyBlock? ParseVerifyBlock()
        {
            Advance(); // 'verify'
            var analysis = Advance().Text;
            if (!Expect(TokKind.LBrace, "Expected '{' for verify block")) return null;
            var exprs = new List<string>();
            while (Cur.Kind is not (TokKind.RBrace or TokKind.Eof))
            {
                if (Cur.Text.Equals("assert", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    exprs.Add(Advance().Text);
                    Match(TokKind.Semi);
                }
                else
                {
                    Advance();
                }
            }
            Expect(TokKind.RBrace, "Expected '}' for verify block");
            return new FlagshipVerifyBlock(analysis, exprs);
        }

        private NetlistModelDef? ParseModelBlock()
        {
            int lineNo = Cur.Line;
            Advance(); // 'model'
            var mName = Advance().Text;
            var kind = NetlistModelKind.SpiceModelCard;
            string? osdiPath = null;
            while (Cur.Kind == TokKind.Word && Peek().Kind == TokKind.Eq)
            {
                var k = Advance().Text.ToLowerInvariant();
                Advance();
                var v = Advance().Text;
                if (k == "kind" && Enum.TryParse<NetlistModelKind>(v, ignoreCase: true, out var parsedKind))
                    kind = parsedKind;
                else if (k == "osdi")
                    osdiPath = v;
            }
            if (Cur.Kind is not (TokKind.MultilineString or TokKind.String))
            {
                diags.Add(new FlagshipDiagnostic("error", "missing_model_body", $"Expected raw string body for model '{mName}'.", lineNo, mName));
                return null;
            }
            var body = Advance().Text;
            return new NetlistModelDef(kind, mName, body.Trim(), OsdiLibraryPath: osdiPath);
        }
    }

    // ==================== HIERARCHICAL ELABORATION ====================

    /// <summary>
    /// Recursively flattens hierarchical `module` instantiations (`use inst = Mod(...)`),
    /// substitutes `$param` expressions, expands signal-flow `chain` statements into
    /// explicit electrical nets, and rejects cycles or depth violations fail-closed.
    /// </summary>
    public static ElaboratedNetlist Elaborate(
        FlagshipNetlistDocument document,
        Func<string, NetlistPartDefaults?>? libraryResolver = null)
    {
        var diags = new List<FlagshipDiagnostic>();
        var modMap = document.Modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        var rtlMap = document.RtlBlocks.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
        var flatParts = new List<FlagshipPartDef>();
        var netBuckets = new Dictionary<string, (bool IsGround, List<NetlistPinRef> Pins)>(StringComparer.OrdinalIgnoreCase);
        var directives = new List<string>();
        var modelMap = document.Models.ToDictionary(m => m.ModelName, StringComparer.OrdinalIgnoreCase);
        var bridges = new List<FlagshipMixedBridge>();
        var activeStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddNetPin(string netName, NetlistPinRef pin, bool isGround = false)
        {
            var effName = isGround || string.Equals(netName, "0", StringComparison.Ordinal) ||
                          string.Equals(netName, "GND", StringComparison.OrdinalIgnoreCase)
                ? "GND"
                : netName;
            bool effGnd = isGround || string.Equals(effName, "GND", StringComparison.OrdinalIgnoreCase);
            if (!netBuckets.TryGetValue(effName, out var bucket))
            {
                bucket = (effGnd, new List<NetlistPinRef>());
                netBuckets[effName] = bucket;
            }
            else if (effGnd && !bucket.IsGround)
            {
                netBuckets[effName] = (true, bucket.Pins);
            }
            if (!bucket.Pins.Contains(pin))
            {
                bucket.Pins.Add(pin);
            }
        }

        foreach (var rtl in document.RtlBlocks)
        {
            bridges.AddRange(rtl.Bridges);
        }

        ElaborateModule(
            document.Circuit,
            scopeSuffix: "",
            paramEnv: new Dictionary<string, double>(StringComparer.Ordinal),
            portBindings: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            depth: 0);

        void ElaborateModule(
            FlagshipModuleDef mod,
            string scopeSuffix,
            IReadOnlyDictionary<string, double> paramEnv,
            IReadOnlyDictionary<string, string> portBindings,
            int depth)
        {
            if (depth > MaxHierarchyDepth)
            {
                diags.Add(new FlagshipDiagnostic("error", "hierarchy_depth_exceeded", $"Module hierarchy exceeded max depth {MaxHierarchyDepth} at '{mod.Name}'.", SubjectId: mod.Name));
                return;
            }

            string ScopePartId(string localId) => string.IsNullOrEmpty(scopeSuffix) ? localId : $"{localId}_{scopeSuffix}";
            string ResolveNet(string localNet, bool explicitGround = false)
            {
                if (explicitGround || string.Equals(localNet, "0", StringComparison.Ordinal) ||
                    string.Equals(localNet, "GND", StringComparison.OrdinalIgnoreCase))
                {
                    return "GND";
                }
                if (portBindings.TryGetValue(localNet, out var bound)) return bound;
                return string.IsNullOrEmpty(scopeSuffix) ? localNet : $"{scopeSuffix}_{localNet}";
            }

            var localPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localUseMap = mod.Uses.ToDictionary(u => u.InstanceId, StringComparer.OrdinalIgnoreCase);

            foreach (var p in mod.Parts)
            {
                localPartIds.Add(p.Id);
                var resolvedParams = new Dictionary<string, double>(StringComparer.Ordinal);
                string? effModel = p.ModelName;

                if (!string.IsNullOrWhiteSpace(p.LibraryPartId) && libraryResolver is not null)
                {
                    var defaults = libraryResolver(p.LibraryPartId);
                    if (defaults is not null)
                    {
                        foreach (var kv in defaults.Parameters) resolvedParams[kv.Key] = kv.Value;
                        if (string.IsNullOrWhiteSpace(effModel) && defaults.Model is not null)
                            effModel = defaults.Model.ModelName;
                        if (defaults.Model is not null && !modelMap.ContainsKey(defaults.Model.ModelName))
                            modelMap[defaults.Model.ModelName] = defaults.Model;
                    }
                }

                foreach (var kv in p.Parameters) resolvedParams[kv.Key] = kv.Value;
                if (p.ParameterExpressions is not null)
                {
                    foreach (var (pk, expr) in p.ParameterExpressions)
                    {
                        var varName = expr.TrimStart('$');
                        if (paramEnv.TryGetValue(varName, out var pv))
                            resolvedParams[pk] = pv;
                        else
                            diags.Add(new FlagshipDiagnostic("error", "unresolved_param", $"Unresolved parameter '{expr}' on part '{p.Id}' in '{mod.Name}'.", SubjectId: p.Id));
                    }
                }

                var scopedCtrl = string.IsNullOrWhiteSpace(p.ControlSourceId) ? null : ScopePartId(p.ControlSourceId);
                flatParts.Add(p with
                {
                    Id = ScopePartId(p.Id),
                    Parameters = resolvedParams,
                    ParameterExpressions = null,
                    ModelName = effModel,
                    ControlSourceId = scopedCtrl
                });
            }

            foreach (var dir in mod.Directives) directives.Add(dir);

            var childPortBindings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            void BindPinOrChildPort(string netName, bool isGnd, string instId, string pinName)
            {
                var effNet = ResolveNet(netName, isGnd);
                if (localUseMap.ContainsKey(instId))
                {
                    if (!childPortBindings.TryGetValue(instId, out var map))
                    {
                        map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        childPortBindings[instId] = map;
                    }
                    map[pinName] = effNet;
                }
                else
                {
                    AddNetPin(effNet, new NetlistPinRef(ScopePartId(instId), pinName), string.Equals(effNet, "GND", StringComparison.OrdinalIgnoreCase));
                }
            }

            foreach (var net in mod.Nets)
            {
                foreach (var pr in net.Pins)
                {
                    BindPinOrChildPort(net.Name, net.IsGround, pr.InstanceId, pr.PinName);
                }
            }

            int autoNetIdx = 0;
            foreach (var chain in mod.Chains)
            {
                string? currentNet = null;
                for (int s = 0; s < chain.Steps.Count; ++s)
                {
                    var step = chain.Steps[s];
                    var dot = step.IndexOf('.');
                    if (dot > 0 && dot < step.Length - 1)
                    {
                        var inst = step[..dot];
                        var pin = step[(dot + 1)..];
                        currentNet ??= $"net_{++autoNetIdx}";
                        BindPinOrChildPort(currentNet, false, inst, pin);
                    }
                    else if (localPartIds.Contains(step))
                    {
                        var inNet = currentNet ?? $"net_{++autoNetIdx}";
                        BindPinOrChildPort(inNet, false, step, "p");
                        if (s + 1 < chain.Steps.Count)
                        {
                            var next = chain.Steps[s + 1];
                            if (!localPartIds.Contains(next) && !next.Contains('.'))
                            {
                                BindPinOrChildPort(next, false, step, "n");
                                currentNet = next;
                                s++;
                            }
                            else
                            {
                                currentNet = $"net_{++autoNetIdx}";
                                BindPinOrChildPort(currentNet, false, step, "n");
                            }
                        }
                    }
                    else
                    {
                        currentNet = step;
                    }
                }
            }

            foreach (var u in mod.Uses)
            {
                if (rtlMap.ContainsKey(u.ModuleName)) continue;
                if (!modMap.TryGetValue(u.ModuleName, out var childMod))
                {
                    diags.Add(new FlagshipDiagnostic("error", "unknown_module", $"Unknown module '{u.ModuleName}' referenced by 'use {u.InstanceId}'.", SubjectId: u.InstanceId));
                    continue;
                }
                if (!activeStack.Add(childMod.Name))
                {
                    diags.Add(new FlagshipDiagnostic("error", "cyclic_module_hierarchy", $"Cyclic module instantiation involving '{childMod.Name}'.", SubjectId: u.InstanceId));
                    continue;
                }

                var childEnv = new Dictionary<string, double>(childMod.DefaultParameters, StringComparer.Ordinal);
                foreach (var kv in u.Arguments) childEnv[kv.Key] = kv.Value;
                if (u.ArgumentExpressions is not null)
                {
                    foreach (var (ak, expr) in u.ArgumentExpressions)
                    {
                        var vn = expr.TrimStart('$');
                        if (paramEnv.TryGetValue(vn, out var val)) childEnv[ak] = val;
                        else diags.Add(new FlagshipDiagnostic("error", "unresolved_use_param", $"Unresolved parameter '{expr}' on 'use {u.InstanceId}'.", SubjectId: u.InstanceId));
                    }
                }

                var bindings = childPortBindings.TryGetValue(u.InstanceId, out var b)
                    ? b
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var childScope = string.IsNullOrEmpty(scopeSuffix) ? u.InstanceId : $"{scopeSuffix}_{u.InstanceId}";
                ElaborateModule(childMod, childScope, childEnv, bindings, depth + 1);
                activeStack.Remove(childMod.Name);
            }
        }

        var flatNets = netBuckets
            .OrderBy(kv => kv.Value.IsGround ? "" : kv.Key, StringComparer.Ordinal)
            .Select(kv => new FlagshipNetDef(kv.Key, kv.Value.Pins, kv.Value.IsGround))
            .ToList();

        return new ElaboratedNetlist(
            document.Name,
            flatParts,
            flatNets,
            document.Circuit.Ports,
            directives,
            modelMap.Values.OrderBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase).ToList(),
            document.RtlBlocks,
            bridges,
            diags);
    }

    // ==================== POLYGLOT SVG METADATA CODEC ====================

    public static string EmbedIntoSvg(string svgText, FlagshipNetlistDocument document)
    {
        var netlistText = Format(document);
        var safeCdata = netlistText.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal);
        var meta = $"  <metadata>\n    {PolyglotStartMarker}\n{safeCdata}{PolyglotEndMarker}\n  </metadata>\n";
        var defsIdx = svgText.IndexOf("  <defs>", StringComparison.Ordinal);
        if (defsIdx >= 0) return svgText.Insert(defsIdx, meta);
        var closeTag = svgText.IndexOf('>');
        return closeTag >= 0 ? svgText.Insert(closeTag + 1, "\n" + meta) : svgText + "\n" + meta;
    }

    public static FlagshipParseResult ExtractFromPolyglotSvg(
        string svgText,
        Func<string, NetlistPartDefaults?>? libraryResolver = null,
        bool verifyHashes = true)
    {
        if (string.IsNullOrWhiteSpace(svgText))
        {
            return new FlagshipParseResult(false, null, [new FlagshipDiagnostic("error", "empty_svg", "SVG text is empty.")]);
        }
        var start = svgText.IndexOf(PolyglotStartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return new FlagshipParseResult(false, null, [new FlagshipDiagnostic("error", "missing_embedded_netlist", "SVG does not contain an embedded spice.netlist.v3 metadata block.")]);
        }
        var payloadStart = start + PolyglotStartMarker.Length;
        var end = svgText.IndexOf(PolyglotEndMarker, payloadStart, StringComparison.Ordinal);
        if (end < 0)
        {
            return new FlagshipParseResult(false, null, [new FlagshipDiagnostic("error", "unterminated_embedded_netlist", "Embedded spice.netlist.v3 CDATA block is unterminated.")]);
        }
        var embedded = svgText[payloadStart..end].Replace("]]]]><![CDATA[>", "]]>", StringComparison.Ordinal);
        return Parse(embedded, libraryResolver, verifyHashes);
    }

    // ==================== DETERMINISTIC SIGNAL-FLOW AUTO-PLACER ====================

    public static Dictionary<string, FlagshipPlacement> ComputeSignalFlowLayout(
        IReadOnlyList<FlagshipPartDef> parts,
        IReadOnlyList<FlagshipNetDef> nets,
        double grid = 20.0)
    {
        var partNetMap = new Dictionary<string, List<(string Net, bool IsGround)>>(StringComparer.OrdinalIgnoreCase);
        var netPartsMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in parts) partNetMap[p.Id] = [];
        foreach (var n in nets)
        {
            if (!netPartsMap.TryGetValue(n.Name, out var list))
            {
                list = [];
                netPartsMap[n.Name] = list;
            }
            foreach (var pr in n.Pins)
            {
                if (partNetMap.TryGetValue(pr.InstanceId, out var pNets))
                    pNets.Add((n.Name, n.IsGround));
                if (!list.Contains(pr.InstanceId, StringComparer.OrdinalIgnoreCase))
                    list.Add(pr.InstanceId);
            }
        }

        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var p in parts.Where(p => p.KindId is "spice.vsrc" or "spice.isrc").OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            rank[p.Id] = 0;
            queue.Enqueue(p.Id);
        }
        if (queue.Count == 0 && parts.Count > 0)
        {
            var first = parts.OrderBy(p => p.Id, StringComparer.Ordinal).First();
            rank[first.Id] = 0;
            queue.Enqueue(first.Id);
        }

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int r = rank[cur];
            foreach (var (netName, isGnd) in partNetMap[cur])
            {
                if (isGnd) continue;
                if (!netPartsMap.TryGetValue(netName, out var neighbors)) continue;
                foreach (var nb in neighbors.OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (!rank.ContainsKey(nb))
                    {
                        rank[nb] = r + 1;
                        queue.Enqueue(nb);
                    }
                }
            }
        }

        foreach (var p in parts.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (!rank.ContainsKey(p.Id)) rank[p.Id] = 1;
        }

        var result = new Dictionary<string, FlagshipPlacement>(StringComparer.OrdinalIgnoreCase);
        foreach (var colGroup in parts.GroupBy(p => rank[p.Id]).OrderBy(g => g.Key))
        {
            int col = colGroup.Key;
            int row = 0;
            foreach (var p in colGroup.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                bool touchesGnd = partNetMap[p.Id].Any(x => x.IsGround);
                bool isVertical = p.KindId is "spice.vsrc" or "spice.isrc" ||
                                  (touchesGnd && p.KindId is "spice.resistor" or "spice.capacitor" or "spice.inductor" or "spice.diode" or "spice.core");
                double x = Snap(80.0 + col * 140.0, grid);
                double y = Snap(100.0 + row * 120.0, grid);
                result[p.Id] = new FlagshipPlacement(p.Id, x, y, isVertical ? 90 : 0);
                row++;
            }
        }
        return result;
    }

    // ==================== QUANTITY / SI UNIT HELPERS ====================

    public static bool TryParseQuantity(string raw, out double value)
    {
        value = 0.0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
            return true;

        int numEnd = 0;
        if (numEnd < s.Length && (s[numEnd] == '+' || s[numEnd] == '-')) numEnd++;
        bool seenDigit = false;
        while (numEnd < s.Length && char.IsDigit(s[numEnd])) { numEnd++; seenDigit = true; }
        if (numEnd < s.Length && s[numEnd] == '.')
        {
            numEnd++;
            while (numEnd < s.Length && char.IsDigit(s[numEnd])) { numEnd++; seenDigit = true; }
        }
        if (!seenDigit) return false;
        if (numEnd < s.Length && (s[numEnd] == 'e' || s[numEnd] == 'E'))
        {
            int ep = numEnd + 1;
            if (ep < s.Length && (s[ep] == '+' || s[ep] == '-')) ep++;
            int ed = 0;
            while (ep < s.Length && char.IsDigit(s[ep])) { ep++; ed++; }
            if (ed > 0) numEnd = ep;
        }
        if (!double.TryParse(s[..numEnd], NumberStyles.Float, CultureInfo.InvariantCulture, out var mantissa))
            return false;

        var suf = s[numEnd..].Trim();
        if (suf.Length == 0) { value = mantissa; return double.IsFinite(value); }

        var lower = suf.ToLowerInvariant();
        if (lower.EndsWith("ohms", StringComparison.Ordinal)) lower = lower[..^4];
        else if (lower.EndsWith("ohm", StringComparison.Ordinal)) lower = lower[..^3];
        else if (lower.EndsWith("hz", StringComparison.Ordinal)) lower = lower[..^2];
        else if (lower.Length >= 1 && lower[^1] is 'v' or 'a' or 'f' or 'h' or 's')
        {
            if (lower.Length == 1 && lower[0] == 'f') { /* keep femto */ }
            else lower = lower[..^1];
        }

        double mult = lower switch
        {
            "" => 1.0,
            "t" => 1e12,
            "g" => 1e9,
            "meg" => 1e6,
            "k" => 1e3,
            "m" => 1e-3,
            "u" or "µ" => 1e-6,
            "n" => 1e-9,
            "p" => 1e-12,
            "f" => 1e-15,
            "a" => 1e-18,
            "mil" => 25.4e-6,
            _ => double.NaN
        };
        if (double.IsNaN(mult)) return false;
        value = mantissa * mult;
        return double.IsFinite(value);
    }

    private static bool TryParseMillimeters(string raw, out double mm)
    {
        mm = 0.0;
        var s = raw.Trim();
        if (s.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            return double.TryParse(s[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out mm) && double.IsFinite(mm);
        if (s.EndsWith("mil", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(s[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var mils) && double.IsFinite(mils))
            {
                mm = mils * 0.0254;
                return true;
            }
            return false;
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out mm) && double.IsFinite(mm);
    }

    private static string FmtValue(string key, double v)
    {
        if (key.Contains("ohms", StringComparison.OrdinalIgnoreCase) || key == "r") return FmtUnit(v, "");
        if (key.Contains("farads", StringComparison.OrdinalIgnoreCase) || key == "c") return FmtUnit(v, "F");
        if (key.Contains("henries", StringComparison.OrdinalIgnoreCase)) return FmtUnit(v, "H");
        if (key.Contains("volts", StringComparison.OrdinalIgnoreCase)) return FmtUnit(v, "V");
        if (key.Contains("amps", StringComparison.OrdinalIgnoreCase)) return FmtUnit(v, "A");
        return FmtCanon(v);
    }

    private static string FmtUnit(double v, string unit)
    {
        if (v == 0.0) return "0" + unit;
        double a = Math.Abs(v);
        if (a >= 1e9) return FmtCanon(v / 1e9) + "G" + unit;
        if (a >= 1e6) return FmtCanon(v / 1e6) + "Meg" + unit;
        if (a >= 1e3) return FmtCanon(v / 1e3) + "k" + unit;
        if (a >= 1.0) return FmtCanon(v) + unit;
        if (a >= 1e-3) return FmtCanon(v * 1e3) + "m" + unit;
        if (a >= 1e-6) return FmtCanon(v * 1e6) + "u" + unit;
        if (a >= 1e-9) return FmtCanon(v * 1e9) + "n" + unit;
        if (a >= 1e-12) return FmtCanon(v * 1e12) + "p" + unit;
        if (a >= 1e-15) return FmtCanon(v * 1e15) + "f" + unit;
        return FmtCanon(v);
    }

    private static string FmtCanon(double v) => v.ToString("G15", CultureInfo.InvariantCulture);
    private static double Snap(double v, double g) => Math.Round(v / g) * g;
    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    private static string Sha256Hex(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string Dedent(string text)
    {
        var lines = NormalizeNewlines(text).Split('\n');
        int minIndent = int.MaxValue;
        foreach (var l in lines)
        {
            if (string.IsNullOrWhiteSpace(l)) continue;
            int sp = 0;
            while (sp < l.Length && l[sp] == ' ') sp++;
            minIndent = Math.Min(minIndent, sp);
        }
        if (minIndent == int.MaxValue || minIndent == 0) return text.Trim();
        return string.Join("\n", lines.Select(l => l.Length >= minIndent ? l[minIndent..] : l.TrimStart())).Trim();
    }
}
