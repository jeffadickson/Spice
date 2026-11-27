using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cameo.Netlist;
using Cameo.Netlist.Scripting;
using Spice.Cameo;
using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;

namespace Spice.App;

/// <summary>
/// Embedded cross-platform HTTP workbench host for Spice.App backed strictly by public
/// Cameo.Netlist, Cameo.Netlist.Scripting, Spice.Cameo, Spice.Ir, Spice.Schematic, and
/// Spice.Sdk (NativeCppBackend) APIs. Exposes interactive schematic capture, Cameo script
/// execution, multi-dialect netlist/bundle export, versioned save/load, and persistent
/// custom component library endpoints.
/// </summary>
public sealed class WorkbenchServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly NativeCppBackend _backend;
    private readonly bool _ownsBackend;
    private readonly ComponentLibraryStore _library;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public string BaseUrl { get; }
    public ComponentLibraryStore Library => _library;

    public WorkbenchServer(int port = 0, NativeCppBackend? backend = null, ComponentLibraryStore? library = null)
    {
        if (port == 0)
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }
        BaseUrl = $"http://127.0.0.1:{port}/";
        _ownsBackend = backend is null;
        _backend = backend ?? new NativeCppBackend(enableAudits: true);
        _library = library ?? new ComponentLibraryStore(ComponentLibraryStore.GetDefaultLibraryPath());
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
    }

    public void Start()
    {
        _listener.Start();
        _loopTask = Task.Run(() => ServeLoopAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }
        _listener.Close();
        if (_loopTask is not null)
        {
            try { await _loopTask; } catch (OperationCanceledException) { } catch (HttpListenerException) { }
        }
        if (_ownsBackend)
        {
            _backend.Dispose();
        }
        _cts.Dispose();
    }

    private async Task ServeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(ctx, ct), ct);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var method = ctx.Request.HttpMethod.ToUpperInvariant();

            if (method == "GET" && (path == "/" || path == "/index.html"))
            {
                await WriteTextAsync(ctx.Response, 200, "text/html; charset=utf-8", WorkbenchUi.Html);
                return;
            }
            if (method == "GET" && path == "/api/info")
            {
                var caps = _backend.QueryCapabilities();
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    backend = _backend.BackendId,
                    abi = $"{caps.AbiMajor}.{caps.AbiMinor}",
                    klu_available = caps.KluAvailable,
                    osdi_available = caps.OsdiAvailable,
                    audits_available = caps.AuditsAvailable,
                    dotnet_runtime = Environment.Version.ToString(),
                    library_count = _library.ListAll().Count,
                    custom_library_count = _library.ListCustom().Count,
                    device_kinds = DeviceRegistry.Default.All.Select(d => d.KindId).OrderBy(k => k, StringComparer.Ordinal).ToArray()
                });
                return;
            }

            // ---- COMPONENT & MODEL LIBRARY ENDPOINTS ----
            if (method == "GET" && path == "/api/library")
            {
                var parts = _library.ListAll().Select(p => new
                {
                    part = p,
                    preview_svg = SchematicCompiler.ExportSymbolPreviewSvg(p)
                }).ToArray();
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    format_id = ComponentLibraryStore.LibraryFormatId,
                    schema_version = ComponentLibraryStore.CurrentSchemaVersion,
                    storage_path = _library.StoragePath,
                    components = parts
                });
                return;
            }
            if (method == "POST" && path == "/api/library")
            {
                var part = await ReadJsonAsync<CustomComponentDefinition>(ctx.Request);
                if (part is null)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing custom component payload" });
                    return;
                }
                var errors = ComponentLibraryStore.Validate(part);
                if (errors.Count > 0)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "validation_failed", diagnostics = errors });
                    return;
                }
                var saved = _library.UpsertCustom(part);
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    saved = true,
                    part = saved,
                    preview_svg = SchematicCompiler.ExportSymbolPreviewSvg(saved)
                });
                return;
            }
            if (method == "DELETE" && path.StartsWith("/api/library/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/api/library/".Length..]);
                var removed = _library.DeleteCustom(id);
                await WriteJsonAsync(ctx.Response, removed ? 200 : 404, new { deleted = removed, id });
                return;
            }
            if (method == "POST" && path == "/api/library/from-schematic")
            {
                var req = await ReadJsonAsync<CreatePartFromSchematicRequest>(ctx.Request);
                if (req?.Document is null || string.IsNullOrWhiteSpace(req.Name))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing document or component name" });
                    return;
                }
                var saved = _library.CreateFromSchematic(
                    req.Document,
                    req.PartId ?? "",
                    req.Name,
                    req.Category ?? "Custom Subcircuits",
                    req.Description);
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    saved = true,
                    part = saved,
                    preview_svg = SchematicCompiler.ExportSymbolPreviewSvg(saved)
                });
                return;
            }
            if (method == "POST" && path == "/api/library/import-spice")
            {
                var req = await ReadJsonAsync<ImportSpiceLibraryRequest>(ctx.Request);
                if (req is null || string.IsNullOrWhiteSpace(req.SpiceText))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing spiceText" });
                    return;
                }
                var report = _library.ImportSpiceLibraryText(
                    req.SpiceText,
                    req.DefaultCategory,
                    overwriteExisting: req.Overwrite ?? false,
                    dryRun: req.DryRun ?? false);
                var items = report.Components.Select(p => new
                {
                    part = p,
                    preview_svg = SchematicCompiler.ExportSymbolPreviewSvg(p)
                }).ToArray();
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    dry_run = report.DryRun,
                    total_found = report.TotalFound,
                    imported_count = report.ImportedCount,
                    skipped_count = report.SkippedCount,
                    error_count = report.ErrorCount,
                    components = items,
                    diagnostics = report.Diagnostics
                });
                return;
            }
            if (method == "GET" && path == "/api/library/export")
            {
                var json = _library.ExportCustomLibraryJson();
                await WriteTextAsync(ctx.Response, 200, "application/json; charset=utf-8", json);
                return;
            }
            if (method == "POST" && path == "/api/library/import-json")
            {
                using var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var rawBody = await sr.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(rawBody))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing library JSON payload" });
                    return;
                }
                string libraryJson = rawBody;
                try
                {
                    var wrapper = JsonSerializer.Deserialize<ImportLibraryJsonRequest>(rawBody, JsonOpts);
                    if (!string.IsNullOrWhiteSpace(wrapper?.Content))
                    {
                        libraryJson = wrapper.Content;
                    }
                }
                catch
                {
                    // Treat rawBody directly as ComponentLibraryFile JSON.
                }

                try
                {
                    var imported = _library.ImportCustomLibraryJson(libraryJson);
                    await WriteJsonAsync(ctx.Response, 200, new { imported });
                }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = ex.Message });
                }
                return;
            }

            // ---- SCHEMATIC SAVE / LOAD / IMPORT / EXPORT ----
            if (method == "POST" && path == "/api/schematic/save")
            {
                var req = await ReadJsonAsync<SchematicSaveRequest>(ctx.Request);
                if (req?.Document is null)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing document" });
                    return;
                }
                var stamped = req.Document with
                {
                    Metadata = (req.Document.Metadata ?? new SchematicMetadata()) with
                    {
                        Author = req.Author ?? req.Document.Metadata?.Author,
                        Description = req.Description ?? req.Document.Metadata?.Description,
                        ModifiedUtc = DateTime.UtcNow.ToString("O")
                    }
                };
                var json = SchematicCompiler.SerializeJson(stamped, _library);
                var parsed = SchematicCompiler.DeserializeJson(json, verifyChecksum: true);
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    filename = $"{parsed.Name}.spice.json",
                    schema_version = parsed.SchemaVersion,
                    sha256 = parsed.Metadata?.ContentSha256,
                    content = json
                });
                return;
            }
            if (method == "POST" && path == "/api/schematic/load")
            {
                var req = await ReadJsonAsync<SchematicLoadRequest>(ctx.Request);
                if (req is null || string.IsNullOrWhiteSpace(req.Content))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing file content" });
                    return;
                }
                try
                {
                    var doc = SchematicCompiler.DeserializeJson(req.Content, verifyChecksum: req.VerifyChecksum ?? true);
                    await WriteJsonAsync(ctx.Response, 200, new
                    {
                        loaded = true,
                        document = doc,
                        schema_version = doc.SchemaVersion,
                        sha256 = doc.Metadata?.ContentSha256
                    });
                }
                catch (InvalidDataException ex)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { loaded = false, error = ex.Message });
                }
                return;
            }
            if (method == "POST" && path == "/api/schematic/import-spice")
            {
                var req = await ReadJsonAsync<ImportSpiceRequest>(ctx.Request);
                if (req is null || string.IsNullOrWhiteSpace(req.SpiceText))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing spiceText" });
                    return;
                }
                var doc = SchematicExporter.ImportFromSpiceNetlist(req.SpiceText, req.Name);
                await WriteJsonAsync(ctx.Response, 200, new { imported = true, document = doc });
                return;
            }
            if (method == "POST" && path == "/api/cameo/run")
            {
                var req = await ReadJsonAsync<CameoRunRequest>(ctx.Request);
                if (req is null || string.IsNullOrWhiteSpace(req.Script))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing script" });
                    return;
                }
                var res = CameoSimulationHost.RunScript(
                    req.Script,
                    _backend,
                    _library,
                    new CameoNetlistScriptOptions(
                        MaxSteps: req.MaxSteps is > 0 ? req.MaxSteps.Value : 2_000_000,
                        FileName: string.IsNullOrWhiteSpace(req.FileName) ? "workbench.cameo" : req.FileName));

                SchematicDocument? schem = null;
                string? velosimJson = null;
                string? circuitJson = null;
                if (res.Document is not null)
                {
                    schem = CameoNetlistBridge.ToSchematicDocument(res.Document, _library);
                    velosimJson = CameoNetlistBridge.ExportVeloSimNetlistJson(res.Document, _library);
                    circuitJson = CameoNetlistBridge.ExportCircuitJson(res.Document, _library);
                }

                await WriteJsonAsync(ctx.Response, res.Succeeded ? 200 : 400, new
                {
                    succeeded = res.Succeeded,
                    design = res.Document?.Name,
                    electrical_sha256 = res.Document?.ElectricalSha256,
                    layout_sha256 = res.Document?.LayoutSha256,
                    spicenet = res.FormattedSpicenet,
                    velosim_json = velosimJson,
                    circuit_json = circuitJson,
                    document = schem,
                    runs = res.Runs,
                    assertions = res.Assertions,
                    diagnostics = res.Diagnostics,
                    stdout = res.Stdout
                });
                return;
            }
            if (method == "POST" && path == "/api/schematic/import-netlist")
            {
                var req = await ReadJsonAsync<ImportFlagshipNetlistRequest>(ctx.Request);
                if (req is null || string.IsNullOrWhiteSpace(req.Content))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing content" });
                    return;
                }
                var trimmed = req.Content.TrimStart();
                FlagshipParseResult parsed;
                string detected;
                if (trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                {
                    detected = "polyglot_svg";
                    parsed = CameoNetlistBridge.ExtractFromPolyglotSvg(req.Content, _library, verifyHashes: req.VerifyHashes ?? true);
                }
                else if (trimmed.StartsWith('{'))
                {
                    detected = "velosim.netlist.v1";
                    parsed = CameoNetlistBridge.ImportVeloSimNetlistJson(req.Content, _library, verifyModelHashes: req.VerifyHashes ?? true);
                }
                else if (CameoNetlistScriptHost.LooksLikeCameoNetlistScript(req.Content))
                {
                    detected = "cameo.script";
                    var eval = CameoNetlistScriptHost.Evaluate(
                        req.Content,
                        new CameoNetlistScriptOptions(LibraryResolver: CameoNetlistBridge.CreateLibraryResolver(_library)));
                    parsed = new FlagshipParseResult(eval.Succeeded, eval.Document, eval.Diagnostics);
                }
                else
                {
                    detected = FlagshipNetlist.FormatId;
                    parsed = CameoNetlistBridge.Parse(req.Content, _library, verifyHashes: req.VerifyHashes ?? false);
                }

                if (!parsed.Succeeded || parsed.Document is null)
                {
                    await WriteJsonAsync(ctx.Response, 400, new
                    {
                        imported = false,
                        format = detected,
                        error = "Flagship netlist import failed.",
                        diagnostics = parsed.Diagnostics
                    });
                    return;
                }

                var elab = CameoNetlistBridge.Elaborate(parsed.Document, _library);
                if (!elab.Succeeded)
                {
                    await WriteJsonAsync(ctx.Response, 400, new
                    {
                        imported = false,
                        format = detected,
                        error = "Flagship netlist elaboration failed.",
                        diagnostics = elab.Diagnostics
                    });
                    return;
                }

                var schem = CameoNetlistBridge.ToSchematicDocument(parsed.Document, _library);
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    imported = true,
                    format = detected,
                    electrical_sha256 = parsed.Document.ElectricalSha256,
                    layout_sha256 = parsed.Document.LayoutSha256,
                    rtl_block_count = parsed.Document.RtlBlocks.Count,
                    bridge_count = elab.Bridges.Count,
                    document = schem,
                    diagnostics = parsed.Diagnostics
                });
                return;
            }
            if (method == "POST" && path == "/api/schematic/export")
            {
                var req = await ReadJsonAsync<SchematicExportRequest>(ctx.Request);
                if (req?.Document is null)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing document" });
                    return;
                }
                var fmt = (req.Format ?? "spice").ToLowerInvariant();
                var analysis = ParseAnalysis(req.Analysis ?? "op");
                var numOpts = BuildSchematicAnalysisOptions(analysis, req.Start, req.Stop, req.Step);
                var exportOpts = new SchematicExportOptions(
                    Dialect: ParseDialect(fmt),
                    Analysis: analysis,
                    AnalysisOptions: numOpts,
                    SweepSourceId: req.Source,
                    IncludeComments: req.IncludeComments ?? true,
                    IncludeProbes: req.IncludeProbes ?? true);

                string content;
                string filename;
                string contentType;
                switch (fmt)
                {
                    case "spicenet":
                    {
                        var flagship = CameoNetlistBridge.FromSchematic(req.Document, _library);
                        content = FlagshipNetlist.Format(flagship);
                        filename = $"{req.Document.Name}.spicenet";
                        contentType = "text/plain; charset=utf-8";
                        break;
                    }
                    case "polyglot_svg":
                    {
                        OperatingPointOverlay? polyOverlay = null;
                        if (req.IncludeOp ?? true)
                        {
                            var compiled = SchematicCompiler.CompileToIr(req.Document, library: _library);
                            if (compiled.Succeeded && compiled.Circuit is not null)
                            {
                                var opSim = await SolveSchematicAsync(req.Document, compiled.Circuit, AnalysisKind.OperatingPoint, new Dictionary<string, double>(), null, ct);
                                if (opSim.Status == RunTerminalStatus.Succeeded)
                                {
                                    polyOverlay = SchematicCompiler.BuildOperatingPointOverlay(req.Document, opSim, req.GridSize ?? 20.0, req.ShowGrid ?? true, showOperatingPoint: true, activeSheetId: req.ActiveSheetId);
                                }
                            }
                        }
                        content = CameoNetlistBridge.ExportPolyglotSvg(req.Document, polyOverlay, _library, lightTheme: false, gridStyle: req.GridStyle);
                        filename = $"{req.Document.Name}.spice.svg";
                        contentType = "image/svg+xml; charset=utf-8";
                        break;
                    }
                    case "velosim":
                    {
                        content = CameoNetlistBridge.ExportVeloSimNetlistJson(req.Document, _library);
                        filename = $"{req.Document.Name}.velosim.json";
                        contentType = "application/json; charset=utf-8";
                        break;
                    }
                    case "circuit_json":
                    {
                        content = CameoNetlistBridge.ExportCircuitJson(req.Document, _library);
                        filename = $"{req.Document.Name}.circuit.json";
                        contentType = "application/json; charset=utf-8";
                        break;
                    }
                    case "spice":
                    case "spice3f5":
                    case "ngspice":
                    case "xyce":
                        content = SchematicExporter.ExportNetlist(req.Document, exportOpts, library: _library);
                        filename = $"{req.Document.Name}.cir";
                        contentType = "text/plain; charset=utf-8";
                        break;
                    case "ltspice":
                        content = SchematicExporter.ExportNetlist(req.Document, exportOpts, library: _library);
                        filename = $"{req.Document.Name}.net";
                        contentType = "text/plain; charset=utf-8";
                        break;
                    case "spectre":
                        content = SchematicExporter.ExportNetlist(req.Document, exportOpts, library: _library);
                        filename = $"{req.Document.Name}.scs";
                        contentType = "text/plain; charset=utf-8";
                        break;
                    case "cameo":
                    case "starlark":
                        content = SchematicExporter.ExportCameoScript(req.Document, library: _library);
                        filename = $"{req.Document.Name}.cameo";
                        contentType = "text/plain; charset=utf-8";
                        break;
                    case "bom":
                        content = SchematicExporter.ExportBomCsv(req.Document, _library);
                        filename = $"{req.Document.Name}.bom.csv";
                        contentType = "text/csv; charset=utf-8";
                        break;
                    case "bundle":
                        content = SchematicExporter.ExportBundleJson(req.Document, exportOpts, library: _library);
                        filename = $"{req.Document.Name}.bundle.json";
                        contentType = "application/json; charset=utf-8";
                        break;
                    case "json":
                        content = SchematicCompiler.SerializeJson(req.Document, _library);
                        filename = $"{req.Document.Name}.spice.json";
                        contentType = "application/json; charset=utf-8";
                        break;
                    case "svg":
                    case "svg_light":
                    {
                        OperatingPointOverlay? opOverlay = null;
                        if (req.IncludeOp ?? true)
                        {
                            var compiled = SchematicCompiler.CompileToIr(req.Document, library: _library);
                            if (compiled.Succeeded && compiled.Circuit is not null)
                            {
                                var opSim = await SolveSchematicAsync(req.Document, compiled.Circuit, AnalysisKind.OperatingPoint, new Dictionary<string, double>(), null, ct);
                                if (opSim.Status == RunTerminalStatus.Succeeded)
                                {
                                    opOverlay = SchematicCompiler.BuildOperatingPointOverlay(req.Document, opSim, req.GridSize ?? 20.0, req.ShowGrid ?? true, showOperatingPoint: true, activeSheetId: req.ActiveSheetId);
                                }
                            }
                        }
                        content = SchematicCompiler.ExportSvg(req.Document, opOverlay, _library, lightTheme: fmt == "svg_light", gridStyle: req.GridStyle, activeSheetId: req.ActiveSheetId);
                        filename = $"{req.Document.Name}.svg";
                        contentType = "image/svg+xml; charset=utf-8";
                        break;
                    }
                    default:
                        await WriteJsonAsync(ctx.Response, 400, new { error = $"unsupported export format '{fmt}'" });
                        return;
                }

                await WriteJsonAsync(ctx.Response, 200, new
                {
                    format = fmt,
                    filename,
                    content_type = contentType,
                    content
                });
                return;
            }

            // ---- SCHEMATIC COMPILE & LIVE SIMULATE ----
            if (method == "POST" && path == "/api/schematic/compile")
            {
                var req = await ReadJsonAsync<SchematicSimRequest>(ctx.Request);
                if (req?.Document is null)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing document" });
                    return;
                }
                var compiled = SchematicCompiler.CompileToIr(req.Document, library: _library);
                var analysis = ParseAnalysis(req.Analysis ?? "op");
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    erc_ok = compiled.Succeeded,
                    diagnostics = compiled.Diagnostics,
                    svg = SchematicCompiler.ExportSvg(req.Document, library: _library, gridStyle: req.GridStyle, activeSheetId: req.ActiveSheetId),
                    spice = compiled.Succeeded ? SchematicCompiler.ExportSpiceNetlist(req.Document, analysis, library: _library) : ""
                });
                return;
            }
            if (method == "POST" && path == "/api/schematic/simulate")
            {
                var req = await ReadJsonAsync<SchematicSimRequest>(ctx.Request);
                if (req?.Document is null)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing document" });
                    return;
                }
                var gridSize = req.GridSize is > 2.0 ? req.GridSize.Value : 20.0;
                var showGrid = req.ShowGrid ?? true;
                var showOp = req.ShowOp ?? true;
                var doc = SchematicCompiler.EmbedReferencedLibraryAssets(req.Document, _library);
                var compiled = SchematicCompiler.CompileToIr(doc, library: _library);
                var analysis = ParseAnalysis(req.Analysis ?? "op");
                if (!compiled.Succeeded || compiled.Circuit is null)
                {
                    var failSvg = SchematicCompiler.ExportSvg(
                        doc,
                        new OperatingPointOverlay(
                            new Dictionary<string, double>(),
                            new Dictionary<string, double>(),
                            gridSize,
                            showGrid,
                            ShowOperatingPoint: false),
                        _library,
                        lightTheme: req.LightTheme ?? false,
                        gridStyle: req.GridStyle,
                        activeSheetId: req.ActiveSheetId);
                    await WriteJsonAsync(ctx.Response, 200, new
                    {
                        erc_ok = false,
                        diagnostics = compiled.Diagnostics,
                        svg = failSvg,
                        spice = "",
                        status = "ErcFailed"
                    });
                    return;
                }

                var numOpts = BuildSchematicAnalysisOptions(analysis, req.Start, req.Stop, req.Step);
                var sim = await SolveSchematicAsync(doc, compiled.Circuit, analysis, numOpts, req.Source, ct);

                // Always solve DC Operating Point so live node voltages & branch currents display directly on the schematic!
                var opSim = analysis == AnalysisKind.OperatingPoint
                    ? sim
                    : await SolveSchematicAsync(doc, compiled.Circuit, AnalysisKind.OperatingPoint, new Dictionary<string, double>(), null, ct);

                OperatingPointOverlay overlay = opSim.Status == RunTerminalStatus.Succeeded
                    ? SchematicCompiler.BuildOperatingPointOverlay(doc, opSim, gridSize, showGrid, showOp, activeSheetId: req.ActiveSheetId)
                    : new OperatingPointOverlay(
                        new Dictionary<string, double>(),
                        new Dictionary<string, double>(),
                        gridSize,
                        showGrid,
                        ShowOperatingPoint: false);

                var svg = SchematicCompiler.ExportSvg(
                    doc,
                    overlay,
                    _library,
                    lightTheme: req.LightTheme ?? false,
                    gridStyle: req.GridStyle,
                    activeSheetId: req.ActiveSheetId);
                var (axis, series) = FlattenResult(sim);
                var spiceText = SchematicExporter.ExportNetlist(
                    doc,
                    new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, analysis, numOpts, req.Source),
                    library: _library);
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    erc_ok = true,
                    diagnostics = compiled.Diagnostics,
                    svg,
                    spice = spiceText,
                    status = sim.Status.ToString(),
                    backend = sim.BackendId,
                    error_message = sim.ErrorMessage,
                    op_voltages = overlay.NodeVoltages,
                    op_currents = overlay.BranchCurrents,
                    axis,
                    series
                });
                return;
            }
            if (method == "POST" && path == "/api/deck/simulate")
            {
                var req = await ReadJsonAsync<DeckSimRequest>(ctx.Request);
                if (req is null || string.IsNullOrWhiteSpace(req.Deck))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing deck text" });
                    return;
                }
                var analysis = ParseAnalysis(req.Analysis ?? "op");
                var numOpts = new Dictionary<string, double>();
                switch (analysis)
                {
                    case AnalysisKind.DcSweep:
                        numOpts["start"] = req.Start ?? 0.0;
                        numOpts["stop"] = req.Stop ?? 1.2;
                        numOpts["step"] = req.Step ?? 0.1;
                        break;
                    case AnalysisKind.Ac:
                    case AnalysisKind.Noise:
                    case AnalysisKind.SParameters:
                        numOpts["f_start_hz"] = req.Start ?? 100.0;
                        numOpts["f_stop_hz"] = req.Stop ?? 1e6;
                        numOpts["points_per_decade"] = req.Step ?? 10.0;
                        numOpts["z0_ohms"] = 50.0;
                        break;
                    case AnalysisKind.Transient:
                        numOpts["step_s"] = req.Step ?? 1e-6;
                        numOpts["stop_s"] = req.Stop ?? 2e-4;
                        numOpts["method"] = 1.0;
                        break;
                    case AnalysisKind.HarmonicBalance:
                        numOpts["fundamental_hz"] = req.Start ?? 1e6;
                        numOpts["harmonics"] = req.Stop ?? 7.0;
                        break;
                    case AnalysisKind.PeriodicSteadyState:
                        numOpts["fundamental_hz"] = req.Start ?? 1e6;
                        numOpts["steps_per_period"] = req.Stop ?? 64.0;
                        numOpts["method"] = 1.0;
                        break;
                }

                var sim = await _backend.RunSpiceDeckAsync(
                    "workbench_deck",
                    req.Deck,
                    analysis,
                    new SpiceDeckExecutionOptions(
                        SweepSourceId: req.Source,
                        NoiseOutPositive: req.OutNode ?? "out",
                        NoiseInputSourceId: req.Source ?? "V1",
                        SParamPorts: [("p1", "0"), ("p2", "0")],
                        NumericOptions: numOpts),
                    ct);
                var (axis, series) = FlattenResult(sim);
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    status = sim.Status.ToString(),
                    backend = sim.BackendId,
                    error_message = sim.ErrorMessage,
                    axis,
                    series
                });
                return;
            }
            if (method == "POST" && path == "/api/campaign/run")
            {
                var req = await ReadJsonAsync<CampaignRequest>(ctx.Request);
                var vdd = req?.Vdd ?? 10.0;
                var r1 = req?.R1 ?? 1000.0;
                var r2Vals = (req?.R2Values is { Length: > 0 } arr) ? arr : [1000.0, 2000.0, 4000.0, 9000.0];
                var results = await CampaignRunner.RunParallelMapAsync(
                    r2Vals,
                    (r2, idx) => new SimulationRequestIr(
                        $"corner_{idx}",
                        new CircuitBuilder($"c_{idx}")
                            .AddVoltageSource("V1", "in", "0", SiUnits.Volts(vdd))
                            .AddResistor("R1", "in", "out", r1)
                            .AddResistor("R2", "out", "0", r2)
                            .Build(),
                        AnalysisKind.OperatingPoint),
                    _backend,
                    maxDegreeOfParallelism: 4,
                    cancellationToken: ct);
                var runs = results.Select((r, i) => new
                {
                    case_id = r.CaseId,
                    r2_ohms = r2Vals[i],
                    status = r.Status.ToString(),
                    v_out = r.RealData?.SignalValues.TryGetValue("v(out)", out var v) == true ? v[0] : double.NaN
                }).ToArray();
                await WriteJsonAsync(ctx.Response, 200, new { runs });
                return;
            }

            await WriteJsonAsync(ctx.Response, 404, new { error = $"unknown route {method} {path}" });
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(ctx.Response, 500, new { error = ex.Message });
            }
            catch
            {
                // Client already disconnected.
            }
        }
    }

    private Task<ResultManifestIr> SolveSchematicAsync(
        SchematicDocument doc,
        CircuitIr circuit,
        AnalysisKind analysis,
        Dictionary<string, double> numOpts,
        string? sweepSource,
        CancellationToken ct)
    {
        bool needsFullDeckSolver =
            (circuit.Models is { Count: > 0 }) ||
            (circuit.Directives is { Count: > 0 }) ||
            circuit.Devices.Any(d => d.KindId is "spice.mosfet" or "spice.vdmos" or "spice.bjt" or "spice.jfet" or "spice.mesfet" or "spice.switch" or "spice.cswitch" or "spice.core" or "spice.subckt" or "spice.osdi");

        if (!needsFullDeckSolver)
        {
            return _backend.RunAsync(
                new SimulationRequestIr(doc.Name, circuit, analysis, numOpts, SweepSourceId: sweepSource), ct);
        }

        var deck = SchematicExporter.ExportNetlist(
            doc,
            new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, analysis, numOpts, sweepSource),
            library: _library);

        return _backend.RunSpiceDeckAsync(
            doc.Name,
            deck,
            analysis,
            new SpiceDeckExecutionOptions(
                SweepSourceId: sweepSource ?? circuit.Devices.FirstOrDefault(d => d.KindId == "spice.vsrc")?.Id ?? "V1",
                NumericOptions: numOpts),
            ct);
    }

    private static Dictionary<string, double> BuildSchematicAnalysisOptions(
        AnalysisKind analysis,
        double? start,
        double? stop,
        double? step)
    {
        var numOpts = new Dictionary<string, double>();
        if (analysis == AnalysisKind.DcSweep)
        {
            numOpts["start"] = start ?? 0.0;
            numOpts["stop"] = stop ?? 5.0;
            numOpts["step"] = step ?? 0.1;
        }
        else if (analysis == AnalysisKind.Ac)
        {
            numOpts["f_start_hz"] = start ?? 10.0;
            numOpts["f_stop_hz"] = stop ?? 100_000.0;
            numOpts["points_per_decade"] = step ?? 10.0;
        }
        else if (analysis == AnalysisKind.Transient)
        {
            numOpts["step_s"] = step ?? 2e-5;
            numOpts["stop_s"] = stop ?? 5e-3;
            numOpts["uic"] = 1.0;
            numOpts["method"] = 2.0; // BDF2
        }
        return numOpts;
    }

    private static (IReadOnlyList<double> Axis, Dictionary<string, double[]> Series) FlattenResult(ResultManifestIr res)
    {
        var series = new Dictionary<string, double[]>(StringComparer.Ordinal);
        if (res.RealData is not null)
        {
            foreach (var (sig, vals) in res.RealData.SignalValues)
            {
                series[sig] = [.. vals];
            }
            return (res.RealData.AxisValues, series);
        }
        if (res.ComplexData is not null)
        {
            foreach (var (sig, vals) in res.ComplexData.SignalValues)
            {
                series[sig] = vals.Select(c => Math.Sqrt(c.Real * c.Real + c.Imaginary * c.Imaginary)).ToArray();
            }
            return (res.ComplexData.AxisValues, series);
        }
        return ([], series);
    }

    private static AnalysisKind ParseAnalysis(string s) => s.ToLowerInvariant() switch
    {
        "op" => AnalysisKind.OperatingPoint,
        "dc" => AnalysisKind.DcSweep,
        "ac" => AnalysisKind.Ac,
        "tran" => AnalysisKind.Transient,
        "noise" => AnalysisKind.Noise,
        "hb" => AnalysisKind.HarmonicBalance,
        "pss" => AnalysisKind.PeriodicSteadyState,
        "sp" => AnalysisKind.SParameters,
        _ => throw new ArgumentException($"Unsupported analysis '{s}'.")
    };

    private static NetlistDialect ParseDialect(string s) => s.ToLowerInvariant() switch
    {
        "ngspice" => NetlistDialect.Ngspice,
        "xyce" => NetlistDialect.Xyce,
        "ltspice" => NetlistDialect.Ltspice,
        "spectre" => NetlistDialect.Spectre,
        _ => NetlistDialect.CanonicalSpice3f5
    };

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        using var sr = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await sr.ReadToEndAsync();
        return JsonSerializer.Deserialize<T>(body, JsonOpts);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        await WriteTextAsync(response, statusCode, "application/json; charset=utf-8", json);
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, int statusCode, string contentType, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private sealed record SchematicSimRequest(
        SchematicDocument? Document,
        string? Analysis,
        double? Start,
        double? Stop,
        double? Step,
        string? Source = null,
        double? GridSize = null,
        bool? ShowGrid = null,
        bool? ShowOp = null,
        string? GridStyle = null,
        bool? LightTheme = null,
        string? ActiveSheetId = null);

    private sealed record SchematicExportRequest(
        SchematicDocument? Document,
        string? Format,
        string? Analysis = null,
        string? Source = null,
        double? Start = null,
        double? Stop = null,
        double? Step = null,
        bool? IncludeComments = null,
        bool? IncludeProbes = null,
        bool? IncludeOp = null,
        double? GridSize = null,
        bool? ShowGrid = null,
        string? GridStyle = null,
        string? ActiveSheetId = null);

    private sealed record SchematicSaveRequest(
        SchematicDocument? Document,
        string? Author = null,
        string? Description = null);

    private sealed record SchematicLoadRequest(
        string? Content,
        bool? VerifyChecksum = null);

    private sealed record ImportSpiceRequest(
        string? SpiceText,
        string? Name = null);

    private sealed record ImportFlagshipNetlistRequest(
        string? Content,
        bool? VerifyHashes = null);

    private sealed record CameoRunRequest(
        string? Script,
        string? FileName = null,
        long? MaxSteps = null);

    private sealed record ImportSpiceLibraryRequest(
        string? SpiceText,
        string? DefaultCategory = null,
        bool? Overwrite = null,
        bool? DryRun = null);

    private sealed record ImportLibraryJsonRequest(
        string? Content);

    private sealed record CreatePartFromSchematicRequest(
        SchematicDocument? Document,
        string? PartId,
        string? Name,
        string? Category = null,
        string? Description = null);

    private sealed record DeckSimRequest(
        string? Deck,
        string? Analysis,
        string? Source,
        string? OutNode,
        double? Start,
        double? Stop,
        double? Step);

    private sealed record CampaignRequest(
        double? Vdd,
        double? R1,
        double[]? R2Values);
}
