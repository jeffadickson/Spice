using System.Security.Cryptography;
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
    string? SubjectId = null,
    double? X = null,
    double? Y = null);

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
/// Electrical Rules Check (ERC), deterministic Canonical IR emission, versioned JSON
/// persistence (with SHA-256 integrity &amp; 2.0-&gt;2.1 migration), custom library component
/// resolution, SPICE export, and vector SVG export for Spice.Schematic
/// (ARCH-SCHEM-001 .. ARCH-SCHEM-005).
/// </summary>
public static class SchematicCompiler
{
    public const string CurrentFormatId = "spice.schematic.v2";
    public const string CurrentSchemaVersion = "2.1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static IReadOnlyList<ErcDiagnostic> RunErc(
        SchematicDocument document,
        DeviceRegistry? registry = null,
        ComponentLibraryStore? library = null)
    {
        var reg = registry ?? DeviceRegistry.Default;
        var diagnostics = new List<ErcDiagnostic>();

        if (string.IsNullOrWhiteSpace(document.SchemaVersion))
        {
            diagnostics.Add(new ErcDiagnostic(ErcSeverity.Error, "missing_schema_version", "Schematic SchemaVersion is required."));
        }
        else if (document.SchemaVersion is not ("2.0" or "2.1"))
        {
            diagnostics.Add(new ErcDiagnostic(
                ErcSeverity.Error,
                "unsupported_schema_version",
                $"Unsupported schematic SchemaVersion '{document.SchemaVersion}'. Supported versions: 2.0, 2.1."));
        }

        ValidateSheet(document, document.RootSheet, reg, library, diagnostics, requireGround: true);
        if (document.SubSheets is not null)
        {
            foreach (var sub in document.SubSheets)
            {
                ValidateSheet(document, sub, reg, library, diagnostics, requireGround: false);
            }
        }

        return diagnostics;
    }

    public static SchematicCompileResult CompileToIr(
        SchematicDocument document,
        DeviceRegistry? registry = null,
        ComponentLibraryStore? library = null)
    {
        var reg = registry ?? DeviceRegistry.Default;
        var diagnostics = RunErc(document, reg, library);
        if (diagnostics.Any(d => d.Severity == ErcSeverity.Error))
        {
            return new SchematicCompileResult(false, null, diagnostics);
        }

        var nodes = new HashSet<string>(StringComparer.Ordinal) { "0" };
        var devices = new List<DeviceInstanceIr>();
        var models = new Dictionary<string, ModelDefinitionIr>(StringComparer.OrdinalIgnoreCase);
        var directives = new List<string>();

        if (document.EmbeddedModels is not null)
        {
            foreach (var em in document.EmbeddedModels)
            {
                AddAttachedModel(models, em);
            }
        }

        EmitSheetInto(document, document.RootSheet, prefix: "", nodes, devices, models, directives, library);
        if (document.SubSheets is not null)
        {
            foreach (var sub in document.SubSheets)
            {
                EmitSheetInto(document, sub, prefix: $"{sub.SheetId}.", nodes, devices, models, directives, library);
            }
        }

        var orderedNodes = nodes
            .OrderBy(n => n == "0" ? "" : n, StringComparer.Ordinal)
            .ToList();

        var provenance = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "Spice.Schematic",
            ["format_id"] = string.IsNullOrWhiteSpace(document.FormatId) ? CurrentFormatId : document.FormatId,
            ["schema_version"] = document.SchemaVersion,
            ["schematic_name"] = document.Name
        };

        var circuit = new CircuitIr(
            document.Name,
            orderedNodes,
            devices,
            provenance,
            models.Count > 0 ? models.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList() : null,
            directives.Count > 0 ? directives : null);

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

    /// <summary>
    /// Attaches referenced library parts and models directly into the document so saved
    /// .spice.json files are self-contained and Git-portable across machines.
    /// </summary>
    public static SchematicDocument EmbedReferencedLibraryAssets(
        SchematicDocument document,
        ComponentLibraryStore? library)
    {
        if (library is null) return document;

        var parts = new Dictionary<string, CustomComponentDefinition>(StringComparer.OrdinalIgnoreCase);
        if (document.EmbeddedParts is not null)
        {
            foreach (var p in document.EmbeddedParts) parts[p.Id] = p;
        }
        var models = new Dictionary<string, AttachedModelDefinition>(StringComparer.OrdinalIgnoreCase);
        if (document.EmbeddedModels is not null)
        {
            foreach (var m in document.EmbeddedModels) models[m.ModelName] = m;
        }

        foreach (var comp in document.RootSheet.Components)
        {
            if (!string.IsNullOrWhiteSpace(comp.LibraryPartId) &&
                library.TryGet(comp.LibraryPartId, out var part) && part is not null)
            {
                parts[part.Id] = part;
                if (part.AttachedModel is not null)
                {
                    models[part.AttachedModel.ModelName] = part.AttachedModel;
                }
            }
            else if (!string.IsNullOrWhiteSpace(comp.ModelName) &&
                     !models.ContainsKey(comp.ModelName) &&
                     library.TryFindByModelName(comp.ModelName, out var byModel) &&
                     byModel?.AttachedModel is not null)
            {
                models[byModel.AttachedModel.ModelName] = byModel.AttachedModel;
            }
        }

        return document with
        {
            EmbeddedParts = parts.Count > 0 ? parts.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList() : null,
            EmbeddedModels = models.Count > 0 ? models.Values.OrderBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase).ToList() : null
        };
    }

    /// <summary>
    /// Deterministic, versioned, Git-diffable JSON persistence (.spice.json) with SHA-256
    /// integrity checksum computed over canonical payload (ARCH-SCHEM-005).
    /// </summary>
    public static string SerializeJson(SchematicDocument document, ComponentLibraryStore? library = null)
    {
        var enriched = EmbedReferencedLibraryAssets(document, library);
        var baseMeta = enriched.Metadata ?? new SchematicMetadata();
        var canonicalDoc = enriched with
        {
            FormatId = CurrentFormatId,
            SchemaVersion = string.IsNullOrWhiteSpace(enriched.SchemaVersion) ? CurrentSchemaVersion : enriched.SchemaVersion,
            Metadata = baseMeta with { ContentSha256 = null }
        };
        var payloadForHash = JsonSerializer.Serialize(canonicalDoc, JsonOptions);
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadForHash)));
        var stamped = canonicalDoc with
        {
            Metadata = baseMeta with { ContentSha256 = sha }
        };
        return JsonSerializer.Serialize(stamped, JsonOptions);
    }

    /// <summary>
    /// Fail-closed deserialization of versioned .spice.json schematics with automatic
    /// 2.0 -&gt; 2.1 schema migration and optional SHA-256 integrity verification.
    /// </summary>
    public static SchematicDocument DeserializeJson(string json, bool verifyChecksum = false)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Schematic JSON is empty.");
        }

        var doc = JsonSerializer.Deserialize<SchematicDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize SchematicDocument JSON.");

        if (doc.RootSheet is null)
        {
            throw new InvalidDataException("SchematicDocument is missing rootSheet.");
        }

        if (string.IsNullOrWhiteSpace(doc.SchemaVersion) || doc.SchemaVersion is not ("2.0" or "2.1"))
        {
            throw new InvalidDataException(
                $"Unsupported schematic SchemaVersion '{doc.SchemaVersion}'. Supported versions: 2.0, 2.1.");
        }

        if (verifyChecksum && !string.IsNullOrWhiteSpace(doc.Metadata?.ContentSha256))
        {
            var expected = doc.Metadata.ContentSha256;
            var stripped = doc with { Metadata = doc.Metadata with { ContentSha256 = null } };
            var canonical = JsonSerializer.Serialize(stripped, JsonOptions);
            var actual = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Schematic SHA-256 checksum mismatch: expected '{expected}', computed '{actual}'.");
            }
        }

        // Normalize 2.0 -> 2.1 transparently on load while preserving user metadata.
        return doc with
        {
            FormatId = string.IsNullOrWhiteSpace(doc.FormatId) ? CurrentFormatId : doc.FormatId,
            SchemaVersion = CurrentSchemaVersion
        };
    }

    public static string ExportSpiceNetlist(
        SchematicDocument document,
        AnalysisKind analysis = AnalysisKind.OperatingPoint,
        IReadOnlyDictionary<string, double>? analysisOptions = null,
        DeviceRegistry? registry = null,
        ComponentLibraryStore? library = null)
    {
        return SchematicExporter.ExportNetlist(
            document,
            new SchematicExportOptions(
                Dialect: NetlistDialect.CanonicalSpice3f5,
                Analysis: analysis,
                AnalysisOptions: analysisOptions),
            registry,
            library);
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
    /// convention p -&gt; n) from a solved OperatingPoint ResultManifestIr.
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
    /// Deterministic vector SVG export of the root schematic sheet with configurable snap grid
    /// (lines/dots/off), alignment snap guides, orthogonal pin-to-pin wiring, IEEE schematic
    /// symbols (including MOSFET/BJT/JFET/Schottky/Op-Amp/custom IC subcircuits), annotations,
    /// hierarchical ports, cross-probing metadata, dark/light themes, and live DC operating-point
    /// voltage/current annotations (ARCH-SCHEM-005).
    /// </summary>
    public static string ExportSvg(
        SchematicDocument document,
        OperatingPointOverlay? opOverlay = null,
        ComponentLibraryStore? library = null,
        bool lightTheme = false,
        string? gridStyle = null)
    {
        var sheet = document.RootSheet;
        var grid = opOverlay is { GridSize: > 2.0 } ? opOverlay.GridSize : (sheet.GridPitch > 2.0 ? sheet.GridPitch : 20.0);
        var effectiveGridStyle = (gridStyle ?? sheet.GridStyle ?? "lines").ToLowerInvariant();
        if (opOverlay is { ShowGrid: false }) effectiveGridStyle = "off";
        var showOp = opOverlay is not null && opOverlay.ShowOperatingPoint;

        // Palette tokens (dark CAD studio vs clean light print theme)
        var bg = lightTheme ? "#ffffff" : "#0b1120";
        var minorCol = lightTheme ? "#e2e8f0" : "#1e293b";
        var majorCol = lightTheme ? "#cbd5e1" : "#263449";
        var dotCol = lightTheme ? "#94a3b8" : "#334155";
        var wireCol = lightTheme ? "#0284c7" : "#38bdf8";
        var gndCol = lightTheme ? "#15803d" : "#22c55e";
        var symStroke = lightTheme ? "#0f172a" : "#e2e8f0";
        var symFill = lightTheme ? "#f8fafc" : "#111827";
        var symBorder = lightTheme ? "#cbd5e1" : "#334155";
        var textPrimary = lightTheme ? "#0f172a" : "#f8fafc";
        var textMuted = lightTheme ? "#475569" : "#94a3b8";
        var netText = lightTheme ? "#0369a1" : "#7dd3fc";
        var pinFill = lightTheme ? "#ffffff" : "#0f172a";

        double minX = 0.0, minY = 0.0, maxX = 520.0, maxY = 320.0;
        foreach (var comp in sheet.Components)
        {
            minX = Math.Min(minX, comp.X - 90.0);
            minY = Math.Min(minY, comp.Y - 70.0);
            maxX = Math.Max(maxX, comp.X + 140.0);
            maxY = Math.Max(maxY, comp.Y + 100.0);
        }
        if (sheet.Annotations is not null)
        {
            foreach (var ann in sheet.Annotations)
            {
                minX = Math.Min(minX, ann.X - 20.0);
                minY = Math.Min(minY, ann.Y - 20.0);
                maxX = Math.Max(maxX, ann.X + 200.0);
                maxY = Math.Max(maxY, ann.Y + 40.0);
            }
        }
        if (sheet.Ports is not null)
        {
            foreach (var port in sheet.Ports)
            {
                minX = Math.Min(minX, port.X - 40.0);
                minY = Math.Min(minY, port.Y - 30.0);
                maxX = Math.Max(maxX, port.X + 80.0);
                maxY = Math.Max(maxY, port.Y + 30.0);
            }
        }

        minX = Math.Floor(minX / grid) * grid;
        minY = Math.Floor(minY / grid) * grid;
        maxX = Math.Ceiling(maxX / grid) * grid;
        maxY = Math.Ceiling(maxY / grid) * grid;

        var w = maxX - minX;
        var h = maxY - minY;
        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{minX:F1} {minY:F1} {w:F1} {h:F1}\" data-grid=\"{grid:F1}\" data-grid-style=\"{EscapeXml(effectiveGridStyle)}\">"));
        sb.AppendLine($"  <title>{EscapeXml(document.Name)} - {EscapeXml(sheet.Title)}</title>");

        // Background + snap grid pattern (lines or dots)
        sb.AppendLine("  <defs>");
        var major = grid * 5.0;
        sb.AppendLine(FormattableString.Invariant(
            $"    <pattern id=\"spice-minor-grid\" width=\"{grid:F1}\" height=\"{grid:F1}\" patternUnits=\"userSpaceOnUse\">"));
        if (effectiveGridStyle == "dots")
        {
            sb.AppendLine($"      <circle cx=\"0\" cy=\"0\" r=\"1.2\" fill=\"{dotCol}\" />");
        }
        else
        {
            sb.AppendLine(FormattableString.Invariant(
                $"      <path d=\"M {grid:F1} 0 L 0 0 0 {grid:F1}\" fill=\"none\" stroke=\"{minorCol}\" stroke-width=\"0.7\" />"));
            sb.AppendLine($"      <circle cx=\"0\" cy=\"0\" r=\"0.9\" fill=\"{dotCol}\" />");
        }
        sb.AppendLine("    </pattern>");
        sb.AppendLine(FormattableString.Invariant(
            $"    <pattern id=\"spice-major-grid\" width=\"{major:F1}\" height=\"{major:F1}\" patternUnits=\"userSpaceOnUse\">"));
        sb.AppendLine(FormattableString.Invariant(
            $"      <rect width=\"{major:F1}\" height=\"{major:F1}\" fill=\"url(#spice-minor-grid)\" />"));
        if (effectiveGridStyle != "dots")
        {
            sb.AppendLine(FormattableString.Invariant(
                $"      <path d=\"M {major:F1} 0 L 0 0 0 {major:F1}\" fill=\"none\" stroke=\"{majorCol}\" stroke-width=\"1.1\" />"));
        }
        else
        {
            sb.AppendLine($"      <circle cx=\"0\" cy=\"0\" r=\"1.8\" fill=\"{majorCol}\" />");
        }
        sb.AppendLine("    </pattern>");
        sb.AppendLine("  </defs>");
        sb.AppendLine(FormattableString.Invariant(
            $"  <rect x=\"{minX:F1}\" y=\"{minY:F1}\" width=\"{w:F1}\" height=\"{h:F1}\" fill=\"{bg}\" />"));
        if (effectiveGridStyle != "off")
        {
            sb.AppendLine(FormattableString.Invariant(
                $"  <rect class=\"spice-snap-grid\" x=\"{minX:F1}\" y=\"{minY:F1}\" width=\"{w:F1}\" height=\"{h:F1}\" fill=\"url(#spice-major-grid)\" />"));
        }

        // Dynamic in-situ snap alignment guide lines & rubber-band wire preview layer
        sb.AppendLine(FormattableString.Invariant(
            $"  <line id=\"snap-v\" class=\"spice-snap-guide\" x1=\"0\" y1=\"{minY:F1}\" x2=\"0\" y2=\"{maxY:F1}\" stroke=\"#22d3ee\" stroke-width=\"1.2\" stroke-dasharray=\"4 4\" visibility=\"hidden\" />"));
        sb.AppendLine(FormattableString.Invariant(
            $"  <line id=\"snap-h\" class=\"spice-snap-guide\" x1=\"{minX:F1}\" y1=\"0\" x2=\"{maxX:F1}\" y2=\"0\" stroke=\"#22d3ee\" stroke-width=\"1.2\" stroke-dasharray=\"4 4\" visibility=\"hidden\" />"));
        sb.AppendLine("  <polyline id=\"wire-preview\" fill=\"none\" stroke=\"#f59e0b\" stroke-width=\"2.2\" stroke-dasharray=\"5 3\" visibility=\"hidden\" points=\"\" />");

        var compMap = sheet.Components.ToDictionary(c => c.InstanceId, StringComparer.Ordinal);

        // Draw nets using snapped orthogonal (Manhattan) pin-to-pin wiring + DC voltage badges
        foreach (var net in sheet.Nets)
        {
            var pinPts = new List<(double X, double Y)>();
            foreach (var pr in net.ConnectedPins)
            {
                if (compMap.TryGetValue(pr.InstanceId, out var c))
                {
                    var part = ResolveCustomPart(document, c, library);
                    pinPts.Add(GetPinWorldPosition(c, pr.PinName, part));
                }
            }

            var effectiveNet = net.IsGlobalGround || string.Equals(net.NetName, "0", StringComparison.Ordinal)
                ? "0"
                : net.NetName.ToLowerInvariant();
            var signalAttr = effectiveNet == "0" ? "" : $"v({effectiveNet})";
            var stroke = net.IsGlobalGround ? gndCol : wireCol;

            sb.AppendLine($"  <g class=\"spice-net\" data-net=\"{EscapeXml(net.NetName)}\" data-signal=\"{EscapeXml(signalAttr)}\">");
            if (pinPts.Count >= 2)
            {
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
                        $"    <circle class=\"spice-junction\" cx=\"{anchor.X:F1}\" cy=\"{anchor.Y:F1}\" r=\"3.6\" fill=\"{stroke}\" />"));
                }
            }

            if (pinPts.Count >= 1)
            {
                var lx = pinPts.Count >= 2 ? 0.5 * (pinPts[0].X + pinPts[1].X) : pinPts[0].X;
                var ly = pinPts.Count >= 2 ? Math.Min(pinPts[0].Y, pinPts[1].Y) - 10.0 : pinPts[0].Y - 12.0;

                if (net.IsGlobalGround)
                {
                    var gx = pinPts[0].X;
                    var gy = pinPts.Max(p => p.Y) + 12.0;
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <line x1=\"{gx:F1}\" y1=\"{pinPts[0].Y:F1}\" x2=\"{gx:F1}\" y2=\"{gy:F1}\" stroke=\"{gndCol}\" stroke-width=\"2\" />"));
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <line x1=\"{gx - 10:F1}\" y1=\"{gy:F1}\" x2=\"{gx + 10:F1}\" y2=\"{gy:F1}\" stroke=\"{gndCol}\" stroke-width=\"2\" />"));
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <line x1=\"{gx - 6:F1}\" y1=\"{gy + 4:F1}\" x2=\"{gx + 6:F1}\" y2=\"{gy + 4:F1}\" stroke=\"{gndCol}\" stroke-width=\"2\" />"));
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <line x1=\"{gx - 2.5:F1}\" y1=\"{gy + 8:F1}\" x2=\"{gx + 2.5:F1}\" y2=\"{gy + 8:F1}\" stroke=\"{gndCol}\" stroke-width=\"2\" />"));
                }
                else
                {
                    sb.AppendLine(FormattableString.Invariant(
                        $"    <text x=\"{lx:F1}\" y=\"{ly:F1}\" text-anchor=\"middle\" fill=\"{netText}\" font-family=\"monospace\" font-size=\"10\" font-weight=\"bold\">{EscapeXml(net.NetName)}</text>"));
                    if (showOp && opOverlay!.NodeVoltages.TryGetValue(effectiveNet, out var vdc))
                    {
                        var vLabel = FormatEngineering(vdc, "V");
                        var pillW = Math.Max(48.0, vLabel.Length * 6.4 + 10.0);
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

        // Draw hierarchical sheet ports
        if (sheet.Ports is not null)
        {
            foreach (var port in sheet.Ports)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"  <g class=\"spice-port\" data-port=\"{EscapeXml(port.Name)}\" transform=\"translate({port.X:F1},{port.Y:F1})\">"));
                sb.AppendLine("    <polygon points=\"-28,-8 12,-8 22,0 12,8 -28,8\" fill=\"#1e1b4b\" stroke=\"#818cf8\" stroke-width=\"1.5\" />");
                sb.AppendLine($"    <text x=\"-6\" y=\"3.5\" text-anchor=\"middle\" fill=\"#c7d2fe\" font-family=\"monospace\" font-size=\"9.5\" font-weight=\"bold\">{EscapeXml(port.Name)}</text>");
                sb.AppendLine("  </g>");
            }
        }

        // Draw text annotations and SPICE directives
        if (sheet.Annotations is not null)
        {
            foreach (var ann in sheet.Annotations)
            {
                var fill = ann.IsSpiceDirective ? "#451a03" : symFill;
                var border = ann.IsSpiceDirective ? "#f59e0b" : symBorder;
                var txtCol = ann.IsSpiceDirective ? "#fde68a" : textMuted;
                var boxW = Math.Max(80.0, ann.Text.Length * 6.5 + 16.0);
                sb.AppendLine(FormattableString.Invariant(
                    $"  <g class=\"spice-annotation\" data-annotation=\"{EscapeXml(ann.Id)}\" data-directive=\"{(ann.IsSpiceDirective ? "true" : "false")}\" transform=\"translate({ann.X:F1},{ann.Y:F1})\">"));
                sb.AppendLine(FormattableString.Invariant(
                    $"    <rect x=\"0\" y=\"-12\" width=\"{boxW:F1}\" height=\"20\" rx=\"4\" fill=\"{fill}\" stroke=\"{border}\" stroke-width=\"1\" />"));
                sb.AppendLine($"    <text x=\"8\" y=\"1.5\" fill=\"{txtCol}\" font-family=\"monospace\" font-size=\"10\">{EscapeXml(ann.Text)}</text>");
                sb.AppendLine("  </g>");
            }
        }

        // Draw placed components with IEEE/custom symbols, interactive pins, and DC branch current overlays
        foreach (var comp in sheet.Components)
        {
            var customPart = ResolveCustomPart(document, comp, library);
            var localPins = GetLocalPins(comp, customPart);
            var currentSig = comp.DeviceKindId is "spice.vsrc" or "spice.inductor"
                ? $"i({comp.InstanceId})"
                : "";
            var sx = comp.MirrorX ? -1 : 1;
            var sy = comp.MirrorY ? -1 : 1;
            var symTransform = (comp.MirrorX || comp.MirrorY)
                ? FormattableString.Invariant($"scale({sx},{sy}) rotate({comp.RotationDegrees})")
                : FormattableString.Invariant($"rotate({comp.RotationDegrees})");

            sb.AppendLine(FormattableString.Invariant(
                $"  <g class=\"spice-component\" data-instance=\"{EscapeXml(comp.InstanceId)}\" data-kind=\"{EscapeXml(comp.DeviceKindId)}\" data-model=\"{EscapeXml(comp.ModelName ?? "")}\" data-signal=\"{EscapeXml(currentSig)}\" data-x=\"{comp.X:F1}\" data-y=\"{comp.Y:F1}\" data-rot=\"{comp.RotationDegrees}\" transform=\"translate({comp.X:F1},{comp.Y:F1})\">"));

            sb.AppendLine($"    <g transform=\"{symTransform}\">");
            AppendComponentSymbol(sb, comp, customPart, localPins, symStroke, symFill, symBorder, textMuted);
            foreach (var (pinName, px, py) in localPins)
            {
                sb.AppendLine(FormattableString.Invariant(
                    $"      <circle class=\"spice-pin\" data-instance=\"{EscapeXml(comp.InstanceId)}\" data-pin=\"{EscapeXml(pinName)}\" cx=\"{px:F1}\" cy=\"{py:F1}\" r=\"4.2\" fill=\"{pinFill}\" stroke=\"{symStroke}\" stroke-width=\"1.5\"><title>{EscapeXml(comp.InstanceId)}.{EscapeXml(pinName)}</title></circle>"));
            }
            sb.AppendLine("    </g>");

            // Unrotated crisp text labels (reference designator + value/model)
            sb.AppendLine($"    <text x=\"0\" y=\"-26\" text-anchor=\"middle\" fill=\"{textPrimary}\" font-family=\"monospace\" font-size=\"11\" font-weight=\"bold\">{EscapeXml(comp.InstanceId)}</text>");
            var valText = !string.IsNullOrWhiteSpace(comp.DisplayValue)
                ? comp.DisplayValue
                : (!string.IsNullOrWhiteSpace(comp.ModelName) ? comp.ModelName : FormatPrimaryParameter(comp));
            if (!string.IsNullOrEmpty(valText))
            {
                sb.AppendLine($"    <text x=\"0\" y=\"33\" text-anchor=\"middle\" fill=\"{textMuted}\" font-family=\"monospace\" font-size=\"9.5\">{EscapeXml(valText)}</text>");
            }

            // DC branch current badge + directional flow arrow
            if (showOp && opOverlay!.BranchCurrents.TryGetValue(comp.InstanceId, out var idc))
            {
                var arrow = Math.Abs(idc) < 1e-12 ? "•" : (idc >= 0.0 ? "→" : "←");
                var iLabel = $"{arrow} {FormatEngineering(Math.Abs(idc), "A")}";
                var pillW = Math.Max(56.0, iLabel.Length * 6.2 + 10.0);
                sb.AppendLine($"    <g class=\"spice-op-i\" data-op-signal=\"i({EscapeXml(comp.InstanceId)})\">");
                sb.AppendLine(FormattableString.Invariant(
                    $"      <rect x=\"{-pillW * 0.5:F1}\" y=\"38\" width=\"{pillW:F1}\" height=\"15\" rx=\"4\" fill=\"#451a03\" stroke=\"#f59e0b\" stroke-width=\"1\" />"));
                sb.AppendLine($"      <text x=\"0\" y=\"48.5\" text-anchor=\"middle\" fill=\"#fde68a\" font-family=\"monospace\" font-size=\"9.5\" font-weight=\"bold\">{EscapeXml(iLabel)}</text>");
                sb.AppendLine("    </g>");
            }

            sb.AppendLine("  </g>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Renders a standalone mini SVG preview of any built-in or custom component symbol for the
    /// Component Shelf and Library Studio.
    /// </summary>
    public static string ExportSymbolPreviewSvg(CustomComponentDefinition part)
    {
        var comp = new PlacedComponent(
            part.SpicePrefix + "1",
            part.BaseDeviceKindId,
            0, 0, 0,
            part.DefaultParameters,
            ModelName: part.AttachedModel?.ModelName,
            LibraryPartId: part.Id);
        var localPins = part.Pins.Select(p => (p.Name, p.LocalX, p.LocalY)).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"-52 -36 104 72\">");
        AppendComponentSymbol(sb, comp, part, localPins, "#e2e8f0", "#111827", "#334155", "#94a3b8");
        foreach (var (_, px, py) in localPins)
        {
            sb.AppendLine(FormattableString.Invariant(
                $"  <circle cx=\"{px:F1}\" cy=\"{py:F1}\" r=\"3\" fill=\"#0f172a\" stroke=\"#38bdf8\" stroke-width=\"1.4\" />"));
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static CustomComponentDefinition? ResolveCustomPart(
        SchematicDocument doc,
        PlacedComponent comp,
        ComponentLibraryStore? library)
    {
        if (!string.IsNullOrWhiteSpace(comp.LibraryPartId))
        {
            var embedded = doc.EmbeddedParts?.FirstOrDefault(p =>
                string.Equals(p.Id, comp.LibraryPartId, StringComparison.OrdinalIgnoreCase));
            if (embedded is not null) return embedded;
            if (library is not null && library.TryGet(comp.LibraryPartId, out var fromLib))
            {
                return fromLib;
            }
        }
        if (!string.IsNullOrWhiteSpace(comp.ModelName) && library is not null &&
            library.TryFindByModelName(comp.ModelName, out var byModel))
        {
            return byModel;
        }
        return null;
    }

    private static IReadOnlyList<(string Pin, double X, double Y)> GetLocalPins(
        PlacedComponent comp,
        CustomComponentDefinition? customPart)
    {
        if (customPart is { Pins.Count: > 0 })
        {
            return customPart.Pins.Select(p => (p.Name, p.LocalX, p.LocalY)).ToList();
        }
        return comp.DeviceKindId switch
        {
            "spice.mosfet" => [("d", 20, -20), ("g", -40, 0), ("s", 20, 20), ("b", 40, 0)],
            "spice.bjt" => [("c", 20, -20), ("b", -40, 0), ("e", 20, 20)],
            "spice.jfet" => [("d", 20, -20), ("g", -40, 0), ("s", 20, 20)],
            "spice.vcvs" or "spice.vccs" => [("p", 40, -20), ("n", 40, 20), ("cp", -40, -20), ("cn", -40, 20)],
            _ => [("p", -36, 0), ("n", 36, 0)]
        };
    }

    private static (double X, double Y) GetPinWorldPosition(
        PlacedComponent comp,
        string pinName,
        CustomComponentDefinition? customPart)
    {
        var pins = GetLocalPins(comp, customPart);
        var match = pins.FirstOrDefault(p => string.Equals(p.Pin, pinName, StringComparison.OrdinalIgnoreCase));
        var lx = match.Pin is null ? 0.0 : match.X;
        var ly = match.Pin is null ? 0.0 : match.Y;
        var rad = comp.RotationDegrees * (Math.PI / 180.0);
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        // Apply rotate then scale(sx, sy) to match SVG transform="scale(sx,sy) rotate(deg)"
        var rx = lx * cos - ly * sin;
        var ry = lx * sin + ly * cos;
        if (comp.MirrorX) rx = -rx;
        if (comp.MirrorY) ry = -ry;
        return (comp.X + rx, comp.Y + ry);
    }

    private static string FormatPrimaryParameter(PlacedComponent comp)
    {
        if (comp.Parameters.TryGetValue("ohms", out var r)) return FormatEngineering(r, "Ω");
        if (comp.Parameters.TryGetValue("farads", out var c)) return FormatEngineering(c, "F");
        if (comp.Parameters.TryGetValue("henries", out var l)) return FormatEngineering(l, "H");
        if (comp.Parameters.TryGetValue("dc_volts", out var v)) return FormatEngineering(v, "V");
        if (comp.Parameters.TryGetValue("dc_amps", out var a)) return FormatEngineering(a, "A");
        if (comp.Parameters.TryGetValue("gain", out var g)) return FormattableString.Invariant($"A={g:G4}");
        if (comp.Parameters.TryGetValue("siemens", out var gm)) return FormatEngineering(gm, "S");
        if (comp.Parameters.Count > 0)
        {
            var first = comp.Parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal).First();
            return FormattableString.Invariant($"{first.Key}={first.Value:G4}");
        }
        return "";
    }

    private static void AppendComponentSymbol(
        StringBuilder sb,
        PlacedComponent comp,
        CustomComponentDefinition? customPart,
        IReadOnlyList<(string Pin, double X, double Y)> localPins,
        string symStroke,
        string symFill,
        string symBorder,
        string textMuted)
    {
        var style = customPart?.SymbolStyle?.ToLowerInvariant() ?? comp.DeviceKindId switch
        {
            "spice.resistor" => "resistor",
            "spice.capacitor" => "capacitor",
            "spice.inductor" => "inductor",
            "spice.vsrc" => "vsrc",
            "spice.isrc" => "isrc",
            "spice.diode" => "diode",
            "spice.mosfet" => comp.Parameters.GetValueOrDefault("vth0_volts", 0.4) < 0 ? "pmos" : "nmos",
            "spice.bjt" => "npn",
            "spice.jfet" => "jfet",
            "spice.vcvs" => "vcvs",
            "spice.vccs" => "vccs",
            "spice.subckt" => "ic_box",
            _ => "ic_box"
        };

        if (customPart?.SymbolPrimitives is { Count: > 0 } primitives)
        {
            double bx0 = -36.0, by0 = -22.0, bx1 = 36.0, by1 = 22.0;
            foreach (var (_, px, py) in localPins)
            {
                bx0 = Math.Min(bx0, px - 4.0);
                by0 = Math.Min(by0, py - 4.0);
                bx1 = Math.Max(bx1, px + 4.0);
                by1 = Math.Max(by1, py + 4.0);
            }
            sb.AppendLine(FormattableString.Invariant(
                $"      <rect x=\"{bx0:F1}\" y=\"{by0:F1}\" width=\"{bx1 - bx0:F1}\" height=\"{by1 - by0:F1}\" rx=\"6\" fill=\"{symFill}\" fill-opacity=\"0.65\" stroke=\"{symBorder}\" stroke-width=\"1\" />"));
            sb.AppendLine("      <g class=\"spice-custom-symbol\">");
            foreach (var prim in primitives)
            {
                var stroke = string.IsNullOrWhiteSpace(prim.Stroke) ? symStroke : EscapeXml(prim.Stroke);
                var fill = string.IsNullOrWhiteSpace(prim.Fill) ? "none" : EscapeXml(prim.Fill);
                var sw = prim.StrokeWidth > 0.0 ? prim.StrokeWidth : 2.0;
                switch (prim.Kind.ToLowerInvariant())
                {
                    case "line":
                        sb.AppendLine(FormattableString.Invariant(
                            $"        <line x1=\"{prim.X1:F1}\" y1=\"{prim.Y1:F1}\" x2=\"{prim.X2:F1}\" y2=\"{prim.Y2:F1}\" stroke=\"{stroke}\" stroke-width=\"{sw:F1}\" stroke-linecap=\"round\" />"));
                        break;
                    case "rect":
                    {
                        var rx = Math.Min(prim.X1, prim.X2);
                        var ry = Math.Min(prim.Y1, prim.Y2);
                        var rw = Math.Abs(prim.X2 - prim.X1);
                        var rh = Math.Abs(prim.Y2 - prim.Y1);
                        sb.AppendLine(FormattableString.Invariant(
                            $"        <rect x=\"{rx:F1}\" y=\"{ry:F1}\" width=\"{rw:F1}\" height=\"{rh:F1}\" rx=\"2\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{sw:F1}\" />"));
                        break;
                    }
                    case "circle":
                        sb.AppendLine(FormattableString.Invariant(
                            $"        <circle cx=\"{prim.X1:F1}\" cy=\"{prim.Y1:F1}\" r=\"{prim.Radius:F1}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{sw:F1}\" />"));
                        break;
                    case "polyline":
                        if (!string.IsNullOrWhiteSpace(prim.Points))
                        {
                            sb.AppendLine(FormattableString.Invariant(
                                $"        <polyline points=\"{EscapeXml(prim.Points)}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{sw:F1}\" stroke-linejoin=\"round\" stroke-linecap=\"round\" />"));
                        }
                        break;
                    case "polygon":
                        if (!string.IsNullOrWhiteSpace(prim.Points))
                        {
                            sb.AppendLine(FormattableString.Invariant(
                                $"        <polygon points=\"{EscapeXml(prim.Points)}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{sw:F1}\" stroke-linejoin=\"round\" />"));
                        }
                        break;
                    case "text":
                        if (!string.IsNullOrWhiteSpace(prim.Text))
                        {
                            sb.AppendLine(FormattableString.Invariant(
                                $"        <text x=\"{prim.X1:F1}\" y=\"{prim.Y1:F1}\" text-anchor=\"middle\" fill=\"{stroke}\" font-family=\"monospace\" font-size=\"10\" font-weight=\"bold\">{EscapeXml(prim.Text)}</text>"));
                        }
                        break;
                }
            }
            sb.AppendLine("      </g>");
            return;
        }

        // Hit box for smooth dragging & selection highlight
        sb.AppendLine($"      <rect x=\"-36\" y=\"-22\" width=\"72\" height=\"44\" rx=\"6\" fill=\"{symFill}\" fill-opacity=\"0.82\" stroke=\"{symBorder}\" stroke-width=\"1\" />");

        switch (style)
        {
            case "resistor":
                sb.AppendLine($"      <polyline points=\"-36,0 -22,0 -18,-9 -10,9 -2,-9 6,9 14,-9 20,0 36,0\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" stroke-linejoin=\"round\" />");
                break;
            case "capacitor":
                sb.AppendLine($"      <line x1=\"-36\" y1=\"0\" x2=\"-5\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"-5\" y1=\"-13\" x2=\"-5\" y2=\"13\" stroke=\"{symStroke}\" stroke-width=\"2.4\" />");
                sb.AppendLine($"      <line x1=\"5\" y1=\"-13\" x2=\"5\" y2=\"13\" stroke=\"{symStroke}\" stroke-width=\"2.4\" />");
                sb.AppendLine($"      <line x1=\"5\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                break;
            case "inductor":
                sb.AppendLine($"      <path d=\"M -36 0 L -20 0 A 5 5 0 0 1 -10 0 A 5 5 0 0 1 0 0 A 5 5 0 0 1 10 0 A 5 5 0 0 1 20 0 L 36 0\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                break;
            case "vsrc":
                sb.AppendLine($"      <line x1=\"-36\" y1=\"0\" x2=\"-16\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"16\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <circle cx=\"0\" cy=\"0\" r=\"16\" fill=\"{symFill}\" stroke=\"#38bdf8\" stroke-width=\"2\" />");
                sb.AppendLine($"      <text x=\"-7\" y=\"4\" text-anchor=\"middle\" fill=\"{symStroke}\" font-family=\"monospace\" font-size=\"12\" font-weight=\"bold\">+</text>");
                sb.AppendLine($"      <text x=\"7\" y=\"4\" text-anchor=\"middle\" fill=\"{textMuted}\" font-family=\"monospace\" font-size=\"12\" font-weight=\"bold\">-</text>");
                break;
            case "isrc":
                sb.AppendLine($"      <line x1=\"-36\" y1=\"0\" x2=\"-16\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"16\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <circle cx=\"0\" cy=\"0\" r=\"16\" fill=\"{symFill}\" stroke=\"#a855f7\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"-8\" y1=\"0\" x2=\"8\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <polyline points=\"3,-5 9,0 3,5\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                break;
            case "diode":
                sb.AppendLine($"      <line x1=\"-36\" y1=\"0\" x2=\"-10\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine("      <polygon points=\"-10,-11 -10,11 10,0\" fill=\"#1e293b\" stroke=\"#f43f5e\" stroke-width=\"2\" />");
                sb.AppendLine("      <line x1=\"10\" y1=\"-11\" x2=\"10\" y2=\"11\" stroke=\"#f43f5e\" stroke-width=\"2.4\" />");
                sb.AppendLine($"      <line x1=\"10\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                break;
            case "schottky":
                sb.AppendLine($"      <line x1=\"-36\" y1=\"0\" x2=\"-10\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine("      <polygon points=\"-10,-11 -10,11 10,0\" fill=\"#1e293b\" stroke=\"#fb923c\" stroke-width=\"2\" />");
                sb.AppendLine("      <polyline points=\"15,-12 10,-12 10,12 5,12\" fill=\"none\" stroke=\"#fb923c\" stroke-width=\"2.2\" />");
                sb.AppendLine($"      <line x1=\"10\" y1=\"0\" x2=\"36\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                break;
            case "nmos":
            case "pmos":
            {
                var accent = style == "pmos" ? "#c084fc" : "#38bdf8";
                sb.AppendLine($"      <line x1=\"-40\" y1=\"0\" x2=\"-12\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"-12\" y1=\"-14\" x2=\"-12\" y2=\"14\" stroke=\"{accent}\" stroke-width=\"2.2\" />");
                sb.AppendLine($"      <line x1=\"-5\" y1=\"-14\" x2=\"-5\" y2=\"14\" stroke=\"{accent}\" stroke-width=\"2\" stroke-dasharray=\"6 3\" />");
                sb.AppendLine($"      <polyline points=\"-5,-11 20,-11 20,-20\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <polyline points=\"-5,11 20,11 20,20\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"-5\" y1=\"0\" x2=\"40\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"1.6\" />");
                if (style == "pmos")
                {
                    sb.AppendLine($"      <circle cx=\"-16\" cy=\"0\" r=\"3.2\" fill=\"{symFill}\" stroke=\"{accent}\" stroke-width=\"1.6\" />");
                    sb.AppendLine($"      <polygon points=\"8,-4 16,0 8,4\" fill=\"{accent}\" />");
                }
                else
                {
                    sb.AppendLine($"      <polygon points=\"6,-4 -2,0 6,4\" fill=\"{accent}\" />");
                }
                break;
            }
            case "npn":
            case "pnp":
            {
                var accent = style == "pnp" ? "#f472b6" : "#34d399";
                sb.AppendLine($"      <line x1=\"-40\" y1=\"0\" x2=\"-10\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"-10\" y1=\"-15\" x2=\"-10\" y2=\"15\" stroke=\"{accent}\" stroke-width=\"2.8\" />");
                sb.AppendLine($"      <polyline points=\"-10,-6 20,-20\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <polyline points=\"-10,6 20,20\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                if (style == "npn")
                {
                    sb.AppendLine($"      <polygon points=\"7,10 15,17 4,17\" fill=\"{accent}\" />");
                }
                else
                {
                    sb.AppendLine($"      <polygon points=\"2,15 -6,8 5,8\" fill=\"{accent}\" />");
                }
                break;
            }
            case "jfet":
                sb.AppendLine($"      <line x1=\"-40\" y1=\"0\" x2=\"-10\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine("      <line x1=\"-6\" y1=\"-14\" x2=\"-6\" y2=\"14\" stroke=\"#fbbf24\" stroke-width=\"2.5\" />");
                sb.AppendLine($"      <polyline points=\"-6,-11 20,-11 20,-20\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <polyline points=\"-6,11 20,11 20,20\" fill=\"none\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine("      <polygon points=\"-15,-4 -7,0 -15,4\" fill=\"#fbbf24\" />");
                break;
            case "opamp":
                sb.AppendLine("      <polygon points=\"-26,-22 -26,22 28,0\" fill=\"#1e293b\" stroke=\"#38bdf8\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"-40\" y1=\"-20\" x2=\"-26\" y2=\"-20\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"-40\" y1=\"20\" x2=\"-26\" y2=\"20\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"28\" y1=\"0\" x2=\"40\" y2=\"0\" stroke=\"{symStroke}\" stroke-width=\"2\" />");
                sb.AppendLine($"      <text x=\"-18\" y=\"-8\" fill=\"{symStroke}\" font-family=\"monospace\" font-size=\"11\" font-weight=\"bold\">+</text>");
                sb.AppendLine($"      <text x=\"-18\" y=\"13\" fill=\"{symStroke}\" font-family=\"monospace\" font-size=\"11\" font-weight=\"bold\">-</text>");
                break;
            case "vcvs":
            case "vccs":
                sb.AppendLine("      <polygon points=\"0,-18 22,0 0,18 -22,0\" fill=\"#1e293b\" stroke=\"#38bdf8\" stroke-width=\"2\" />");
                sb.AppendLine($"      <line x1=\"-40\" y1=\"-20\" x2=\"-12\" y2=\"-10\" stroke=\"{symStroke}\" stroke-width=\"1.6\" />");
                sb.AppendLine($"      <line x1=\"-40\" y1=\"20\" x2=\"-12\" y2=\"10\" stroke=\"{symStroke}\" stroke-width=\"1.6\" />");
                sb.AppendLine($"      <line x1=\"40\" y1=\"-20\" x2=\"12\" y2=\"-10\" stroke=\"{symStroke}\" stroke-width=\"1.6\" />");
                sb.AppendLine($"      <line x1=\"40\" y1=\"20\" x2=\"12\" y2=\"10\" stroke=\"{symStroke}\" stroke-width=\"1.6\" />");
                sb.AppendLine($"      <text x=\"0\" y=\"4\" text-anchor=\"middle\" fill=\"{symStroke}\" font-family=\"monospace\" font-size=\"10\" font-weight=\"bold\">{(style == "vcvs" ? "E" : "G")}</text>");
                break;
            default:
            {
                // Sleek custom IC / Subcircuit block with labeled pins
                sb.AppendLine("      <rect x=\"-28\" y=\"-20\" width=\"56\" height=\"40\" rx=\"5\" fill=\"#172554\" stroke=\"#60a5fa\" stroke-width=\"2\" />");
                foreach (var (pinName, px, py) in localPins)
                {
                    var edgeX = px < 0 ? -28.0 : 28.0;
                    sb.AppendLine(FormattableString.Invariant(
                        $"      <line x1=\"{px:F1}\" y1=\"{py:F1}\" x2=\"{edgeX:F1}\" y2=\"{py:F1}\" stroke=\"{symStroke}\" stroke-width=\"1.8\" />"));
                    var tx = px < 0 ? -22.0 : 22.0;
                    var anchor = px < 0 ? "start" : "end";
                    sb.AppendLine(FormattableString.Invariant(
                        $"      <text x=\"{tx:F1}\" y=\"{py + 3.0:F1}\" text-anchor=\"{anchor}\" fill=\"#bfdbfe\" font-family=\"monospace\" font-size=\"8\">{EscapeXml(pinName)}</text>"));
                }
                if (!string.IsNullOrWhiteSpace(customPart?.CustomSvgPath))
                {
                    sb.AppendLine($"      <path d=\"{EscapeXml(customPart.CustomSvgPath)}\" fill=\"none\" stroke=\"#93c5fd\" stroke-width=\"1.5\" />");
                }
                break;
            }
        }
    }

    private static void AddAttachedModel(
        Dictionary<string, ModelDefinitionIr> models,
        AttachedModelDefinition attached)
    {
        if (string.IsNullOrWhiteSpace(attached.ModelName)) return;
        var modelType = attached.Kind switch
        {
            AttachedModelKind.Subcircuit => "SUBCKT",
            AttachedModelKind.Osdi => "OSDI",
            _ => "SPICE"
        };
        models[attached.ModelName] = new ModelDefinitionIr(
            Name: attached.ModelName,
            ModelType: modelType,
            Parameters: new Dictionary<string, double>(),
            OsdiLibraryPath: attached.OsdiLibraryPath,
            RawSpiceDefinition: attached.SpiceText);
    }

    private static void ValidateSheet(
        SchematicDocument document,
        SchematicSheet sheet,
        DeviceRegistry reg,
        ComponentLibraryStore? library,
        List<ErcDiagnostic> diagnostics,
        bool requireGround)
    {
        var compMap = new Dictionary<string, PlacedComponent>(StringComparer.OrdinalIgnoreCase);
        foreach (var comp in sheet.Components)
        {
            if (string.IsNullOrWhiteSpace(comp.InstanceId))
            {
                diagnostics.Add(new ErcDiagnostic(ErcSeverity.Error, "empty_instance_id", "PlacedComponent has empty InstanceId.", sheet.SheetId, X: comp.X, Y: comp.Y));
                continue;
            }
            if (!compMap.TryAdd(comp.InstanceId, comp))
            {
                diagnostics.Add(new ErcDiagnostic(ErcSeverity.Error, "duplicate_instance", $"Duplicate component '{comp.InstanceId}'.", sheet.SheetId, comp.InstanceId, comp.X, comp.Y));
            }
            if (!reg.TryGet(comp.DeviceKindId, out _))
            {
                diagnostics.Add(new ErcDiagnostic(ErcSeverity.Error, "unknown_device_kind", $"Component '{comp.InstanceId}' uses unknown device kind '{comp.DeviceKindId}'.", sheet.SheetId, comp.InstanceId, comp.X, comp.Y));
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
                if (!compMap.TryGetValue(pinRef.InstanceId, out var targetComp))
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
                        pinRef.InstanceId,
                        targetComp.X,
                        targetComp.Y));
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
            var customPart = ResolveCustomPart(document, comp, library);

            var expectedPinNames = customPart is { Pins.Count: > 0 } &&
                                   string.Equals(comp.DeviceKindId, "spice.subckt", StringComparison.Ordinal)
                ? customPart.Pins.Select(p => p.Name).ToList()
                : desc.Pins.Select(p => p.Name).ToList();

            var pins = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pinName in expectedPinNames)
            {
                if (pinConnectivity.TryGetValue((comp.InstanceId, pinName), out var netName))
                {
                    pins[pinName] = netName;
                }
                else
                {
                    diagnostics.Add(new ErcDiagnostic(
                        ErcSeverity.Error,
                        "unconnected_pin",
                        $"Component '{comp.InstanceId}' pin '{pinName}' is unconnected.",
                        sheet.SheetId,
                        comp.InstanceId,
                        comp.X,
                        comp.Y));
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
                    comp.InstanceId,
                    comp.X,
                    comp.Y));
            }
        }
    }

    private static void EmitSheetInto(
        SchematicDocument document,
        SchematicSheet sheet,
        string prefix,
        HashSet<string> nodes,
        List<DeviceInstanceIr> devices,
        Dictionary<string, ModelDefinitionIr> models,
        List<string> directives,
        ComponentLibraryStore? library)
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

        if (sheet.Annotations is not null)
        {
            foreach (var ann in sheet.Annotations)
            {
                if (ann.IsSpiceDirective && !string.IsNullOrWhiteSpace(ann.Text))
                {
                    directives.Add(ann.Text.Trim());
                }
            }
        }

        foreach (var comp in sheet.Components)
        {
            var customPart = ResolveCustomPart(document, comp, library);
            if (customPart?.AttachedModel is not null)
            {
                AddAttachedModel(models, customPart.AttachedModel);
            }

            var conns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var ((inst, pin), netName) in pinMap)
            {
                if (string.Equals(inst, comp.InstanceId, StringComparison.Ordinal))
                {
                    conns[pin] = netName;
                }
            }

            var effectiveModelName = !string.IsNullOrWhiteSpace(comp.ModelName)
                ? comp.ModelName
                : customPart?.AttachedModel?.ModelName;

            IReadOnlyList<string>? pinOrder = customPart is { Pins.Count: > 0 } &&
                                              string.Equals(comp.DeviceKindId, "spice.subckt", StringComparison.Ordinal)
                ? customPart.Pins.Select(p => p.Name).ToList()
                : null;

            devices.Add(new DeviceInstanceIr(
                Id: $"{prefix}{comp.InstanceId}",
                KindId: comp.DeviceKindId,
                PinConnections: conns,
                Parameters: new Dictionary<string, double>(comp.Parameters, StringComparer.Ordinal),
                ModelName: effectiveModelName,
                PinOrder: pinOrder));
        }
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal)
         .Replace("\"", "&quot;", StringComparison.Ordinal);
}
