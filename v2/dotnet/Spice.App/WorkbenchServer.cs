using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;
using Spice.Starlark;

namespace Spice.App;

/// <summary>
/// Embedded cross-platform HTTP workbench host for Spice.App backed strictly by public
/// Spice.Ir, Spice.Schematic, Spice.Starlark, and Spice.Sdk (NativeCppBackend) APIs.
/// </summary>
public sealed class WorkbenchServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly NativeCppBackend _backend;
    private readonly bool _ownsBackend;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public string BaseUrl { get; }

    public WorkbenchServer(int port = 0, NativeCppBackend? backend = null)
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
                    device_kinds = DeviceRegistry.Default.All.Select(d => d.KindId).OrderBy(k => k, StringComparer.Ordinal).ToArray()
                });
                return;
            }
            if (method == "POST" && path == "/api/schematic/compile")
            {
                var req = await ReadJsonAsync<SchematicSimRequest>(ctx.Request);
                if (req?.Document is null)
                {
                    await WriteJsonAsync(ctx.Response, 400, new { error = "missing document" });
                    return;
                }
                var compiled = SchematicCompiler.CompileToIr(req.Document);
                var analysis = ParseAnalysis(req.Analysis ?? "op");
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    erc_ok = compiled.Succeeded,
                    diagnostics = compiled.Diagnostics,
                    svg = SchematicCompiler.ExportSvg(req.Document),
                    spice = compiled.Succeeded ? SchematicCompiler.ExportSpiceNetlist(req.Document, analysis) : ""
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
                var compiled = SchematicCompiler.CompileToIr(req.Document);
                var analysis = ParseAnalysis(req.Analysis ?? "op");
                if (!compiled.Succeeded || compiled.Circuit is null)
                {
                    var failSvg = SchematicCompiler.ExportSvg(
                        req.Document,
                        new OperatingPointOverlay(
                            new Dictionary<string, double>(),
                            new Dictionary<string, double>(),
                            gridSize,
                            showGrid,
                            ShowOperatingPoint: false));
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

                var numOpts = new Dictionary<string, double>();
                if (analysis == AnalysisKind.Ac)
                {
                    numOpts["f_start_hz"] = req.Start ?? 10.0;
                    numOpts["f_stop_hz"] = req.Stop ?? 100_000.0;
                    numOpts["points_per_decade"] = req.Step ?? 10.0;
                }
                else if (analysis == AnalysisKind.Transient)
                {
                    numOpts["step_s"] = req.Step ?? 2e-5;
                    numOpts["stop_s"] = req.Stop ?? 5e-3;
                    numOpts["uic"] = 1.0;
                    numOpts["method"] = 2.0; // BDF2
                }

                var sim = await _backend.RunAsync(
                    new SimulationRequestIr(req.Document.Name, compiled.Circuit, analysis, numOpts), ct);

                // Always solve DC Operating Point so live node voltages & branch currents display directly on the schematic!
                var opSim = analysis == AnalysisKind.OperatingPoint
                    ? sim
                    : await _backend.RunAsync(
                        new SimulationRequestIr(req.Document.Name + "_op", compiled.Circuit, AnalysisKind.OperatingPoint), ct);

                OperatingPointOverlay? overlay = opSim.Status == RunTerminalStatus.Succeeded
                    ? SchematicCompiler.BuildOperatingPointOverlay(req.Document, opSim, gridSize, showGrid, showOp)
                    : new OperatingPointOverlay(
                        new Dictionary<string, double>(),
                        new Dictionary<string, double>(),
                        gridSize,
                        showGrid,
                        ShowOperatingPoint: false);

                var svg = SchematicCompiler.ExportSvg(req.Document, overlay);
                var (axis, series) = FlattenResult(sim);
                await WriteJsonAsync(ctx.Response, 200, new
                {
                    erc_ok = true,
                    diagnostics = compiled.Diagnostics,
                    svg,
                    spice = SchematicCompiler.ExportSpiceNetlist(req.Document, analysis),
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
        double? GridSize = null,
        bool? ShowGrid = null,
        bool? ShowOp = null);

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
