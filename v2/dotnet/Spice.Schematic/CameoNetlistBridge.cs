using Cameo.Netlist;
using Spice.Ir;

namespace Spice.Schematic;

/// <summary>
/// Thin adapter bridging the standalone zero-dependency Cameo.Netlist package
/// (spice.netlist.v3 / .spicenet, velosim.netlist.v1, Circuit JSON) with
/// Spice.Schematic (SchematicDocument, ComponentLibraryStore, ERC, SVG rendering)
/// and Spice.Ir (Canonical CircuitIr).
/// </summary>
public static class CameoNetlistBridge
{
    /// <summary>
    /// Creates a library part resolver callback for Cameo.Netlist so Cameo.Netlist
    /// remains strictly zero-dependency while still resolving Spice.Schematic's
    /// 34 Foundation models and persistent custom user parts.
    /// </summary>
    public static Func<string, NetlistPartDefaults?>? CreateLibraryResolver(ComponentLibraryStore? library)
    {
        if (library is null) return null;
        return partId =>
        {
            if (!library.TryGet(partId, out var part) || part is null) return null;
            NetlistModelDef? mdl = part.AttachedModel is null
                ? null
                : ToNetlistModel(part.AttachedModel);
            return new NetlistPartDefaults(part.DefaultParameters, mdl);
        };
    }

    public static FlagshipParseResult Parse(
        string text,
        ComponentLibraryStore? library = null,
        bool verifyHashes = false) =>
        FlagshipNetlist.Parse(text, CreateLibraryResolver(library), verifyHashes);

    public static FlagshipParseResult ExtractFromPolyglotSvg(
        string svgText,
        ComponentLibraryStore? library = null,
        bool verifyHashes = true) =>
        FlagshipNetlist.ExtractFromPolyglotSvg(svgText, CreateLibraryResolver(library), verifyHashes);

    public static FlagshipParseResult ImportVeloSimNetlistJson(
        string json,
        ComponentLibraryStore? library = null,
        bool verifyModelHashes = true) =>
        VeloSimIngestion.ImportVeloSimNetlistJson(json, CreateLibraryResolver(library), verifyModelHashes);

    public static ElaboratedNetlist Elaborate(
        FlagshipNetlistDocument document,
        ComponentLibraryStore? library = null) =>
        FlagshipNetlist.Elaborate(document, CreateLibraryResolver(library));

    /// <summary>
    /// Converts a Cameo.Netlist FlagshipNetlistDocument into a renderable, simulatable
    /// SchematicDocument, honoring explicit layout placements and applying Cameo.Netlist's
    /// deterministic left-to-right signal-flow auto-placer for unplaced parts.
    /// </summary>
    public static SchematicDocument ToSchematicDocument(
        FlagshipNetlistDocument document,
        ComponentLibraryStore? library = null)
    {
        var elab = Elaborate(document, library);
        if (!elab.Succeeded)
        {
            throw new InvalidOperationException(
                "Flagship netlist elaboration failed: " +
                string.Join("; ", elab.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        }

        double grid = document.Layout is { GridPitch: > 2.0 } ? document.Layout.GridPitch : 20.0;
        string style = document.Layout?.GridStyle ?? "lines";
        var explicitMap = (document.Layout?.Placements ?? [])
            .ToDictionary(p => p.InstanceId, StringComparer.OrdinalIgnoreCase);

        var autoCoords = FlagshipNetlist.ComputeSignalFlowLayout(elab.Parts, elab.Nets, grid);

        var placed = new List<PlacedComponent>();
        foreach (var p in elab.Parts)
        {
            var pl = explicitMap.TryGetValue(p.Id, out var explicitPl) ? explicitPl : autoCoords[p.Id];
            placed.Add(new PlacedComponent(
                InstanceId: p.Id,
                DeviceKindId: p.KindId,
                X: pl.X,
                Y: pl.Y,
                RotationDegrees: pl.RotationDegrees,
                Parameters: p.Parameters,
                MirrorX: pl.MirrorX,
                MirrorY: pl.MirrorY,
                ModelName: p.ModelName,
                LibraryPartId: p.LibraryPartId,
                DisplayValue: p.DisplayValue,
                ControlSourceId: p.ControlSourceId));
        }

        var wireNets = elab.Nets
            .Select(n => new WireNet(n.Name, n.Pins.Select(pr => new PinRef(pr.InstanceId, pr.PinName)).ToList(), n.IsGround))
            .ToList();

        var annotations = elab.Directives.Count > 0
            ? elab.Directives.Select((d, idx) => new SheetAnnotation($"dir_{idx + 1}", 80.0, 280.0 + idx * 24.0, d, IsSpiceDirective: true)).ToList()
            : null;

        var ports = elab.Ports.Count > 0
            ? elab.Ports.Select((pt, idx) => new SheetPort(pt.Name, pt.Direction == "in" ? 40.0 : 480.0, 80.0 + idx * 40.0, pt.Direction)).ToList()
            : null;

        var embeddedModels = elab.Models.Count > 0
            ? elab.Models.Select(ToAttachedModel).ToList()
            : null;

        var doc = new SchematicDocument(
            SchemaVersion: SchematicCompiler.CurrentSchemaVersion,
            Name: document.Name,
            RootSheet: new SchematicSheet(
                SheetId: "root",
                Title: document.Name,
                Components: placed,
                Nets: wireNets,
                Annotations: annotations,
                Ports: ports,
                GridPitch: grid,
                GridStyle: style),
            EmbeddedModels: embeddedModels);

        return SchematicCompiler.EmbedReferencedLibraryAssets(doc, library);
    }

    public static SchematicCompileResult CompileToIr(
        FlagshipNetlistDocument document,
        DeviceRegistry? registry = null,
        ComponentLibraryStore? library = null)
    {
        var elab = Elaborate(document, library);
        if (!elab.Succeeded)
        {
            return new SchematicCompileResult(
                false,
                null,
                elab.Diagnostics.Select(d => new ErcDiagnostic(ErcSeverity.Error, d.Code, d.Message, SubjectId: d.SubjectId)).ToList());
        }
        var schem = ToSchematicDocument(document, library);
        return SchematicCompiler.CompileToIr(schem, registry, library);
    }

    /// <summary>
    /// Lifts any single-sheet or multi-sheet SchematicDocument into a Cameo.Netlist
    /// FlagshipNetlistDocument (spice.netlist.v3) with dual electrical/layout SHA-256 hashes.
    /// </summary>
    public static FlagshipNetlistDocument FromSchematic(
        SchematicDocument document,
        ComponentLibraryStore? library = null)
    {
        var enriched = SchematicCompiler.EmbedReferencedLibraryAssets(document, library);
        var parts = new List<FlagshipPartDef>();
        var nets = new Dictionary<string, (bool IsGround, List<NetlistPinRef> Pins)>(StringComparer.OrdinalIgnoreCase);
        var placements = new List<FlagshipPlacement>();
        var directives = new List<string>();
        var ports = (enriched.RootSheet.Ports ?? [])
            .Select(p => new FlagshipPortDef(p.Name, p.Direction, 1))
            .ToList();

        var sheets = new List<SchematicSheet> { enriched.RootSheet };
        if (enriched.SubSheets is not null) sheets.AddRange(enriched.SubSheets);

        foreach (var sheet in sheets)
        {
            foreach (var c in sheet.Components)
            {
                var canonId = SchematicCompiler.ResolveInstanceId(enriched, sheet, c);
                parts.Add(new FlagshipPartDef(
                    Id: canonId,
                    KindId: c.DeviceKindId,
                    Parameters: new Dictionary<string, double>(c.Parameters, StringComparer.Ordinal),
                    ModelName: c.ModelName,
                    LibraryPartId: c.LibraryPartId,
                    ControlSourceId: c.ControlSourceId,
                    DisplayValue: c.DisplayValue));
                placements.Add(new FlagshipPlacement(canonId, c.X, c.Y, c.RotationDegrees, c.MirrorX, c.MirrorY));
            }

            foreach (var net in sheet.Nets)
            {
                var eff = SchematicCompiler.ResolveNetName(enriched, sheet, net);
                bool isGnd = eff == "0";
                var key = isGnd ? "GND" : eff;
                if (!nets.TryGetValue(key, out var bucket))
                {
                    bucket = (isGnd, new List<NetlistPinRef>());
                    nets[key] = bucket;
                }
                foreach (var pr in net.ConnectedPins)
                {
                    var comp = sheet.Components.FirstOrDefault(x => string.Equals(x.InstanceId, pr.InstanceId, StringComparison.OrdinalIgnoreCase));
                    var instId = comp is not null ? SchematicCompiler.ResolveInstanceId(enriched, sheet, comp) : pr.InstanceId;
                    var scopedPin = new NetlistPinRef(instId, pr.PinName);
                    if (!bucket.Pins.Contains(scopedPin)) bucket.Pins.Add(scopedPin);
                }
            }

            if (sheet.Annotations is not null)
            {
                foreach (var ann in sheet.Annotations)
                {
                    if (ann.IsSpiceDirective && !string.IsNullOrWhiteSpace(ann.Text))
                        directives.Add(ann.Text.Trim());
                }
            }
        }

        var circuit = new FlagshipModuleDef(
            Name: "__circuit__",
            DefaultParameters: new Dictionary<string, double>(),
            Ports: ports,
            Parts: parts,
            Uses: [],
            Nets: nets.Select(kv => new FlagshipNetDef(kv.Key, kv.Value.Pins, kv.Value.IsGround)).ToList(),
            Chains: [],
            Directives: directives);

        var layout = new FlagshipLayout(enriched.RootSheet.GridPitch, enriched.RootSheet.GridStyle, placements);
        var models = (enriched.EmbeddedModels ?? []).Select(ToNetlistModel).ToList();

        return FlagshipNetlist.WithHashes(new FlagshipNetlistDocument(
            Name: enriched.Name,
            Circuit: circuit,
            Modules: [],
            RtlBlocks: [],
            Models: models,
            Layout: layout));
    }

    public static string ExportPolyglotSvg(
        SchematicDocument document,
        OperatingPointOverlay? opOverlay = null,
        ComponentLibraryStore? library = null,
        bool lightTheme = false,
        string? gridStyle = null)
    {
        var flagship = FromSchematic(document, library);
        var svg = SchematicCompiler.ExportSvg(document, opOverlay, library, lightTheme, gridStyle);
        return FlagshipNetlist.EmbedIntoSvg(svg, flagship);
    }

    public static string ExportPolyglotSvg(
        FlagshipNetlistDocument document,
        OperatingPointOverlay? opOverlay = null,
        ComponentLibraryStore? library = null,
        bool lightTheme = false,
        string? gridStyle = null)
    {
        var schem = ToSchematicDocument(document, library);
        var svg = SchematicCompiler.ExportSvg(schem, opOverlay, library, lightTheme, gridStyle);
        return FlagshipNetlist.EmbedIntoSvg(svg, document);
    }

    public static string ExportVeloSimNetlistJson(
        FlagshipNetlistDocument document,
        ComponentLibraryStore? library = null)
    {
        var schem = ToSchematicDocument(document, library);
        var compiled = SchematicCompiler.CompileToIr(schem, library: library);
        var deck = compiled.Succeeded && compiled.Circuit is not null
            ? SchematicExporter.ExportNetlist(
                schem,
                new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, AnalysisKind.OperatingPoint),
                library: library)
            : "";
        return VeloSimIngestion.ExportVeloSimNetlistJson(document, CreateLibraryResolver(library), deck);
    }

    public static string ExportVeloSimNetlistJson(
        SchematicDocument document,
        ComponentLibraryStore? library = null) =>
        ExportVeloSimNetlistJson(FromSchematic(document, library), library);

    public static string ExportCircuitJson(
        FlagshipNetlistDocument document,
        ComponentLibraryStore? library = null) =>
        VeloSimIngestion.ExportCircuitJson(document, CreateLibraryResolver(library));

    public static string ExportCircuitJson(
        SchematicDocument document,
        ComponentLibraryStore? library = null) =>
        ExportCircuitJson(FromSchematic(document, library), library);

    private static NetlistModelDef ToNetlistModel(AttachedModelDefinition m) =>
        new(
            Kind: m.Kind switch
            {
                AttachedModelKind.Subcircuit => NetlistModelKind.Subcircuit,
                AttachedModelKind.Osdi => NetlistModelKind.Osdi,
                _ => NetlistModelKind.SpiceModelCard
            },
            ModelName: m.ModelName,
            SpiceText: m.SpiceText,
            OsdiLibraryPath: m.OsdiLibraryPath,
            Description: m.Description);

    private static AttachedModelDefinition ToAttachedModel(NetlistModelDef m) =>
        new(
            Kind: m.Kind switch
            {
                NetlistModelKind.Subcircuit => AttachedModelKind.Subcircuit,
                NetlistModelKind.Osdi => AttachedModelKind.Osdi,
                _ => AttachedModelKind.SpiceModelCard
            },
            ModelName: m.ModelName,
            SpiceText: m.SpiceText,
            OsdiLibraryPath: m.OsdiLibraryPath,
            Description: m.Description);
}
