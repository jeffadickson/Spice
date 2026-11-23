using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spice.Ir;

namespace Spice.Schematic;

/// <summary>
/// Supported target netlist dialects for schematic export (ARCH-SCHEM-005, RSpice parity).
/// </summary>
public enum NetlistDialect
{
    CanonicalSpice3f5,
    Ngspice,
    Xyce,
    Ltspice,
    Spectre
}

/// <summary>
/// Configurable netlist and artifact export options derived from the schematic document.
/// </summary>
public sealed record SchematicExportOptions(
    NetlistDialect Dialect = NetlistDialect.CanonicalSpice3f5,
    AnalysisKind Analysis = AnalysisKind.OperatingPoint,
    IReadOnlyDictionary<string, double>? AnalysisOptions = null,
    string? SweepSourceId = null,
    bool IncludeComments = true,
    bool IncludeProbes = true);

/// <summary>
/// Multi-dialect netlist, StarLark-cs script, BOM CSV, checksummed export bundle, and
/// SPICE netlist importer for Spice.Schematic (ARCH-SCHEM-005).
/// </summary>
public static class SchematicExporter
{
    private static readonly JsonSerializerOptions BundleJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string Fmt(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

    public static string ExportNetlist(
        SchematicDocument document,
        SchematicExportOptions? options = null,
        DeviceRegistry? registry = null,
        ComponentLibraryStore? library = null)
    {
        var opts = options ?? new SchematicExportOptions();
        var compiled = SchematicCompiler.CompileToIr(document, registry, library);
        if (!compiled.Succeeded || compiled.Circuit is null)
        {
            throw new InvalidOperationException(
                "Schematic failed ERC: " +
                string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        }

        var circuit = compiled.Circuit;
        var req = new SimulationRequestIr(
            document.Name,
            circuit,
            opts.Analysis,
            opts.AnalysisOptions,
            SweepSourceId: opts.SweepSourceId);

        return opts.Dialect switch
        {
            NetlistDialect.Spectre => EmitSpectreNetlist(document, req, opts),
            NetlistDialect.Xyce => SpiceNetlistEmitter.EmitNetlist(req, includeXycePrintCsv: opts.IncludeProbes, registry: registry),
            NetlistDialect.Ngspice => EmitNgspiceNetlist(req, opts, registry),
            NetlistDialect.Ltspice => EmitLtspiceNetlist(req, opts, registry),
            _ => SpiceNetlistEmitter.EmitNetlist(req, includeXycePrintCsv: false, registry: registry)
        };
    }

    private static string EmitNgspiceNetlist(SimulationRequestIr req, SchematicExportOptions opts, DeviceRegistry? registry)
    {
        var baseNet = SpiceNetlistEmitter.EmitNetlist(req, includeXycePrintCsv: false, registry: registry);
        var lines = baseNet.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var endIdx = lines.FindLastIndex(l => l.Trim().Equals(".END", StringComparison.OrdinalIgnoreCase));
        if (endIdx < 0) endIdx = lines.Count;

        var insert = new List<string>();
        if (opts.IncludeComments)
        {
            insert.Add("* Dialect: ngspice");
        }
        if (opts.IncludeProbes)
        {
            var nodes = req.Circuit.Nodes.Where(n => n != "0").OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (nodes.Count > 0)
            {
                insert.Add(".SAVE " + string.Join(" ", nodes.Select(n => $"v({n})")));
            }
        }
        lines.InsertRange(endIdx, insert);
        return string.Join(Environment.NewLine, lines);
    }

    private static string EmitLtspiceNetlist(SimulationRequestIr req, SchematicExportOptions opts, DeviceRegistry? registry)
    {
        var baseNet = SpiceNetlistEmitter.EmitNetlist(req, includeXycePrintCsv: false, registry: registry);
        var lines = baseNet.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var endIdx = lines.FindLastIndex(l => l.Trim().Equals(".END", StringComparison.OrdinalIgnoreCase));
        if (endIdx < 0) endIdx = lines.Count;

        var insert = new List<string>();
        if (opts.IncludeComments)
        {
            insert.Add("* Dialect: LTspice-compatible");
        }
        insert.Add(".backanno");
        lines.InsertRange(endIdx, insert);
        return string.Join(Environment.NewLine, lines);
    }

    private static string EmitSpectreNetlist(SchematicDocument doc, SimulationRequestIr req, SchematicExportOptions opts)
    {
        var sb = new StringBuilder();
        if (opts.IncludeComments)
        {
            sb.AppendLine($"// Spice V2 Spectre Netlist Export: {doc.Name}");
        }
        sb.AppendLine("simulator lang=spectre");
        sb.AppendLine("global 0");

        if (req.Circuit.Models is not null)
        {
            foreach (var m in req.Circuit.Models)
            {
                if (!string.IsNullOrWhiteSpace(m.RawSpiceDefinition))
                {
                    sb.AppendLine($"// Embedded SPICE model/subckt '{m.Name}'");
                    sb.AppendLine("simulator lang=spice");
                    sb.AppendLine(m.RawSpiceDefinition.Trim());
                    sb.AppendLine("simulator lang=spectre");
                }
                else
                {
                    var kvs = string.Join(" ", m.Parameters.Select(kv => $"{kv.Key}={Fmt(kv.Value)}"));
                    sb.AppendLine($"model {m.Name} {m.ModelType.ToLowerInvariant()} {kvs}");
                }
            }
        }

        foreach (var dev in req.Circuit.Devices)
        {
            switch (dev.KindId)
            {
                case "spice.resistor":
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["p"]} {dev.PinConnections["n"]}) resistor r={Fmt(dev.Parameters["ohms"])}");
                    break;
                case "spice.capacitor":
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["p"]} {dev.PinConnections["n"]}) capacitor c={Fmt(dev.Parameters["farads"])}");
                    break;
                case "spice.inductor":
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["p"]} {dev.PinConnections["n"]}) inductor l={Fmt(dev.Parameters["henries"])}");
                    break;
                case "spice.vsrc":
                {
                    var dc = dev.Parameters.GetValueOrDefault("dc_volts", 0.0);
                    var ac = dev.Parameters.TryGetValue("ac_mag", out var am) && am != 0.0 ? $" mag={Fmt(am)}" : "";
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["p"]} {dev.PinConnections["n"]}) vsource dc={Fmt(dc)}{ac}");
                    break;
                }
                case "spice.isrc":
                {
                    var dc = dev.Parameters.GetValueOrDefault("dc_amps", 0.0);
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["p"]} {dev.PinConnections["n"]}) isource dc={Fmt(dc)}");
                    break;
                }
                case "spice.diode":
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["p"]} {dev.PinConnections["n"]}) {(string.IsNullOrWhiteSpace(dev.ModelName) ? $"MOD_{dev.Id}" : dev.ModelName)}");
                    break;
                case "spice.mosfet":
                {
                    var w = dev.Parameters.GetValueOrDefault("w_m", 1e-6);
                    var l = dev.Parameters.GetValueOrDefault("l_m", 65e-9);
                    var mname = string.IsNullOrWhiteSpace(dev.ModelName) ? $"MOS_{dev.Id}" : dev.ModelName;
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["d"]} {dev.PinConnections["g"]} {dev.PinConnections["s"]} {dev.PinConnections["b"]}) {mname} w={Fmt(w)} l={Fmt(l)}");
                    break;
                }
                case "spice.bjt":
                {
                    var mname = string.IsNullOrWhiteSpace(dev.ModelName) ? $"BJT_{dev.Id}" : dev.ModelName;
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["c"]} {dev.PinConnections["b"]} {dev.PinConnections["e"]}) {mname} area={Fmt(dev.Parameters.GetValueOrDefault("area", 1.0))}");
                    break;
                }
                case "spice.jfet":
                {
                    var mname = string.IsNullOrWhiteSpace(dev.ModelName) ? $"JFET_{dev.Id}" : dev.ModelName;
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["d"]} {dev.PinConnections["g"]} {dev.PinConnections["s"]}) {mname} area={Fmt(dev.Parameters.GetValueOrDefault("area", 1.0))}");
                    break;
                }
                case "spice.subckt":
                {
                    var orderedPins = dev.PinOrder is { Count: > 0 }
                        ? dev.PinOrder.Select(p => dev.PinConnections[p])
                        : dev.PinConnections.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value);
                    var parms = dev.Parameters.Count > 0
                        ? " " + string.Join(" ", dev.Parameters.Select(kv => $"{kv.Key}={Fmt(kv.Value)}"))
                        : "";
                    sb.AppendLine($"{dev.Id} ({string.Join(" ", orderedPins)}) {dev.ModelName}{parms}");
                    break;
                }
                case "spice.vcvs":
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["p"]} {dev.PinConnections["n"]} {dev.PinConnections["cp"]} {dev.PinConnections["cn"]}) vcvs gain={Fmt(dev.Parameters["gain"])}");
                    break;
                case "spice.vccs":
                    sb.AppendLine($"{dev.Id} ({dev.PinConnections["p"]} {dev.PinConnections["n"]} {dev.PinConnections["cp"]} {dev.PinConnections["cn"]}) vccs gm={Fmt(dev.Parameters["siemens"])}");
                    break;
            }
        }

        var anOpts = opts.AnalysisOptions ?? new Dictionary<string, double>();
        switch (opts.Analysis)
        {
            case AnalysisKind.OperatingPoint:
                sb.AppendLine("dcOp dc");
                break;
            case AnalysisKind.DcSweep:
                sb.AppendLine($"dc1 dc dev={opts.SweepSourceId ?? "V1"} param=dc start={Fmt(anOpts.GetValueOrDefault("start", 0.0))} stop={Fmt(anOpts.GetValueOrDefault("stop", 1.2))} step={Fmt(anOpts.GetValueOrDefault("step", 0.1))}");
                break;
            case AnalysisKind.Ac:
                sb.AppendLine($"ac1 ac start={Fmt(anOpts.GetValueOrDefault("f_start_hz", 10.0))} stop={Fmt(anOpts.GetValueOrDefault("f_stop_hz", 1e5))} dec={(int)Math.Max(1, Math.Round(anOpts.GetValueOrDefault("points_per_decade", 10.0)))}");
                break;
            case AnalysisKind.Transient:
                sb.AppendLine($"tran1 tran step={Fmt(anOpts.GetValueOrDefault("step_s", 1e-5))} stop={Fmt(anOpts.GetValueOrDefault("stop_s", 1e-3))}");
                break;
            default:
                sb.AppendLine("dcOp dc");
                break;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Exports a deterministic StarLark-cs / CircuitBuilder authoring script (ARCH-SCHEM-005).
    /// </summary>
    public static string ExportStarlarkScript(
        SchematicDocument document,
        DeviceRegistry? registry = null,
        ComponentLibraryStore? library = null)
    {
        var compiled = SchematicCompiler.CompileToIr(document, registry, library);
        if (!compiled.Succeeded || compiled.Circuit is null)
        {
            throw new InvalidOperationException("Schematic failed ERC; cannot export StarLark script.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Spice V2 StarLark-cs circuit script exported from schematic '{document.Name}'");
        sb.AppendLine($"def build_{SanitizeId(document.Name)}():");
        sb.AppendLine($"    c = circuit(\"{document.Name}\")");
        foreach (var dev in compiled.Circuit.Devices)
        {
            var pins = string.Join(", ", dev.PinConnections.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"\"{kv.Key}\": \"{kv.Value}\""));
            var parms = string.Join(", ", dev.Parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"\"{kv.Key}\": {Fmt(kv.Value)}"));
            var modelArg = string.IsNullOrWhiteSpace(dev.ModelName) ? "" : $", model=\"{dev.ModelName}\"";
            sb.AppendLine($"    c.add(\"{dev.Id}\", \"{dev.KindId}\", pins={{{pins}}}, params={{{parms}}}{modelArg})");
        }
        sb.AppendLine("    return c");
        return sb.ToString();
    }

    /// <summary>
    /// Exports a deterministic CSV Bill of Materials (BOM) (ARCH-SCHEM-005).
    /// </summary>
    public static string ExportBomCsv(
        SchematicDocument document,
        ComponentLibraryStore? library = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Designator,DeviceKind,LibraryPart,ModelName,PrimaryValue,PositionX,PositionY,RotationDeg");
        foreach (var comp in document.RootSheet.Components.OrderBy(c => c.InstanceId, StringComparer.OrdinalIgnoreCase))
        {
            var partName = "";
            if (!string.IsNullOrWhiteSpace(comp.LibraryPartId))
            {
                var found = document.EmbeddedParts?.FirstOrDefault(p => string.Equals(p.Id, comp.LibraryPartId, StringComparison.OrdinalIgnoreCase));
                if (found is null && library is not null) library.TryGet(comp.LibraryPartId, out found);
                partName = found?.Name ?? comp.LibraryPartId;
            }
            var val = comp.DisplayValue ?? FormatBomValue(comp);
            sb.AppendLine(string.Join(",",
                CsvEscape(comp.InstanceId),
                CsvEscape(comp.DeviceKindId),
                CsvEscape(partName),
                CsvEscape(comp.ModelName ?? ""),
                CsvEscape(val),
                Fmt(comp.X),
                Fmt(comp.Y),
                comp.RotationDegrees.ToString(CultureInfo.InvariantCulture)));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Exports a self-contained bundle with schematic, netlist, attached models, BOM, and a
    /// SHA-256 checksum manifest (RSpice "Export bundle with dependencies and checksum manifest" parity).
    /// </summary>
    public static string ExportBundleJson(
        SchematicDocument document,
        SchematicExportOptions? options = null,
        DeviceRegistry? registry = null,
        ComponentLibraryStore? library = null)
    {
        var opts = options ?? new SchematicExportOptions();
        var schemJson = SchematicCompiler.SerializeJson(document);
        var netlist = ExportNetlist(document, opts, registry, library);
        var starlark = ExportStarlarkScript(document, registry, library);
        var bom = ExportBomCsv(document, library);

        var ext = opts.Dialect switch
        {
            NetlistDialect.Spectre => "scs",
            NetlistDialect.Ltspice => "net",
            _ => "cir"
        };

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{document.Name}.spice.json"] = schemJson,
            [$"{document.Name}.{ext}"] = netlist,
            [$"{document.Name}.star"] = starlark,
            [$"{document.Name}.bom.csv"] = bom
        };

        // Include every attached model / subcircuit as its own .lib artifact inside the bundle.
        var compiled = SchematicCompiler.CompileToIr(document, registry, library);
        if (compiled.Circuit?.Models is { Count: > 0 } models)
        {
            var libSb = new StringBuilder();
            foreach (var m in models)
            {
                if (!string.IsNullOrWhiteSpace(m.RawSpiceDefinition))
                {
                    libSb.AppendLine(m.RawSpiceDefinition.Trim());
                    libSb.AppendLine();
                }
            }
            if (libSb.Length > 0)
            {
                files[$"{document.Name}.models.lib"] = libSb.ToString().TrimEnd();
            }
        }

        var manifest = files.ToDictionary(
            kv => kv.Key,
            kv => new
            {
                bytes = Encoding.UTF8.GetByteCount(kv.Value),
                sha256 = Sha256Hex(kv.Value)
            });

        var bundle = new
        {
            formatId = "spice.export-bundle.v2",
            schemaVersion = "2.1",
            schematicName = document.Name,
            dialect = opts.Dialect.ToString(),
            analysis = opts.Analysis.ToString(),
            manifest,
            files
        };
        return JsonSerializer.Serialize(bundle, BundleJsonOpts);
    }

    /// <summary>
    /// Best-effort SPICE netlist importer creating a grid-snapped SchematicDocument with
    /// embedded .MODEL / .SUBCKT definitions (RSpice "Import SPICE deck" parity).
    /// </summary>
    public static SchematicDocument ImportFromSpiceNetlist(string spiceText, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(spiceText))
        {
            throw new InvalidDataException("SPICE netlist text is empty.");
        }

        var lines = spiceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var docName = string.IsNullOrWhiteSpace(name) ? "imported_spice" : name.Trim();
        var components = new List<PlacedComponent>();
        var netPins = new Dictionary<string, List<PinRef>>(StringComparer.OrdinalIgnoreCase);
        var embeddedModels = new List<AttachedModelDefinition>();
        var directives = new List<SheetAnnotation>();

        bool inSubckt = false;
        string? curSubName = null;
        var curSubLines = new List<string>();
        int idx = 0;

        void AttachPin(string netName, string inst, string pin)
        {
            var eff = string.Equals(netName, "0", StringComparison.Ordinal) ||
                      string.Equals(netName, "gnd", StringComparison.OrdinalIgnoreCase)
                ? "GND"
                : netName;
            if (!netPins.TryGetValue(eff, out var list))
            {
                list = [];
                netPins[eff] = list;
            }
            list.Add(new PinRef(inst, pin));
        }

        for (int lineIdx = 0; lineIdx < lines.Length; ++lineIdx)
        {
            var raw = lines[lineIdx].Trim();
            if (string.IsNullOrEmpty(raw)) continue;
            if (lineIdx == 0 && !raw.StartsWith('.') && !raw.StartsWith('*') && raw.Split(' ').Length > 0 && !LooksLikeElement(raw))
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    docName = SanitizeId(raw);
                }
                continue;
            }
            if (raw.StartsWith('*')) continue;

            if (inSubckt)
            {
                curSubLines.Add(raw);
                if (raw.StartsWith(".ENDS", StringComparison.OrdinalIgnoreCase))
                {
                    embeddedModels.Add(new AttachedModelDefinition(
                        AttachedModelKind.Subcircuit,
                        curSubName ?? "SUBCKT",
                        string.Join(Environment.NewLine, curSubLines)));
                    inSubckt = false;
                    curSubName = null;
                    curSubLines.Clear();
                }
                continue;
            }

            if (raw.StartsWith(".SUBCKT", StringComparison.OrdinalIgnoreCase))
            {
                var toks = SplitTokens(raw);
                inSubckt = true;
                curSubName = toks.Length > 1 ? toks[1] : "SUBCKT";
                curSubLines.Clear();
                curSubLines.Add(raw);
                continue;
            }
            if (raw.StartsWith(".MODEL", StringComparison.OrdinalIgnoreCase))
            {
                var toks = SplitTokens(raw);
                if (toks.Length >= 3)
                {
                    embeddedModels.Add(new AttachedModelDefinition(AttachedModelKind.SpiceModelCard, toks[1], raw));
                }
                continue;
            }
            if (raw.StartsWith(".PARAM", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith(".TEMP", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith(".OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                directives.Add(new SheetAnnotation($"dir_{directives.Count + 1}", 80, 260 + directives.Count * 24, raw, IsSpiceDirective: true));
                continue;
            }
            if (raw.StartsWith('.')) continue;

            var t = SplitTokens(raw);
            if (t.Length < 4) continue;
            var id = t[0];
            char prefix = char.ToUpperInvariant(id[0]);
            double x = 80.0 + (idx % 4) * 120.0;
            double y = 80.0 + (idx / 4) * 100.0;
            idx++;

            switch (prefix)
            {
                case 'R':
                    components.Add(new PlacedComponent(id, "spice.resistor", x, y, 0, new Dictionary<string, double> { ["ohms"] = ParseSpiceNumber(t[3], 1000.0) }));
                    AttachPin(t[1], id, "p");
                    AttachPin(t[2], id, "n");
                    break;
                case 'C':
                    components.Add(new PlacedComponent(id, "spice.capacitor", x, y, 90, new Dictionary<string, double> { ["farads"] = ParseSpiceNumber(t[3], 1e-6) }));
                    AttachPin(t[1], id, "p");
                    AttachPin(t[2], id, "n");
                    break;
                case 'L':
                    components.Add(new PlacedComponent(id, "spice.inductor", x, y, 0, new Dictionary<string, double> { ["henries"] = ParseSpiceNumber(t[3], 1e-3) }));
                    AttachPin(t[1], id, "p");
                    AttachPin(t[2], id, "n");
                    break;
                case 'V':
                {
                    var parms = ParseSourceParams(t, "dc_volts");
                    components.Add(new PlacedComponent(id, "spice.vsrc", x, y, 90, parms));
                    AttachPin(t[1], id, "p");
                    AttachPin(t[2], id, "n");
                    break;
                }
                case 'I':
                {
                    var parms = ParseSourceParams(t, "dc_amps");
                    components.Add(new PlacedComponent(id, "spice.isrc", x, y, 90, parms));
                    AttachPin(t[1], id, "p");
                    AttachPin(t[2], id, "n");
                    break;
                }
                case 'D':
                    components.Add(new PlacedComponent(id, "spice.diode", x, y, 90, new Dictionary<string, double> { ["is_amps"] = 1e-14, ["n_emission"] = 1.0 }, ModelName: t[3]));
                    AttachPin(t[1], id, "p");
                    AttachPin(t[2], id, "n");
                    break;
                case 'M' when t.Length >= 6:
                {
                    var parms = new Dictionary<string, double> { ["w_m"] = 1e-6, ["l_m"] = 65e-9 };
                    for (int k = 6; k < t.Length; ++k)
                    {
                        var kv = t[k].Split('=');
                        if (kv.Length == 2)
                        {
                            if (kv[0].Equals("W", StringComparison.OrdinalIgnoreCase)) parms["w_m"] = ParseSpiceNumber(kv[1], 1e-6);
                            if (kv[0].Equals("L", StringComparison.OrdinalIgnoreCase)) parms["l_m"] = ParseSpiceNumber(kv[1], 65e-9);
                        }
                    }
                    components.Add(new PlacedComponent(id, "spice.mosfet", x, y, 0, parms, ModelName: t[5]));
                    AttachPin(t[1], id, "d");
                    AttachPin(t[2], id, "g");
                    AttachPin(t[3], id, "s");
                    AttachPin(t[4], id, "b");
                    break;
                }
                case 'Q' when t.Length >= 5:
                    components.Add(new PlacedComponent(id, "spice.bjt", x, y, 0, new Dictionary<string, double> { ["area"] = 1.0 }, ModelName: t[4]));
                    AttachPin(t[1], id, "c");
                    AttachPin(t[2], id, "b");
                    AttachPin(t[3], id, "e");
                    break;
                case 'J' when t.Length >= 5:
                    components.Add(new PlacedComponent(id, "spice.jfet", x, y, 0, new Dictionary<string, double> { ["area"] = 1.0 }, ModelName: t[4]));
                    AttachPin(t[1], id, "d");
                    AttachPin(t[2], id, "g");
                    AttachPin(t[3], id, "s");
                    break;
                case 'E' when t.Length >= 6:
                    components.Add(new PlacedComponent(id, "spice.vcvs", x, y, 0, new Dictionary<string, double> { ["gain"] = ParseSpiceNumber(t[5], 1.0) }));
                    AttachPin(t[1], id, "p");
                    AttachPin(t[2], id, "n");
                    AttachPin(t[3], id, "cp");
                    AttachPin(t[4], id, "cn");
                    break;
                case 'G' when t.Length >= 6:
                    components.Add(new PlacedComponent(id, "spice.vccs", x, y, 0, new Dictionary<string, double> { ["siemens"] = ParseSpiceNumber(t[5], 1e-3) }));
                    AttachPin(t[1], id, "p");
                    AttachPin(t[2], id, "n");
                    AttachPin(t[3], id, "cp");
                    AttachPin(t[4], id, "cn");
                    break;
            }
        }

        var nets = netPins.Select(kv => new WireNet(
            kv.Key,
            kv.Value,
            IsGlobalGround: string.Equals(kv.Key, "GND", StringComparison.OrdinalIgnoreCase))).ToList();

        return new SchematicDocument(
            SchemaVersion: "2.1",
            Name: docName,
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: docName,
                Components: components,
                Nets: nets,
                Annotations: directives.Count > 0 ? directives : null),
            EmbeddedModels: embeddedModels.Count > 0 ? embeddedModels : null);
    }

    private static bool LooksLikeElement(string line)
    {
        var t = SplitTokens(line);
        if (t.Length < 4) return false;
        char c = char.ToUpperInvariant(t[0][0]);
        return "RCLVIDMQJEGX".Contains(c, StringComparison.Ordinal) && t[0].Length <= 12;
    }

    private static string[] SplitTokens(string line) =>
        line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static Dictionary<string, double> ParseSourceParams(string[] tokens, string dcKey)
    {
        var d = new Dictionary<string, double>();
        for (int i = 3; i < tokens.Length; ++i)
        {
            if (tokens[i].Equals("DC", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                d[dcKey] = ParseSpiceNumber(tokens[++i], 0.0);
            }
            else if (tokens[i].Equals("AC", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Length)
            {
                d["ac_mag"] = ParseSpiceNumber(tokens[++i], 1.0);
            }
            else if (!d.ContainsKey(dcKey) && TryParseSpiceNumber(tokens[i], out var v))
            {
                d[dcKey] = v;
            }
        }
        if (!d.ContainsKey(dcKey)) d[dcKey] = 0.0;
        return d;
    }

    private static double ParseSpiceNumber(string token, double fallback) =>
        TryParseSpiceNumber(token, out var val) ? val : fallback;

    private static bool TryParseSpiceNumber(string token, out double value)
    {
        value = 0.0;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var m = Regex.Match(token.Trim(), @"^([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*([a-zA-Z]*)$");
        if (!m.Success) return false;
        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return false;
        var suf = m.Groups[2].Value.ToLowerInvariant();
        double scale = suf switch
        {
            "" => 1.0,
            "f" => 1e-15,
            "p" => 1e-12,
            "n" => 1e-9,
            "u" => 1e-6,
            "m" => 1e-3,
            "k" => 1e3,
            "meg" => 1e6,
            "g" => 1e9,
            "t" => 1e12,
            _ when suf.StartsWith("meg", StringComparison.Ordinal) => 1e6,
            _ when suf.StartsWith('k') => 1e3,
            _ when suf.StartsWith('m') => 1e-3,
            _ when suf.StartsWith('u') => 1e-6,
            _ when suf.StartsWith('n') => 1e-9,
            _ when suf.StartsWith('p') => 1e-12,
            _ => 1.0
        };
        value = num * scale;
        return double.IsFinite(value);
    }

    private static string FormatBomValue(PlacedComponent comp)
    {
        if (comp.Parameters.TryGetValue("ohms", out var r)) return SchematicCompiler.FormatEngineering(r, "Ohm");
        if (comp.Parameters.TryGetValue("farads", out var c)) return SchematicCompiler.FormatEngineering(c, "F");
        if (comp.Parameters.TryGetValue("henries", out var l)) return SchematicCompiler.FormatEngineering(l, "H");
        if (comp.Parameters.TryGetValue("dc_volts", out var v)) return SchematicCompiler.FormatEngineering(v, "V");
        if (comp.Parameters.TryGetValue("dc_amps", out var a)) return SchematicCompiler.FormatEngineering(a, "A");
        return comp.ModelName ?? "";
    }

    private static string CsvEscape(string s) =>
        s.Contains(',', StringComparison.Ordinal) || s.Contains('"', StringComparison.Ordinal)
            ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : s;

    private static string Sha256Hex(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static string SanitizeId(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-') sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "circuit";
    }
}
