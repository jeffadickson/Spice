using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spice.Ir;

namespace Spice.Schematic;

public enum ErcSeverity
{
    Warning,
    Error
}

public sealed record ErcDiagnostic(
    ErcSeverity Severity,
    string Code,
    string Message,
    string? SheetId = null,
    string? SubjectId = null);

public sealed record SchematicCompileResult(
    bool Succeeded,
    CircuitIr? Circuit,
    IReadOnlyList<ErcDiagnostic> Diagnostics);

/// <summary>
/// Electrical Rules Check (ERC), deterministic Canonical IR emission, JSON
/// persistence, SPICE export, and SVG vector export for Spice.Schematic
/// (ARCH-SCHEM-001 .. ARCH-SCHEM-005).
/// </summary>
public static class SchematicCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IReadOnlyList<ErcDiagnostic> RunErc(
        SchematicDocument document,
        DeviceRegistry? registry = null)
    {
        var reg = registry ?? DeviceRegistry.Default;
        var diagnostics = new List<ErcDiagnostic>();

        if (string.IsNullOrWhiteSpace(document.SchemaVersion))
        {
            diagnostics.Add(new ErcDiagnostic(ErcSeverity.Error, "missing_schema_version", "Schematic SchemaVersion is required."));
        }

        ValidateSheet(document.RootSheet, reg, diagnostics, requireGround: true);
        if (document.SubSheets is not null)
        {
            foreach (var sub in document.SubSheets)
            {
                ValidateSheet(sub, reg, diagnostics, requireGround: false);
            }
        }

        return diagnostics;
    }

    public static SchematicCompileResult CompileToIr(
        SchematicDocument document,
        DeviceRegistry? registry = null)
    {
        var reg = registry ?? DeviceRegistry.Default;
        var diagnostics = RunErc(document, reg);
        if (diagnostics.Any(d => d.Severity == ErcSeverity.Error))
        {
            return new SchematicCompileResult(false, null, diagnostics);
        }

        var nodes = new HashSet<string>(StringComparer.Ordinal) { "0" };
        var devices = new List<DeviceInstanceIr>();

        EmitSheetInto(document.RootSheet, prefix: "", nodes, devices);
        if (document.SubSheets is not null)
        {
            foreach (var sub in document.SubSheets)
            {
                EmitSheetInto(sub, prefix: $"{sub.SheetId}.", nodes, devices);
            }
        }

        var orderedNodes = nodes
            .OrderBy(n => n == "0" ? "" : n, StringComparer.Ordinal)
            .ToList();

        var provenance = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "Spice.Schematic",
            ["schema_version"] = document.SchemaVersion,
            ["schematic_name"] = document.Name
        };

        var circuit = new CircuitIr(document.Name, orderedNodes, devices, provenance);
        var irErrors = reg.ValidateCircuit(circuit);
        if (irErrors.Count > 0)
        {
            var merged = diagnostics.ToList();
            foreach (var err in irErrors)
            {
                merged.Add(new ErcDiagnostic(ErcSeverity.Error, err.Code, err.Message, SubjectId: err.SubjectId));
            }
            return new SchematicCompileResult(false, null, merged);
        }

        return new SchematicCompileResult(true, circuit, diagnostics);
    }

    public static string SerializeJson(SchematicDocument document) =>
        JsonSerializer.Serialize(document, JsonOptions);

    public static SchematicDocument DeserializeJson(string json) =>
        JsonSerializer.Deserialize<SchematicDocument>(json, JsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize SchematicDocument JSON.");

    public static string ExportSpiceNetlist(
        SchematicDocument document,
        AnalysisKind analysis = AnalysisKind.OperatingPoint,
        IReadOnlyDictionary<string, double>? analysisOptions = null,
        DeviceRegistry? registry = null)
    {
        var compiled = CompileToIr(document, registry);
        if (!compiled.Succeeded || compiled.Circuit is null)
        {
            throw new InvalidOperationException(
                "Schematic failed ERC: " +
                string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        }

        var req = new SimulationRequestIr(
            CaseId: document.Name,
            Circuit: compiled.Circuit,
            Analysis: analysis,
            Options: analysisOptions);
        return SpiceNetlistEmitter.EmitNetlist(req, includeXycePrintCsv: false, registry: registry);
    }

    /// <summary>
    /// Deterministic vector SVG export of the root schematic sheet (ARCH-SCHEM-005).
    /// </summary>
    public static string ExportSvg(SchematicDocument document)
    {
        var sheet = document.RootSheet;
        double minX = 0.0, minY = 0.0, maxX = 400.0, maxY = 260.0;
        foreach (var comp in sheet.Components)
        {
            minX = Math.Min(minX, comp.X - 60.0);
            minY = Math.Min(minY, comp.Y - 40.0);
            maxX = Math.Max(maxX, comp.X + 120.0);
            maxY = Math.Max(maxY, comp.Y + 80.0);
        }

        var w = maxX - minX;
        var h = maxY - minY;
        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{minX:F1} {minY:F1} {w:F1} {h:F1}\">"));
        sb.AppendLine($"  <title>{EscapeXml(document.Name)} - {EscapeXml(sheet.Title)}</title>");
        sb.AppendLine(FormattableString.Invariant(
            $"  <rect x=\"{minX:F1}\" y=\"{minY:F1}\" width=\"{w:F1}\" height=\"{h:F1}\" fill=\"#0f172a\" />"));

        var compPositions = sheet.Components.ToDictionary(c => c.InstanceId, StringComparer.Ordinal);

        // Draw nets as crisp polylines with cross-probing attributes.
        foreach (var net in sheet.Nets)
        {
            var pts = net.ConnectedPins
                .Where(p => compPositions.ContainsKey(p.InstanceId))
                .Select(p => compPositions[p.InstanceId])
                .ToList();
            var effectiveNet = net.IsGlobalGround || string.Equals(net.NetName, "0", StringComparison.Ordinal)
                ? "0"
                : net.NetName.ToLowerInvariant();
            var signalAttr = effectiveNet == "0" ? "" : $"v({effectiveNet})";
            var stroke = net.IsGlobalGround ? "#22c55e" : "#38bdf8";
            sb.AppendLine($"  <g class=\"spice-net\" data-net=\"{EscapeXml(net.NetName)}\" data-signal=\"{EscapeXml(signalAttr)}\">");
            for (int i = 1; i < pts.Count; ++i)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"    <line x1=\"{pts[i - 1].X:F1}\" y1=\"{pts[i - 1].Y:F1}\" x2=\"{pts[i].X:F1}\" y2=\"{pts[i].Y:F1}\" stroke=\"transparent\" stroke-width=\"12\" />"));
                sb.AppendLine(FormattableString.Invariant(
                    $"    <line x1=\"{pts[i - 1].X:F1}\" y1=\"{pts[i - 1].Y:F1}\" x2=\"{pts[i].X:F1}\" y2=\"{pts[i].Y:F1}\" stroke=\"{stroke}\" stroke-width=\"2.5\" />"));
            }
            if (pts.Count >= 2 && !net.IsGlobalGround)
            {
                var mx = 0.5 * (pts[0].X + pts[1].X);
                var my = 0.5 * (pts[0].Y + pts[1].Y) - 6.0;
                sb.AppendLine(FormattableString.Invariant(
                    $"    <text x=\"{mx:F1}\" y=\"{my:F1}\" text-anchor=\"middle\" fill=\"#7dd3fc\" font-family=\"monospace\" font-size=\"10\">{EscapeXml(net.NetName)}</text>"));
            }
            sb.AppendLine("  </g>");
        }

        // Draw placed components with cross-probing instance metadata.
        foreach (var comp in sheet.Components)
        {
            var currentSig = comp.DeviceKindId is "spice.vsrc" or "spice.inductor"
                ? $"i({comp.InstanceId})"
                : "";
            sb.AppendLine(FormattableString.Invariant(
                $"  <g class=\"spice-component\" data-instance=\"{EscapeXml(comp.InstanceId)}\" data-kind=\"{EscapeXml(comp.DeviceKindId)}\" data-signal=\"{EscapeXml(currentSig)}\" transform=\"translate({comp.X:F1},{comp.Y:F1})\">"));
            sb.AppendLine("    <rect x=\"-32\" y=\"-18\" width=\"64\" height=\"36\" rx=\"5\" fill=\"#1e293b\" stroke=\"#e2e8f0\" stroke-width=\"1.5\" />");
            sb.AppendLine($"    <text x=\"0\" y=\"-24\" text-anchor=\"middle\" fill=\"#f8fafc\" font-family=\"monospace\" font-size=\"11\" font-weight=\"bold\">{EscapeXml(comp.InstanceId)}</text>");
            var shortKind = comp.DeviceKindId.StartsWith("spice.", StringComparison.Ordinal)
                ? comp.DeviceKindId["spice.".Length..]
                : comp.DeviceKindId;
            sb.AppendLine($"    <text x=\"0\" y=\"-2\" text-anchor=\"middle\" fill=\"#94a3b8\" font-family=\"monospace\" font-size=\"9\">{EscapeXml(shortKind)}</text>");
            if (comp.Parameters.Count > 0)
            {
                var first = comp.Parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal).First();
                sb.AppendLine(FormattableString.Invariant(
                    $"    <text x=\"0\" y=\"11\" text-anchor=\"middle\" fill=\"#38bdf8\" font-family=\"monospace\" font-size=\"9\">{first.Value:G4}</text>"));
            }
            sb.AppendLine("  </g>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static void ValidateSheet(
        SchematicSheet sheet,
        DeviceRegistry reg,
        List<ErcDiagnostic> diagnostics,
        bool requireGround)
    {
        var compMap = new Dictionary<string, PlacedComponent>(StringComparer.OrdinalIgnoreCase);
        foreach (var comp in sheet.Components)
        {
            if (string.IsNullOrWhiteSpace(comp.InstanceId))
            {
                diagnostics.Add(new ErcDiagnostic(ErcSeverity.Error, "empty_instance_id", "PlacedComponent has empty InstanceId.", sheet.SheetId));
                continue;
            }
            if (!compMap.TryAdd(comp.InstanceId, comp))
            {
                diagnostics.Add(new ErcDiagnostic(ErcSeverity.Error, "duplicate_instance", $"Duplicate component '{comp.InstanceId}'.", sheet.SheetId, comp.InstanceId));
            }
            if (!reg.TryGet(comp.DeviceKindId, out _))
            {
                diagnostics.Add(new ErcDiagnostic(ErcSeverity.Error, "unknown_device_kind", $"Component '{comp.InstanceId}' uses unknown device kind '{comp.DeviceKindId}'.", sheet.SheetId, comp.InstanceId));
            }
        }

        bool hasGround = false;
        var pinConnectivity = new Dictionary<(string Inst, string Pin), string>();

        foreach (var net in sheet.Nets)
        {
            var effectiveNet = net.IsGlobalGround || string.Equals(net.NetName, "0", StringComparison.Ordinal)
                ? "0"
                : net.NetName;
            if (effectiveNet == "0")
            {
                hasGround = true;
            }
            if (net.ConnectedPins.Count < 2 && effectiveNet != "0")
            {
                diagnostics.Add(new ErcDiagnostic(
                    ErcSeverity.Warning,
                    "dangling_net",
                    $"Net '{net.NetName}' has fewer than 2 connected pins.",
                    sheet.SheetId,
                    net.NetName));
            }

            foreach (var pinRef in net.ConnectedPins)
            {
                if (!compMap.ContainsKey(pinRef.InstanceId))
                {
                    diagnostics.Add(new ErcDiagnostic(
                        ErcSeverity.Error,
                        "unknown_pin_target",
                        $"Net '{net.NetName}' references missing component '{pinRef.InstanceId}'.",
                        sheet.SheetId,
                        pinRef.InstanceId));
                    continue;
                }
                var key = (pinRef.InstanceId, pinRef.PinName);
                if (!pinConnectivity.TryAdd(key, effectiveNet))
                {
                    diagnostics.Add(new ErcDiagnostic(
                        ErcSeverity.Error,
                        "conflicting_net_connection",
                        $"Pin '{pinRef.InstanceId}.{pinRef.PinName}' is connected to multiple nets.",
                        sheet.SheetId,
                        pinRef.InstanceId));
                }
            }
        }

        if (requireGround && !hasGround)
        {
            diagnostics.Add(new ErcDiagnostic(
                ErcSeverity.Error,
                "missing_ground",
                "Root schematic sheet must contain a global ground net ('0').",
                sheet.SheetId));
        }

        // Verify every required pin on every placed component is connected and no voltage source is shorted.
        foreach (var comp in sheet.Components)
        {
            if (!reg.TryGet(comp.DeviceKindId, out var desc) || desc is null) continue;
            var pins = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pin in desc.Pins)
            {
                if (pinConnectivity.TryGetValue((comp.InstanceId, pin.Name), out var netName))
                {
                    pins[pin.Name] = netName;
                }
                else
                {
                    diagnostics.Add(new ErcDiagnostic(
                        ErcSeverity.Error,
                        "unconnected_pin",
                        $"Component '{comp.InstanceId}' pin '{pin.Name}' is unconnected.",
                        sheet.SheetId,
                        comp.InstanceId));
                }
            }

            if (string.Equals(comp.DeviceKindId, "spice.vsrc", StringComparison.Ordinal) &&
                pins.TryGetValue("p", out var vp) &&
                pins.TryGetValue("n", out var vn) &&
                string.Equals(vp, vn, StringComparison.Ordinal))
            {
                diagnostics.Add(new ErcDiagnostic(
                    ErcSeverity.Error,
                    "shorted_voltage_source",
                    $"Voltage source '{comp.InstanceId}' has both terminals shorted to net '{vp}'.",
                    sheet.SheetId,
                    comp.InstanceId));
            }
        }
    }

    private static void EmitSheetInto(
        SchematicSheet sheet,
        string prefix,
        HashSet<string> nodes,
        List<DeviceInstanceIr> devices)
    {
        var pinMap = new Dictionary<(string Inst, string Pin), string>();
        foreach (var net in sheet.Nets)
        {
            var effectiveNet = net.IsGlobalGround || string.Equals(net.NetName, "0", StringComparison.Ordinal)
                ? "0"
                : $"{prefix}{net.NetName}";
            nodes.Add(effectiveNet);
            foreach (var p in net.ConnectedPins)
            {
                pinMap[(p.InstanceId, p.PinName)] = effectiveNet;
            }
        }

        foreach (var comp in sheet.Components)
        {
            var conns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var ((inst, pin), netName) in pinMap)
            {
                if (string.Equals(inst, comp.InstanceId, StringComparison.Ordinal))
                {
                    conns[pin] = netName;
                }
            }
            devices.Add(new DeviceInstanceIr(
                Id: $"{prefix}{comp.InstanceId}",
                KindId: comp.DeviceKindId,
                PinConnections: conns,
                Parameters: new Dictionary<string, double>(comp.Parameters, StringComparer.Ordinal)));
        }
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal)
         .Replace("\"", "&quot;", StringComparison.Ordinal);
}
