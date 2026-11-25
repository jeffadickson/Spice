using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cameo.Netlist;

/// <summary>
/// Standalone, zero-dependency VeloSim ingestion codec (format: velosim.netlist.v1)
/// bridging Cameo.Netlist (spice.netlist.v3 / .spicenet) into VeloSim SchematicLab
/// analog CircuitJob and VeloSim.Core structural velosim.rtl.v1 digital/mixed-signal
/// runtime, plus a derived tscircuit-aligned Circuit JSON flat element view.
/// </summary>
public static class VeloSimIngestion
{
    public const string VeloSimFormatId = "velosim.netlist.v1";
    public const string VeloSimRtlFormatId = "velosim.rtl.v1";
    public const int MaxIngestionBytes = 4 * 1024 * 1024;
    public const int MaxJsonDepth = 64;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = MaxJsonDepth
    };

    public sealed record VeloSimAnalogElement(
        string Id,
        string KindId,
        [property: JsonPropertyName("velosimKind")] string VeloSimKind,
        IReadOnlyDictionary<string, string> PinNets,
        IReadOnlyDictionary<string, int> PinNodeIndices,
        IReadOnlyDictionary<string, double> Parameters,
        string? ModelName = null,
        string? LibraryPartId = null,
        string? ControlSourceId = null,
        string? Footprint = null,
        string? Mpn = null,
        string? SupplierPartNumber = null);

    public sealed record VeloSimPinnedModel(
        string Name,
        string Kind,
        string SpiceText,
        string Sha256,
        string? OsdiLibraryPath = null);

    public sealed record VeloSimAnalogDomain(
        IReadOnlyList<string> Nodes,
        IReadOnlyList<VeloSimAnalogElement> Elements,
        IReadOnlyList<VeloSimPinnedModel> Models,
        IReadOnlyList<string> Directives,
        string SpiceDeck);

    public sealed record VeloSimRtlSignal(
        string Name,
        int Width,
        string Direction,
        bool Signed = false,
        bool TwoState = false);

    public sealed record VeloSimRtlModuleEnvelope(
        string Name,
        string Format,
        string? Clock,
        IReadOnlyList<VeloSimRtlSignal> Signals,
        JsonElement Memories,
        JsonElement Continuous,
        JsonElement Body);

    public sealed record VeloSimNetConnectivity(
        string NetName,
        int NodeIndex,
        bool IsGround,
        IReadOnlyList<string> Pins);

    public sealed record VeloSimIngestionPackage(
        string Format,
        string SourceFormat,
        string DesignName,
        string ElectricalSha256,
        string LayoutSha256,
        VeloSimAnalogDomain Analog,
        IReadOnlyList<VeloSimRtlModuleEnvelope> Digital,
        IReadOnlyList<FlagshipMixedBridge> Bridges,
        IReadOnlyList<VeloSimNetConnectivity> Connectivity,
        FlagshipPcbSection? Pcb = null);

    /// <summary>
    /// Exports a deterministic, timestamp-free velosim.netlist.v1 JSON package for
    /// VeloSim SchematicLab + VeloSim.Core structural RTL co-simulation ingestion.
    /// </summary>
    public static string ExportVeloSimNetlistJson(
        FlagshipNetlistDocument document,
        Func<string, NetlistPartDefaults?>? libraryResolver = null,
        string? canonicalSpiceDeck = null)
    {
        var stamped = FlagshipNetlist.WithHashes(document);
        var elab = FlagshipNetlist.Elaborate(stamped, libraryResolver);
        if (!elab.Succeeded)
        {
            throw new InvalidOperationException(
                "Cannot export VeloSim ingestion package from invalid netlist: " +
                string.Join("; ", elab.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        }

        var nodeIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["0"] = 0, ["GND"] = 0 };
        var nodes = new List<string> { "0" };
        foreach (var net in elab.Nets.OrderBy(n => n.IsGround ? "" : n.Name, StringComparer.Ordinal))
        {
            if (net.IsGround || string.Equals(net.Name, "0", StringComparison.Ordinal) ||
                string.Equals(net.Name, "GND", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!nodeIndex.ContainsKey(net.Name))
            {
                nodeIndex[net.Name] = nodes.Count;
                nodes.Add(net.Name);
            }
        }

        var pinToNet = new Dictionary<(string Inst, string Pin), string>();
        foreach (var net in elab.Nets)
        {
            var effNet = net.IsGround ? "0" : net.Name;
            foreach (var pr in net.Pins)
            {
                pinToNet[(pr.InstanceId, pr.PinName)] = effNet;
            }
        }

        var elements = new List<VeloSimAnalogElement>();
        foreach (var part in elab.Parts.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var pNets = new Dictionary<string, string>(StringComparer.Ordinal);
            var pIdx = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ((inst, pin), netName) in pinToNet)
            {
                if (string.Equals(inst, part.Id, StringComparison.Ordinal))
                {
                    pNets[pin] = netName;
                    pIdx[pin] = nodeIndex.TryGetValue(netName, out var idx) ? idx : 0;
                }
            }
            elements.Add(new VeloSimAnalogElement(
                Id: part.Id,
                KindId: part.KindId,
                VeloSimKind: MapToVeloSimKind(part, elab.Models),
                PinNets: pNets,
                PinNodeIndices: pIdx,
                Parameters: part.Parameters,
                ModelName: part.ModelName,
                LibraryPartId: part.LibraryPartId,
                ControlSourceId: part.ControlSourceId,
                Footprint: part.Footprint,
                Mpn: part.Mpn,
                SupplierPartNumber: part.SupplierPartNumber));
        }

        var pinnedModels = elab.Models
            .OrderBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase)
            .Select(m => new VeloSimPinnedModel(
                Name: m.ModelName,
                Kind: m.Kind.ToString(),
                SpiceText: m.SpiceText.Trim(),
                Sha256: Sha256Hex(m.SpiceText.Trim()),
                OsdiLibraryPath: m.OsdiLibraryPath))
            .ToList();

        var digital = new List<VeloSimRtlModuleEnvelope>();
        using var emptyArrDoc = JsonDocument.Parse("[]");
        using var emptyBodyDoc = JsonDocument.Parse("{\"kind\":\"block\",\"items\":[]}");
        var emptyArr = emptyArrDoc.RootElement.Clone();
        var emptyBody = emptyBodyDoc.RootElement.Clone();

        foreach (var rtl in elab.RtlBlocks.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            var signals = rtl.Ports.Select(p => new VeloSimRtlSignal(
                Name: p.Name,
                Width: Math.Clamp(p.Width, 1, 64),
                Direction: NormalizeRtlDirection(p.Direction))).ToList();

            var memories = emptyArr;
            var continuous = emptyArr;
            var body = emptyBody;
            if (!string.IsNullOrWhiteSpace(rtl.RawRtlJson))
            {
                using var parsedRtl = JsonDocument.Parse(rtl.RawRtlJson);
                var root = parsedRtl.RootElement;
                if (root.TryGetProperty("memories", out var memEl)) memories = memEl.Clone();
                if (root.TryGetProperty("continuous", out var contEl)) continuous = contEl.Clone();
                if (root.TryGetProperty("body", out var bodyEl)) body = bodyEl.Clone();
            }

            digital.Add(new VeloSimRtlModuleEnvelope(
                Name: rtl.Name,
                Format: VeloSimRtlFormatId,
                Clock: rtl.Clock,
                Signals: signals,
                Memories: memories,
                Continuous: continuous,
                Body: body));
        }

        var connectivity = elab.Nets
            .OrderBy(n => n.IsGround ? "" : n.Name, StringComparer.Ordinal)
            .Select(n => new VeloSimNetConnectivity(
                NetName: n.IsGround ? "0" : n.Name,
                NodeIndex: n.IsGround ? 0 : nodeIndex.GetValueOrDefault(n.Name, 0),
                IsGround: n.IsGround,
                Pins: n.Pins
                    .OrderBy(p => p.InstanceId, StringComparer.Ordinal)
                    .ThenBy(p => p.PinName, StringComparer.Ordinal)
                    .Select(p => $"{p.InstanceId}.{p.PinName}")
                    .ToList()))
            .ToList();

        var package = new VeloSimIngestionPackage(
            Format: VeloSimFormatId,
            SourceFormat: FlagshipNetlist.FormatId,
            DesignName: stamped.Name,
            ElectricalSha256: stamped.ElectricalSha256!,
            LayoutSha256: stamped.LayoutSha256!,
            Analog: new VeloSimAnalogDomain(nodes, elements, pinnedModels, elab.Directives, canonicalSpiceDeck ?? ""),
            Digital: digital,
            Bridges: elab.Bridges,
            Connectivity: connectivity,
            Pcb: stamped.Pcb);

        return JsonSerializer.Serialize(package, JsonOpts);
    }

    /// <summary>
    /// Fail-closed importer reading a velosim.netlist.v1 ingestion package back into a
    /// FlagshipNetlistDocument (with bounded byte size, JSON depth, and model SHA-256 verification).
    /// </summary>
    public static FlagshipParseResult ImportVeloSimNetlistJson(
        string json,
        Func<string, NetlistPartDefaults?>? libraryResolver = null,
        bool verifyModelHashes = true)
    {
        var diags = new List<FlagshipDiagnostic>();
        if (string.IsNullOrWhiteSpace(json))
        {
            diags.Add(new FlagshipDiagnostic("error", "empty_velosim_json", "VeloSim ingestion JSON is empty."));
            return new FlagshipParseResult(false, null, diags);
        }
        if (Encoding.UTF8.GetByteCount(json) > MaxIngestionBytes)
        {
            diags.Add(new FlagshipDiagnostic("error", "velosim_json_too_large", $"VeloSim JSON exceeds bounded limit of {MaxIngestionBytes} bytes."));
            return new FlagshipParseResult(false, null, diags);
        }

        VeloSimIngestionPackage? pkg;
        try
        {
            pkg = JsonSerializer.Deserialize<VeloSimIngestionPackage>(json, JsonOpts);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            diags.Add(new FlagshipDiagnostic("error", "malformed_velosim_json", ex.Message));
            return new FlagshipParseResult(false, null, diags);
        }

        if (pkg is null || !string.Equals(pkg.Format, VeloSimFormatId, StringComparison.Ordinal))
        {
            diags.Add(new FlagshipDiagnostic("error", "unsupported_velosim_format", $"Expected format '{VeloSimFormatId}', got '{pkg?.Format ?? "<null>"}'."));
            return new FlagshipParseResult(false, null, diags);
        }
        if (pkg.Analog is null || string.IsNullOrWhiteSpace(pkg.DesignName))
        {
            diags.Add(new FlagshipDiagnostic("error", "missing_analog_domain", "VeloSim ingestion package is missing designName or analog domain."));
            return new FlagshipParseResult(false, null, diags);
        }

        var models = new List<NetlistModelDef>();
        foreach (var m in pkg.Analog.Models)
        {
            if (verifyModelHashes && !string.IsNullOrWhiteSpace(m.Sha256))
            {
                var actual = Sha256Hex(m.SpiceText.Trim());
                if (!string.Equals(actual, m.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    diags.Add(new FlagshipDiagnostic("error", "model_sha256_mismatch", $"Pinned model '{m.Name}' failed SHA-256 verification.", SubjectId: m.Name));
                    return new FlagshipParseResult(false, null, diags);
                }
            }
            var kind = Enum.TryParse<NetlistModelKind>(m.Kind, ignoreCase: true, out var parsed)
                ? parsed
                : NetlistModelKind.SpiceModelCard;
            models.Add(new NetlistModelDef(kind, m.Name, m.SpiceText, m.OsdiLibraryPath));
        }

        var parts = pkg.Analog.Elements.Select(e => new FlagshipPartDef(
            Id: e.Id,
            KindId: e.KindId,
            Parameters: e.Parameters,
            ModelName: e.ModelName,
            LibraryPartId: e.LibraryPartId,
            ControlSourceId: e.ControlSourceId,
            Footprint: e.Footprint,
            Mpn: e.Mpn,
            SupplierPartNumber: e.SupplierPartNumber)).ToList();

        var nets = new List<FlagshipNetDef>();
        foreach (var c in pkg.Connectivity)
        {
            var pins = new List<NetlistPinRef>();
            foreach (var pStr in c.Pins)
            {
                var dot = pStr.IndexOf('.');
                if (dot > 0 && dot < pStr.Length - 1)
                {
                    pins.Add(new NetlistPinRef(pStr[..dot], pStr[(dot + 1)..]));
                }
            }
            nets.Add(new FlagshipNetDef(c.IsGround ? "GND" : c.NetName, pins, c.IsGround));
        }

        var rtlBlocks = pkg.Digital.Select(d => new FlagshipRtlBlock(
            Name: d.Name,
            Format: d.Format,
            Clock: d.Clock,
            Ports: d.Signals.Select(s => new FlagshipPortDef(s.Name, s.Direction, s.Width)).ToList(),
            Bridges: pkg.Bridges.Where(b => d.Signals.Any(s => string.Equals(s.Name, b.DigitalSignal, StringComparison.OrdinalIgnoreCase))).ToList())).ToList();

        var circuit = new FlagshipModuleDef(
            Name: "__circuit__",
            DefaultParameters: new Dictionary<string, double>(),
            Ports: [],
            Parts: parts,
            Uses: [],
            Nets: nets,
            Chains: [],
            Directives: pkg.Analog.Directives);

        var doc = FlagshipNetlist.WithHashes(new FlagshipNetlistDocument(
            Name: pkg.DesignName,
            Circuit: circuit,
            Modules: [],
            RtlBlocks: rtlBlocks,
            Models: models,
            Layout: null,
            Pcb: pkg.Pcb));

        if (libraryResolver is not null)
        {
            var elab = FlagshipNetlist.Elaborate(doc, libraryResolver);
            diags.AddRange(elab.Diagnostics);
            if (!elab.Succeeded) return new FlagshipParseResult(false, null, diags);
        }

        return new FlagshipParseResult(true, doc, diags);
    }

    /// <summary>
    /// Exports a derived read-only tscircuit-aligned Circuit JSON flat element array
    /// (source_component, source_port, source_net, source_trace, schematic_component,
    /// pcb_board, pcb_component) with zero schematic/UI dependencies.
    /// </summary>
    public static string ExportCircuitJson(
        FlagshipNetlistDocument document,
        Func<string, NetlistPartDefaults?>? libraryResolver = null)
    {
        var elab = FlagshipNetlist.Elaborate(document, libraryResolver);
        if (!elab.Succeeded)
        {
            throw new InvalidOperationException("Cannot export Circuit JSON for invalid netlist.");
        }

        var explicitMap = (document.Layout?.Placements ?? [])
            .ToDictionary(p => p.InstanceId, StringComparer.OrdinalIgnoreCase);
        var autoLayout = FlagshipNetlist.ComputeSignalFlowLayout(
            elab.Parts,
            elab.Nets,
            document.Layout is { GridPitch: > 2.0 } ? document.Layout.GridPitch : 20.0);

        var elements = new List<object>();

        if (document.Pcb is not null)
        {
            elements.Add(new
            {
                type = "pcb_board",
                pcb_board_id = $"pcb_board_{document.Name}",
                name = document.Name,
                width = document.Pcb.BoardWidthMm ?? 50.0,
                height = document.Pcb.BoardHeightMm ?? 30.0,
                num_layers = document.Pcb.LayerCount ?? 2
            });
        }

        var portIdMap = new Dictionary<(string Inst, string Pin), string>();
        foreach (var part in elab.Parts.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            var srcId = $"source_component_{part.Id}";
            var place = explicitMap.TryGetValue(part.Id, out var pl) ? pl : autoLayout[part.Id];

            elements.Add(new
            {
                type = "source_component",
                source_component_id = srcId,
                name = part.Id,
                ftype = MapToCircuitJsonFtype(part.KindId),
                spice_kind_id = part.KindId,
                model_name = part.ModelName,
                library_part_id = part.LibraryPartId,
                footprint = part.Footprint,
                manufacturer_part_number = part.Mpn,
                supplier_part_numbers = !string.IsNullOrWhiteSpace(part.SupplierPartNumber)
                    ? new Dictionary<string, string[]> { ["jlcpcb"] = [part.SupplierPartNumber] }
                    : null,
                parameters = part.Parameters
            });

            elements.Add(new
            {
                type = "schematic_component",
                schematic_component_id = $"schematic_component_{part.Id}",
                source_component_id = srcId,
                center = new { x = place.X, y = place.Y },
                rotation = place.RotationDegrees
            });

            if (!string.IsNullOrWhiteSpace(part.Footprint))
            {
                elements.Add(new
                {
                    type = "pcb_component",
                    pcb_component_id = $"pcb_component_{part.Id}",
                    source_component_id = srcId,
                    footprint = part.Footprint,
                    layer = "top"
                });
            }
        }

        foreach (var net in elab.Nets.OrderBy(n => n.IsGround ? "" : n.Name, StringComparer.Ordinal))
        {
            var netName = net.IsGround ? "GND" : net.Name;
            var netId = $"source_net_{netName}";
            elements.Add(new
            {
                type = "source_net",
                source_net_id = netId,
                name = netName,
                is_ground = net.IsGround
            });

            var connectedPortIds = new List<string>();
            foreach (var pr in net.Pins.OrderBy(p => p.InstanceId, StringComparer.Ordinal).ThenBy(p => p.PinName, StringComparer.Ordinal))
            {
                if (!portIdMap.TryGetValue((pr.InstanceId, pr.PinName), out var portId))
                {
                    portId = $"source_port_{pr.InstanceId}_{pr.PinName}";
                    portIdMap[(pr.InstanceId, pr.PinName)] = portId;
                    elements.Add(new
                    {
                        type = "source_port",
                        source_port_id = portId,
                        source_component_id = $"source_component_{pr.InstanceId}",
                        name = pr.PinName
                    });
                }
                connectedPortIds.Add(portId);
            }

            if (connectedPortIds.Count >= 2)
            {
                elements.Add(new
                {
                    type = "source_trace",
                    source_trace_id = $"source_trace_{netName}",
                    connected_source_port_ids = connectedPortIds,
                    connected_source_net_ids = new[] { netId }
                });
            }
        }

        return JsonSerializer.Serialize(elements, JsonOpts);
    }

    private static string MapToVeloSimKind(FlagshipPartDef part, IReadOnlyList<NetlistModelDef> models)
    {
        var modelText = models.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(part.ModelName) &&
            string.Equals(m.ModelName, part.ModelName, StringComparison.OrdinalIgnoreCase))?.SpiceText ?? "";

        return part.KindId switch
        {
            "spice.vsrc" => "Supply",
            "spice.isrc" => "Current",
            "spice.resistor" => "Resistor",
            "spice.capacitor" => "Capacitor",
            "spice.inductor" => "Inductor",
            "spice.core" => "SaturableInductor",
            "spice.diode" => "Diode",
            "spice.bjt" => modelText.Contains("PNP", StringComparison.OrdinalIgnoreCase) ? "Pnp" : "Npn",
            "spice.mosfet" => modelText.Contains("PMOS", StringComparison.OrdinalIgnoreCase) || part.Parameters.GetValueOrDefault("vth0_volts", 0.4) < 0 ? "Pmos" : "Nmos",
            "spice.vdmos" => modelText.Contains("PCHAN", StringComparison.OrdinalIgnoreCase) || part.Parameters.GetValueOrDefault("vto_volts", 3.0) < 0 ? "Pvdmos" : "Nvdmos",
            "spice.jfet" => modelText.Contains("PJF", StringComparison.OrdinalIgnoreCase) ? "Pjfet" : "Njfet",
            "spice.mesfet" => modelText.Contains("PMF", StringComparison.OrdinalIgnoreCase) ? "Pmesfet" : "Nmesfet",
            "spice.vcvs" => "Vcvs",
            "spice.vccs" => "Vccs",
            "spice.switch" => "Switch",
            "spice.cswitch" => "CurrentSwitch",
            "spice.subckt" or "spice.osdi" => "CellInstance",
            _ => "CellInstance"
        };
    }

    private static string MapToCircuitJsonFtype(string kindId) => kindId switch
    {
        "spice.resistor" => "simple_resistor",
        "spice.capacitor" => "simple_capacitor",
        "spice.inductor" or "spice.core" => "simple_inductor",
        "spice.diode" => "simple_diode",
        "spice.mosfet" or "spice.vdmos" => "simple_mosfet",
        "spice.bjt" or "spice.jfet" or "spice.mesfet" => "simple_transistor",
        "spice.vsrc" => "simple_power_source",
        "spice.isrc" => "simple_current_source",
        "spice.switch" or "spice.cswitch" => "simple_switch",
        _ => "simple_chip"
    };

    private static string NormalizeRtlDirection(string dir) => dir.ToLowerInvariant() switch
    {
        "in" or "input" => "input",
        "out" or "output" => "output",
        _ => "internal"
    };

    private static string Sha256Hex(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
