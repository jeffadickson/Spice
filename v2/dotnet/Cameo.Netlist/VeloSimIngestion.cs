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
        foreach (var m in pkg.Analog.Models ?? [])
        {
            if (m is null || string.IsNullOrWhiteSpace(m.Name) || m.SpiceText is null)
            {
                diags.Add(new FlagshipDiagnostic("error", "malformed_velosim_model", "VeloSim analog model entry is missing name or spiceText."));
                return new FlagshipParseResult(false, null, diags);
            }
            if (verifyModelHashes && !string.IsNullOrWhiteSpace(m.Sha256))
            {
                var actual = Sha256Hex(m.SpiceText.Trim());
                if (!string.Equals(actual, m.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    diags.Add(new FlagshipDiagnostic("error", "model_sha256_mismatch", $"Pinned model '{m.Name}' failed SHA-256 verification.", SubjectId: m.Name));
                    return new FlagshipParseResult(false, null, diags);
                }
            }
            var kind = Enum.TryParse<NetlistModelKind>(m.Kind ?? "", ignoreCase: true, out var parsed)
                ? parsed
                : NetlistModelKind.SpiceModelCard;
            models.Add(new NetlistModelDef(kind, m.Name, m.SpiceText, m.OsdiLibraryPath));
        }

        if (pkg.Analog.Elements is null)
        {
            diags.Add(new FlagshipDiagnostic("error", "missing_analog_elements", "VeloSim analog domain is missing elements array."));
            return new FlagshipParseResult(false, null, diags);
        }

        var parts = new List<FlagshipPartDef>(pkg.Analog.Elements.Count);
        foreach (var e in pkg.Analog.Elements)
        {
            if (e is null || string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.KindId))
            {
                diags.Add(new FlagshipDiagnostic("error", "malformed_velosim_element", "VeloSim analog element is missing id or kindId."));
                return new FlagshipParseResult(false, null, diags);
            }
            parts.Add(new FlagshipPartDef(
                Id: e.Id,
                KindId: e.KindId,
                Parameters: e.Parameters is not null ? new Dictionary<string, double>(e.Parameters, StringComparer.Ordinal) : new Dictionary<string, double>(StringComparer.Ordinal),
                ModelName: e.ModelName,
                LibraryPartId: e.LibraryPartId,
                ControlSourceId: e.ControlSourceId,
                Footprint: e.Footprint,
                Mpn: e.Mpn,
                SupplierPartNumber: e.SupplierPartNumber));
        }

        var nets = new List<FlagshipNetDef>();
        foreach (var c in pkg.Connectivity ?? [])
        {
            if (c is null || string.IsNullOrWhiteSpace(c.NetName)) continue;
            var pins = new List<NetlistPinRef>();
            foreach (var pStr in c.Pins ?? [])
            {
                if (string.IsNullOrEmpty(pStr)) continue;
                var dot = pStr.IndexOf('.');
                if (dot > 0 && dot < pStr.Length - 1)
                {
                    pins.Add(new NetlistPinRef(pStr[..dot], pStr[(dot + 1)..]));
                }
            }
            nets.Add(new FlagshipNetDef(c.IsGround ? "GND" : c.NetName, pins, c.IsGround));
        }

        var allBridges = (pkg.Bridges ?? []).Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Name)).ToList();
        var rtlBlocks = new List<FlagshipRtlBlock>();
        foreach (var d in pkg.Digital ?? [])
        {
            if (d is null || string.IsNullOrWhiteSpace(d.Name)) continue;
            var sigs = (d.Signals ?? []).Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name)).ToList();
            rtlBlocks.Add(new FlagshipRtlBlock(
                Name: d.Name,
                Format: string.IsNullOrWhiteSpace(d.Format) ? "velosim.rtl.v1" : d.Format,
                Clock: d.Clock,
                Ports: sigs.Select(s => new FlagshipPortDef(s.Name, string.IsNullOrWhiteSpace(s.Direction) ? "input" : s.Direction, s.Width)).ToList(),
                Bridges: allBridges.Where(b => sigs.Any(s => string.Equals(s.Name, b.DigitalSignal, StringComparison.OrdinalIgnoreCase))).ToList()));
        }

        var directives = (pkg.Analog.Directives ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var circuit = new FlagshipModuleDef(
            Name: "__circuit__",
            DefaultParameters: new Dictionary<string, double>(),
            Ports: [],
            Parts: parts,
            Uses: [],
            Nets: nets,
            Chains: [],
            Directives: directives);

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
        var pcbPlaceMap = (document.Pcb?.Placements ?? [])
            .ToDictionary(p => p.InstanceId, StringComparer.OrdinalIgnoreCase);
        var autoLayout = FlagshipNetlist.ComputeSignalFlowLayout(
            elab.Parts,
            elab.Nets,
            document.Layout is { GridPitch: > 2.0 } ? document.Layout.GridPitch : 20.0);

        var elements = new List<object>();

        if (document.Pcb is not null)
        {
            double bw = document.Pcb.BoardWidthMm ?? 50.0;
            double bh = document.Pcb.BoardHeightMm ?? 30.0;
            elements.Add(new
            {
                type = "pcb_board",
                pcb_board_id = $"pcb_board_{document.Name}",
                name = document.Name,
                center = new { x = bw * 0.5, y = bh * 0.5 },
                width = bw,
                height = bh,
                num_layers = document.Pcb.LayerCount ?? 2
            });
        }

        var portIdMap = new Dictionary<(string Inst, string Pin), string>();
        var padWorldPos = new Dictionary<(string Inst, string Pin), (double X, double Y, string PadId)>();
        var pinListByPart = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var net in elab.Nets)
        {
            foreach (var pr in net.Pins)
            {
                if (!pinListByPart.TryGetValue(pr.InstanceId, out var list))
                {
                    list = [];
                    pinListByPart[pr.InstanceId] = list;
                }
                if (!list.Contains(pr.PinName, StringComparer.OrdinalIgnoreCase)) list.Add(pr.PinName);
            }
        }

        int autoPcbIdx = 0;
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

            bool hasPcb = pcbPlaceMap.TryGetValue(part.Id, out var pcbPl) || !string.IsNullOrWhiteSpace(part.Footprint);
            if (hasPcb)
            {
                var fp = pcbPl?.Footprint ?? part.Footprint ?? "0603";
                double cx = pcbPl?.XMm ?? (5.0 + (autoPcbIdx % 8) * 6.0);
                double cy = pcbPl?.YMm ?? (5.0 + (autoPcbIdx / 8) * 6.0);
                int rotDeg = pcbPl?.RotationDegrees ?? 0;
                string layer = pcbPl?.Layer ?? "top";
                autoPcbIdx++;

                var spec = ResolveFootprintSpec(fp, pinListByPart.GetValueOrDefault(part.Id) ?? ["p", "n"]);
                var pcbCompId = $"pcb_component_{part.Id}";
                elements.Add(new
                {
                    type = "pcb_component",
                    pcb_component_id = pcbCompId,
                    source_component_id = srcId,
                    footprint = fp,
                    center = new { x = cx, y = cy },
                    width = spec.BodyWidthMm,
                    height = spec.BodyHeightMm,
                    rotation = rotDeg,
                    layer
                });

                double rad = rotDeg * Math.PI / 180.0;
                double cosR = Math.Cos(rad), sinR = Math.Sin(rad);
                foreach (var pad in spec.Pads)
                {
                    double wx = Math.Round(cx + pad.Dx * cosR - pad.Dy * sinR, 4);
                    double wy = Math.Round(cy + pad.Dx * sinR + pad.Dy * cosR, 4);
                    var padId = $"pcb_pad_{part.Id}_{pad.PinName}";
                    padWorldPos[(part.Id, pad.PinName)] = (wx, wy, padId);

                    if (pad.IsThroughHole)
                    {
                        elements.Add(new
                        {
                            type = "pcb_plated_hole",
                            pcb_plated_hole_id = padId,
                            pcb_component_id = pcbCompId,
                            pin_name = pad.PinName,
                            shape = "circle",
                            x = wx,
                            y = wy,
                            hole_diameter = pad.DrillMm,
                            outer_diameter = pad.WidthMm,
                            layers = new[] { "top", "bottom" }
                        });
                    }
                    else
                    {
                        elements.Add(new
                        {
                            type = "pcb_smtpad",
                            pcb_smtpad_id = padId,
                            pcb_component_id = pcbCompId,
                            pin_name = pad.PinName,
                            shape = "rect",
                            x = wx,
                            y = wy,
                            width = pad.WidthMm,
                            height = pad.HeightMm,
                            layer
                        });
                    }
                }
            }
        }

        var routedNets = new HashSet<string>(
            (document.Pcb?.Traces ?? []).Where(t => t is not null).Select(t => t.NetName),
            StringComparer.OrdinalIgnoreCase);

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
            var netPads = new List<(double X, double Y, string PadId)>();
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
                if (padWorldPos.TryGetValue((pr.InstanceId, pr.PinName), out var ppos))
                {
                    netPads.Add(ppos);
                }
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

            // Emit unrouted ratsnest airwires for multi-pad nets without explicit copper traces
            if (netPads.Count >= 2 && !routedNets.Contains(netName))
            {
                for (int k = 1; k < netPads.Count; ++k)
                {
                    elements.Add(new
                    {
                        type = "pcb_ratsnest",
                        pcb_ratsnest_id = $"pcb_ratsnest_{netName}_{k}",
                        source_net_id = netId,
                        from_pad_id = netPads[k - 1].PadId,
                        to_pad_id = netPads[k].PadId,
                        x1 = netPads[k - 1].X,
                        y1 = netPads[k - 1].Y,
                        x2 = netPads[k].X,
                        y2 = netPads[k].Y
                    });
                }
            }
        }

        if (document.Pcb?.Traces is not null)
        {
            int trIdx = 0;
            foreach (var tr in document.Pcb.Traces)
            {
                if (tr is null || tr.Points.Count < 2) continue;
                elements.Add(new
                {
                    type = "pcb_trace",
                    pcb_trace_id = $"pcb_trace_{tr.NetName}_{++trIdx}",
                    source_trace_id = $"source_trace_{tr.NetName}",
                    net_name = tr.NetName,
                    route = tr.Points.Select(pt => new
                    {
                        route_type = "wire",
                        x = pt.XMm,
                        y = pt.YMm,
                        width = tr.WidthMm,
                        layer = tr.Layer
                    }).ToArray()
                });
            }
        }

        if (document.Pcb?.Vias is not null)
        {
            int viaIdx = 0;
            foreach (var via in document.Pcb.Vias)
            {
                if (via is null) continue;
                elements.Add(new
                {
                    type = "pcb_via",
                    pcb_via_id = $"pcb_via_{via.NetName}_{++viaIdx}",
                    source_net_id = $"source_net_{via.NetName}",
                    x = via.XMm,
                    y = via.YMm,
                    hole_diameter = via.DrillMm,
                    outer_diameter = via.OuterDiameterMm,
                    layers = new[] { via.FromLayer, via.ToLayer }
                });
            }
        }

        return JsonSerializer.Serialize(elements, JsonOpts);
    }

    private sealed record PadSpec(string PinName, double Dx, double Dy, double WidthMm, double HeightMm, bool IsThroughHole = false, double DrillMm = 0.0);
    private sealed record FootprintSpec(double BodyWidthMm, double BodyHeightMm, IReadOnlyList<PadSpec> Pads);

    private static FootprintSpec ResolveFootprintSpec(string footprint, IReadOnlyList<string> pins)
    {
        var fp = footprint.Trim().ToUpperInvariant();
        string p0 = pins.Count > 0 ? pins[0] : "p";
        string p1 = pins.Count > 1 ? pins[1] : "n";
        string p2 = pins.Count > 2 ? pins[2] : "3";

        if (fp.Contains("0402", StringComparison.Ordinal))
            return new(1.6, 0.7, [new(p0, -0.5, 0.0, 0.6, 0.6), new(p1, 0.5, 0.0, 0.6, 0.6)]);
        if (fp.Contains("0603", StringComparison.Ordinal))
            return new(2.4, 1.0, [new(p0, -0.8, 0.0, 0.8, 0.9), new(p1, 0.8, 0.0, 0.8, 0.9)]);
        if (fp.Contains("0805", StringComparison.Ordinal))
            return new(3.0, 1.4, [new(p0, -1.0, 0.0, 1.0, 1.3), new(p1, 1.0, 0.0, 1.0, 1.3)]);
        if (fp.Contains("1206", StringComparison.Ordinal))
            return new(4.2, 1.8, [new(p0, -1.5, 0.0, 1.1, 1.6), new(p1, 1.5, 0.0, 1.1, 1.6)]);
        if (fp.Contains("SOT-23", StringComparison.Ordinal) || fp.Contains("SOT23", StringComparison.Ordinal))
            return new(3.0, 2.6, [new(p0, -0.95, -1.1, 0.6, 0.9), new(p1, 0.95, -1.1, 0.6, 0.9), new(p2, 0.0, 1.1, 0.6, 0.9)]);
        if (fp.Contains("SOIC-8", StringComparison.Ordinal) || fp.Contains("SOIC8", StringComparison.Ordinal))
        {
            var pads = new List<PadSpec>(8);
            for (int i = 0; i < 4; ++i) pads.Add(new(i < pins.Count ? pins[i] : $"{i + 1}", -2.7, -1.905 + i * 1.27, 1.5, 0.6));
            for (int i = 0; i < 4; ++i) pads.Add(new(4 + i < pins.Count ? pins[4 + i] : $"{5 + i}", 2.7, 1.905 - i * 1.27, 1.5, 0.6));
            return new(6.0, 5.0, pads);
        }
        if (fp.Contains("DIP-8", StringComparison.Ordinal) || fp.Contains("DIP8", StringComparison.Ordinal))
        {
            var pads = new List<PadSpec>(8);
            for (int i = 0; i < 4; ++i) pads.Add(new(i < pins.Count ? pins[i] : $"{i + 1}", -3.81, -3.81 + i * 2.54, 1.6, 1.6, true, 0.8));
            for (int i = 0; i < 4; ++i) pads.Add(new(4 + i < pins.Count ? pins[4 + i] : $"{5 + i}", 3.81, 3.81 - i * 2.54, 1.6, 1.6, true, 0.8));
            return new(9.2, 9.6, pads);
        }
        if (fp.Contains("TO-220", StringComparison.Ordinal) || fp.Contains("TO220", StringComparison.Ordinal))
            return new(10.0, 4.5, [new(p0, -2.54, 0.0, 1.8, 1.8, true, 1.0), new(p1, 0.0, 0.0, 1.8, 1.8, true, 1.0), new(p2, 2.54, 0.0, 1.8, 1.8, true, 1.0)]);

        // Generic dual-row fallback for arbitrary N-pin packages
        int n = Math.Max(2, pins.Count);
        var gen = new List<PadSpec>(n);
        for (int i = 0; i < n; ++i)
        {
            string nm = i < pins.Count ? pins[i] : $"pin{i + 1}";
            double dx = (i - (n - 1) * 0.5) * 1.27;
            gen.Add(new(nm, dx, 0.0, 0.8, 1.0));
        }
        return new(Math.Max(2.5, n * 1.27 + 1.0), 2.0, gen);
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
