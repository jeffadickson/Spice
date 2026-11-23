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
/// Optional DC operating-point overlay and snap-grid settings rendered directly
/// onto exported schematic SVGs (DC node voltages, branch current arrows, and grid).
/// </summary>
public sealed record OperatingPointOverlay(
    IReadOnlyDictionary<string, double> NodeVoltages,
    IReadOnlyDictionary<string, double> BranchCurrents,
    double GridSize = 20.0,
    bool ShowGrid = true,
    bool ShowOperatingPoint = true);

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
    /// Formats a physical quantity with compact SI engineering suffixes (V, A, Ohm, F, H).
    /// </summary>
    public static string FormatEngineering(double value, string unit)
    {
        if (!double.IsFinite(value)) return $"NaN {unit}";
        var abs = Math.Abs(value);
        if (abs < 1e-15) return FormattableString.Invariant($"0.00 {unit}");
        if (abs >= 1e9) return FormattableString.Invariant($"{value / 1e9:F2} G{unit}");
        if (abs >= 1e6) return FormattableString.Invariant($"{value / 1e6:F2} M{unit}");
        if (abs >= 1e3) return FormattableString.Invariant($"{value / 1e3:F2} k{unit}");
        if (abs >= 1.0) return FormattableString.Invariant($"{value:F2} {unit}");
        if (abs >= 1e-3) return FormattableString.Invariant($"{value * 1e3:F2} m{unit}");
        if (abs >= 1e-6) return FormattableString.Invariant($"{value * 1e6:F2} u{unit}");
        if (abs >= 1e-9) return FormattableString.Invariant($"{value * 1e9:F2} n{unit}");
        if (abs >= 1e-12) return FormattableString.Invariant($"{value * 1e12:F2} p{unit}");
        return FormattableString.Invariant($"{value * 1e15:F2} f{unit}");
    }

    /// <summary>
    /// Computes complete DC node voltages and per-component branch currents (passive sign
    /// convention p -> n) from a solved OperatingPoint ResultManifestIr.
    /// </summary>
    public static OperatingPointOverlay BuildOperatingPointOverlay(
        SchematicDocument document,
        ResultManifestIr opResult,
        double gridSize = 20.0,
        bool showGrid = true,
        bool showOperatingPoint = true)
    {
        var voltages = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["0"] = 0.0 };
        var currents = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (opResult.RealData is not null)
        {
            foreach (var (sig, vals) in opResult.RealData.SignalValues)
            {
                if (vals.Count == 0) continue;
                var val = vals[0];
                if (sig.StartsWith("v(", StringComparison.OrdinalIgnoreCase) && sig.EndsWith(')'))
                {
                    var net = sig[2..^1];
                    voltages[net] = val;
                }
                else if (sig.StartsWith("i(", StringComparison.OrdinalIgnoreCase) && sig.EndsWith(')'))
                {
                    var inst = sig[2..^1];
                    currents[inst] = val;
                }
            }
        }

        // Map (instanceId, pinName) -> effective net name to derive branch currents for R, C, I, D.
        var pinToNet = new Dictionary<(string Inst, string Pin), string>();
        foreach (var net in document.RootSheet.Nets)
        {
            var eff = net.IsGlobalGround || string.Equals(net.NetName, "0", StringComparison.Ordinal)
                ? "0"
                : net.NetName.ToLowerInvariant();
            foreach (var pr in net.ConnectedPins)
            {
                pinToNet[(pr.InstanceId, pr.PinName)] = eff;
            }
        }

        double GetNodeV(string inst, string pin)
        {
            if (pinToNet.TryGetValue((inst, pin), out var net) && voltages.TryGetValue(net, out var v))
            {
                return v;
            }
            return 0.0;
        }

        foreach (var comp in document.RootSheet.Components)
        {
            if (currents.ContainsKey(comp.InstanceId)) continue;
            switch (comp.DeviceKindId)
            {
                case "spice.resistor":
                    if (comp.Parameters.TryGetValue("ohms", out var r) && r > 0.0)
                    {
                        var vp = GetNodeV(comp.InstanceId, "p");
                        var vn = GetNodeV(comp.InstanceId, "n");
                        currents[comp.InstanceId] = (vp - vn) / r;
                    }
                    break;
                case "spice.capacitor":
                    currents[comp.InstanceId] = 0.0;
                    break;
                case "spice.isrc":
                    currents[comp.InstanceId] = comp.Parameters.TryGetValue("dc_amps", out var dcI) ? dcI : 0.0;
                    break;
                case "spice.diode":
                    {
                        var vd = GetNodeV(comp.InstanceId, "p") - GetNodeV(comp.InstanceId, "n");
                        var isA = comp.Parameters.TryGetValue("is_amps", out var iv) ? iv : 1e-14;
                        var nEm = comp.Parameters.TryGetValue("n_emission", out var nv) ? nv : 1.0;
                        var vt = 0.025851991;
                        var arg = Math.Clamp(vd / (nEm * vt), -80.0, 40.0);
                        currents[comp.InstanceId] = isA * (Math.Exp(arg) - 1.0);
                        break;
                    }
            }
        }

        return new OperatingPointOverlay(voltages, currents, gridSize, showGrid, showOperatingPoint);
    }

    /// <summary>
    /// Deterministic vector SVG export of the root schematic sheet with snap grid,
    /// orthogonal pin-to-pin wiring, IEEE schematic symbols, cross-probing metadata,
    /// and optional live DC operating-point voltage/current annotations (ARCH-SCHEM-005).
    /// </summary>
    public static string ExportSvg(SchematicDocument document, OperatingPointOverlay? opOverlay = null)
    {
        var sheet = document.RootSheet;
        var grid = opOverlay is { GridSize: > 2.0 } ? opOverlay.GridSize : 20.0;
        var showGrid = opOverlay?.ShowGrid ?? true;
        var showOp = opOverlay is not null && opOverlay.ShowOperatingPoint;

        double minX = 0.0, minY = 0.0, maxX = 440.0, maxY = 280.0;
        foreach (var comp in sheet.Components)
        {
            minX = Math.Min(minX, comp.X - 80.0);
            minY = Math.Min(minY, comp.Y - 60.0);
            maxX = Math.Max(maxX, comp.X + 140.0);
            maxY = Math.Max(maxY, comp.Y + 90.0);
        }
        // Snap viewBox bounds cleanly to grid multiples.
        minX = Math.Floor(minX / grid) * grid;
        minY = Math.Floor(minY / grid) * grid;
        maxX = Math.Ceiling(maxX / grid) * grid;
        maxY = Math.Ceiling(maxY / grid) * grid;

        var w = maxX - minX;
        var h = maxY - minY;
        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{minX:F1} {minY:F1} {w:F1} {h:F1}\" data-grid=\"{grid:F1}\">"));
        sb.AppendLine($"  <title>{EscapeXml(document.Name)} - {EscapeXml(sheet.Title)}</title>");

        // Background + snap grid pattern (minor + major grid lines).
        sb.AppendLine("  <defs>");
        var major = grid * 5.0;
        sb.AppendLine(FormattableString.Invariant(
            $"    <pattern id=\"spice-minor-grid\" width=\"{grid:F1}\" height=\"{grid:F1}\" patternUnits=\"userSpaceOnUse\">"));
        sb.AppendLine(FormattableString.Invariant(
            $"      <path d=\"M {grid:F1} 0 L 0 0 0 {grid:F1}\" fill=\"none\" stroke=\"#1e293b\" stroke-width=\"0.7\" />"));
        sb.AppendLine("      <circle cx=\"0\" cy=\"0\" r=\"1.0\" fill=\"#334155\" />");
        sb.AppendLine("    </pattern>");
        sb.AppendLine(FormattableString.Invariant(
            $"    <pattern id=\"spice-major-grid\" width=\"{major:F1}\" height=\"{major:F1}\" patternUnits=\"userSpaceOnUse\">"));
        sb.AppendLine(FormattableString.Invariant(
            $"      <rect width=\"{major:F1}\" height=\"{major:F1}\" fill=\"url(#spice-minor-grid)\" />"));
        sb.AppendLine(FormattableString.Invariant(
            $"      <path d=\"M {major:F1} 0 L 0 0 0 {major:F1}\" fill=\"none\" stroke=\"#263449\" stroke-width=\"1.1\" />"));
        sb.AppendLine("    </pattern>");
        sb.AppendLine("  </defs>");
        sb.AppendLine(FormattableString.Invariant(
            $"  <rect x=\"{minX:F1}\" y=\"{minY:F1}\" width=\"{w:F1}\" height=\"{h:F1}\" fill=\"#0b1120\" />"));
        if (showGrid)
        {
            sb.AppendLine(FormattableString.Invariant(
                $"  <rect class=\"spice-snap-grid\" x=\"{minX:F1}\" y=\"{minY:F1}\" width=\"{w:F1}\" height=\"{h:F1}\" fill=\"url(#spice-major-grid)\" />"));
        }

        // Dynamic in-situ snap alignment guide lines & rubber-band wire preview layer (used by interactive editor).
        sb.AppendLine(FormattableString.Invariant(
            $"  <line id=\"snap-v\" class=\"spice-snap-guide\" x1=\"0\" y1=\"{minY:F1}\" x2=\"0\" y2=\"{maxY:F1}\" stroke=\"#22d3ee\" stroke-width=\"1\" stroke-dasharray=\"4 4\" visibility=\"hidden\" />"));
        sb.AppendLine(FormattableString.Invariant(
            $"  <line id=\"snap-h\" class=\"spice-snap-guide\" x1=\"{minX:F1}\" y1=\"0\" x2=\"{maxX:F1}\" y2=\"0\" stroke=\"#22d3ee\" stroke-width=\"1\" stroke-dasharray=\"4 4\" visibility=\"hidden\" />"));
        sb.AppendLine("  <polyline id=\"wire-preview\" fill=\"none\" stroke=\"#f59e0b\" stroke-width=\"2\" stroke-dasharray=\"5 3\" visibility=\"hidden\" points=\"\" />");

        var compMap = sheet.Components.ToDictionary(c => c.InstanceId, StringComparer.Ordinal);

        // Draw nets using snapped orthogonal (Manhattan) pin-to-pin wiring + DC voltage badges.
        foreach (var net in sheet.Nets)
        {
            var pinPts = new List<(double X, double Y)>();
            foreach (var pr in net.ConnectedPins)
            {
                if (compMap.TryGetValue(pr.InstanceId, out var c))
                {
                    pinPts.Add(GetPinWorldPosition(c, pr.PinName));
                }
            }

            var effectiveNet = net.IsGlobalGround || string.Equals(net.NetName, "0", StringComparison.Ordinal)
                ? "0"
                : net.NetName.ToLowerInvariant();
            var signalAttr = effectiveNet == "0" ? "" : $"v({effectiveNet})";
            var stroke = net.IsGlobalGround ? "#22c55e" : "#38bdf8";

            sb.AppendLine($"  <g class=\"spice-net\" data-net=\"{EscapeXml(net.NetName)}\" data-signal=\"{EscapeXml(signalAttr)}\">");
            if (pinPts.Count >= 2)
            {
                // Route from anchor pin to each connected pin via grid-snapped Manhattan elbow.
                var anchor = pinPts[0];
                for (int i = 1; i < pinPts.Count; ++i)
                {
                    var target = pinPts[i];
                    var midX = Math.Round((0.5 * (anchor.X + target.X)) / grid) * grid;
                    string ptsStr;
                    if (Math.Abs(anchor.Y - target.Y) < 0.5 || Math.Abs(anchor.X - target.X) < 0.5)
                    {
                        ptsStr = FormattableString.Invariant($"{anchor.X:F1},{anchor.Y:F1} {target.X:F1},{target.Y:F1}");
                    }
                    else
                    {
                        ptsStr = FormattableString.Invariant(
                            $"{anchor.X:F1},{anchor.Y:F1} {midX:F1},{anchor.Y:F1} {midX:F1},{target.Y:F1} {target.X:F1},{target.Y:F1}");
                    }
                    sb.AppendLine($"    <polyline points=\"{ptsStr}\" fill=\"none\" stroke=\"transparent\" stroke-width=\"12\" />");
                    sb.AppendLine($"    <polyline class=\"spice-wire-path\" points=\"{ptsStr}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"2.2\" stroke-linejoin=\"round\" />");
                }
                if (pinPts.Count >= 3)
                {
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <circle cx=\"{anchor.X:F1}\" cy=\"{anchor.Y:F1}\" r=\"3.5\" fill=\"{stroke}\" />"));
                }
            }

            if (pinPts.Count >= 1)
            {
                var lx = pinPts.Count >= 2 ? 0.5 * (pinPts[0].X + pinPts[1].X) : pinPts[0].X;
                var ly = pinPts.Count >= 2 ? Math.Min(pinPts[0].Y, pinPts[1].Y) - 10.0 : pinPts[0].Y - 12.0;

                if (net.IsGlobalGround)
                {
                    // Draw crisp IEEE ground symbol at the lowest ground pin.
                    var gx = pinPts[0].X;
                    var gy = pinPts.Max(p => p.Y) + 12.0;
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <line x1=\"{gx:F1}\" y1=\"{pinPts[0].Y:F1}\" x2=\"{gx:F1}\" y2=\"{gy:F1}\" stroke=\"#22c55e\" stroke-width=\"2\" />"));
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <line x1=\"{gx - 10:F1}\" y1=\"{gy:F1}\" x2=\"{gx + 10:F1}\" y2=\"{gy:F1}\" stroke=\"#22c55e\" stroke-width=\"2\" />"));
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <line x1=\"{gx - 6:F1}\" y1=\"{gy + 4:F1}\" x2=\"{gx + 6:F1}\" y2=\"{gy + 4:F1}\" stroke=\"#22c55e\" stroke-width=\"2\" />"));
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <line x1=\"{gx - 2.5:F1}\" y1=\"{gy + 8:F1}\" x2=\"{gx + 2.5:F1}\" y2=\"{gy + 8:F1}\" stroke=\"#22c55e\" stroke-width=\"2\" />"));
                }
                else
                {
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <text x=\"{lx:F1}\" y=\"{ly:F1}\" text-anchor=\"middle\" fill=\"#7dd3fc\" font-family=\"monospace\" font-size=\"10\" font-weight=\"bold\">{EscapeXml(net.NetName)}</text>"));
                    if (showOp && opOverlay!.NodeVoltages.TryGetValue(effectiveNet, out var vdc))
                    {
                        var vLabel = FormatEngineering(vdc, "V");
                        var pillW = Math.Max(46.0, vLabel.Length * 6.4 + 10.0);
                        sb.AppendLine($"    <g class=\"spice-op-v\" data-op-signal=\"{EscapeXml(signalAttr)}\">");
                        sb.AppendLine(FormattableString.Invariant(
                            $"      <rect x=\"{lx - pillW * 0.5:F1}\" y=\"{ly - 25.0:F1}\" width=\"{pillW:F1}\" height=\"15\" rx=\"4\" fill=\"#064e3b\" stroke=\"#34d399\" stroke-width=\"1\" />"));
                        sb.AppendLine(FormattableString.Invariant(
                            $"      <text x=\"{lx:F1}\" y=\"{ly - 14.5:F1}\" text-anchor=\"middle\" fill=\"#a7f3d0\" font-family=\"monospace\" font-size=\"9.5\" font-weight=\"bold\">{EscapeXml(vLabel)}</text>"));
                        sb.AppendLine("    </g>");
                    }
                }
            }
            sb.AppendLine("  </g>");
        }

        // Draw placed components with IEEE symbols, interactive pins, and DC branch current overlays.
        foreach (var comp in sheet.Components)
        {
            var currentSig = comp.DeviceKindId is "spice.vsrc" or "spice.inductor"
                ? $"i({comp.InstanceId})"
                : "";
            sb.AppendLine(FormattableString.Invariant(
                $"  <g class=\"spice-component\" data-instance=\"{EscapeXml(comp.InstanceId)}\" data-kind=\"{EscapeXml(comp.DeviceKindId)}\" data-signal=\"{EscapeXml(currentSig)}\" data-x=\"{comp.X:F1}\" data-y=\"{comp.Y:F1}\" data-rot=\"{comp.RotationDegrees}\" transform=\"translate({comp.X:F1},{comp.Y:F1})\">"));

            // Rotatable symbol group
            sb.AppendLine(FormattableString.Invariant($"    <g transform=\"rotate({comp.RotationDegrees})\">"));
            AppendComponentSymbol(sb, comp.DeviceKindId);
            foreach (var (pinName, px, py) in GetLocalPins(comp.DeviceKindId))
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"      <circle class=\"spice-pin\" data-instance=\"{EscapeXml(comp.InstanceId)}\" data-pin=\"{EscapeXml(pinName)}\" cx=\"{px:F1}\" cy=\"{py:F1}\" r=\"4\" fill=\"#0f172a\" stroke=\"#f8fafc\" stroke-width=\"1.5\" />"));
            }
            sb.AppendLine("    </g>");

            // Unrotated crisp text labels (reference designator + value)
            sb.AppendLine($"    <text x=\"0\" y=\"-24\" text-anchor=\"middle\" fill=\"#f8fafc\" font-family=\"monospace\" font-size=\"11\" font-weight=\"bold\">{EscapeXml(comp.InstanceId)}</text>");
            var valText = FormatPrimaryParameter(comp);
            if (!string.IsNullOrEmpty(valText))
            {
                sb.AppendLine($"    <text x=\"0\" y=\"30\" text-anchor=\"middle\" fill=\"#94a3b8\" font-family=\"monospace\" font-size=\"9.5\">{EscapeXml(valText)}</text>");
            }

            // DC branch current badge + directional flow arrow
            if (showOp && opOverlay!.BranchCurrents.TryGetValue(comp.InstanceId, out var idc))
            {
                var arrow = Math.Abs(idc) < 1e-12 ? "•" : (idc >= 0.0 ? "→" : "←");
                var iLabel = $"{arrow} {FormatEngineering(Math.Abs(idc), "A")}";
                var pillW = Math.Max(54.0, iLabel.Length * 6.2 + 10.0);
                sb.AppendLine($"    <g class=\"spice-op-i\" data-op-signal=\"i({EscapeXml(comp.InstanceId)})\">");
                sb.AppendLine(FormattableString.Invariant(
                    $"      <rect x=\"{-pillW * 0.5:F1}\" y=\"35\" width=\"{pillW:F1}\" height=\"15\" rx=\"4\" fill=\"#451a03\" stroke=\"#f59e0b\" stroke-width=\"1\" />"));
                sb.AppendLine($"      <text x=\"0\" y=\"45.5\" text-anchor=\"middle\" fill=\"#fde68a\" font-family=\"monospace\" font-size=\"9.5\" font-weight=\"bold\">{EscapeXml(iLabel)}</text>");
                sb.AppendLine("    </g>");
            }

            sb.AppendLine("  </g>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static IReadOnlyList<(string Pin, double X, double Y)> GetLocalPins(string kindId) => kindId switch
    {
        "spice.mosfet" => [("g", -36, 0), ("d", 20, -24), ("s", 20, 24), ("b", 36, 0)],
        "spice.bjt" => [("b", -36, 0), ("c", 20, -24), ("e", 20, 24)],
        "spice.jfet" => [("g", -36, 0), ("d", 20, -24), ("s", 20, 24)],
        _ => [("p", -36, 0), ("n", 36, 0)]
    };

    private static (double X, double Y) GetPinWorldPosition(PlacedComponent comp, string pinName)
    {
        var pins = GetLocalPins(comp.DeviceKindId);
        var match = pins.FirstOrDefault(p => string.Equals(p.Pin, pinName, StringComparison.OrdinalIgnoreCase));
        var lx = match.Pin is null ? 0.0 : match.X;
        var ly = match.Pin is null ? 0.0 : match.Y;
        var rad = comp.RotationDegrees * (Math.PI / 180.0);
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        return (comp.X + lx * cos - ly * sin, comp.Y + lx * sin + ly * cos);
    }

    private static string FormatPrimaryParameter(PlacedComponent comp)
    {
        if (comp.Parameters.TryGetValue("ohms", out var r)) return FormatEngineering(r, "Ω");
        if (comp.Parameters.TryGetValue("farads", out var c)) return FormatEngineering(c, "F");
        if (comp.Parameters.TryGetValue("henries", out var l)) return FormatEngineering(l, "H");
        if (comp.Parameters.TryGetValue("dc_volts", out var v)) return FormatEngineering(v, "V");
        if (comp.Parameters.TryGetValue("dc_amps", out var a)) return FormatEngineering(a, "A");
        if (comp.Parameters.Count > 0)
        {
            var first = comp.Parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal).First();
            return FormattableString.Invariant($"{first.Key}={first.Value:G4}");
        }
        return "";
    }

    private static void AppendComponentSymbol(StringBuilder sb, string kindId)
    {
        // Transparent hit box for smooth dragging/clicking
        sb.AppendLine("      <rect x=\"-34\" y=\"-20\" width=\"68\" height=\"40\" rx=\"5\" fill=\"#111827\" fill-opacity=\"0.78\" stroke=\"#334155\" stroke-width=\"1\" />");
        switch (kindId)
        {
            case "spice.resistor":
                sb.AppendLine("      <polyline points=\"-36,0 -22,0 -18,-9 -10,9 -2,-9 6,9 14,-9 20,0 36,0\" fill=\"none\" stroke=\"#e2e8f0\" stroke-width=\"2\" stroke-linejoin=\"round\" />");
                break;
            case "spice.capacitor":
                sb.AppendLine("      <line x1=\"-36\" y1=\"0\" x2=\"-5\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                sb.AppendLine("      <line x1=\"-5\" y1=\"-13\" x2=\"-5\" y2=\"13\" stroke=\"#e2e8f0\" stroke-width=\"2.4\" />");
                sb.AppendLine("      <line x1=\"5\" y1=\"-13\" x2=\"5\" y2=\"13\" stroke=\"#e2e8f0\" stroke-width=\"2.4\" />");
                sb.AppendLine("      <line x1=\"5\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                break;
            case "spice.inductor":
                sb.AppendLine("      <path d=\"M -36 0 L -20 0 A 5 5 0 0 1 -10 0 A 5 5 0 0 1 0 0 A 5 5 0 0 1 10 0 A 5 5 0 0 1 20 0 L 36 0\" fill=\"none\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                break;
            case "spice.vsrc":
                sb.AppendLine("      <line x1=\"-36\" y1=\"0\" x2=\"-16\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                sb.AppendLine("      <line x1=\"16\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                sb.AppendLine("      <circle cx=\"0\" cy=\"0\" r=\"16\" fill=\"#0f172a\" stroke=\"#38bdf8\" stroke-width=\"2\" />");
                sb.AppendLine("      <text x=\"-7\" y=\"4\" text-anchor=\"middle\" fill=\"#f8fafc\" font-family=\"monospace\" font-size=\"12\" font-weight=\"bold\">+</text>");
                sb.AppendLine("      <text x=\"7\" y=\"4\" text-anchor=\"middle\" fill=\"#94a3b8\" font-family=\"monospace\" font-size=\"12\" font-weight=\"bold\">-</text>");
                break;
            case "spice.isrc":
                sb.AppendLine("      <line x1=\"-36\" y1=\"0\" x2=\"-16\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                sb.AppendLine("      <line x1=\"16\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                sb.AppendLine("      <circle cx=\"0\" cy=\"0\" r=\"16\" fill=\"#0f172a\" stroke=\"#a855f7\" stroke-width=\"2\" />");
                sb.AppendLine("      <line x1=\"-8\" y1=\"0\" x2=\"8\" y2=\"0\" stroke=\"#f8fafc\" stroke-width=\"2\" />");
                sb.AppendLine("      <polyline points=\"3,-5 9,0 3,5\" fill=\"none\" stroke=\"#f8fafc\" stroke-width=\"2\" />");
                break;
            case "spice.diode":
                sb.AppendLine("      <line x1=\"-36\" y1=\"0\" x2=\"-10\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                sb.AppendLine("      <polygon points=\"-10,-11 -10,11 10,0\" fill=\"#1e293b\" stroke=\"#f43f5e\" stroke-width=\"2\" />");
                sb.AppendLine("      <line x1=\"10\" y1=\"-11\" x2=\"10\" y2=\"11\" stroke=\"#f43f5e\" stroke-width=\"2.4\" />");
                sb.AppendLine("      <line x1=\"10\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                break;
            default:
                sb.AppendLine("      <line x1=\"-36\" y1=\"0\" x2=\"-24\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                sb.AppendLine("      <line x1=\"24\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"#e2e8f0\" stroke-width=\"2\" />");
                sb.AppendLine("      <rect x=\"-24\" y=\"-14\" width=\"48\" height=\"28\" rx=\"4\" fill=\"#1e293b\" stroke=\"#e2e8f0\" stroke-width=\"1.8\" />");
                break;
        }
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
