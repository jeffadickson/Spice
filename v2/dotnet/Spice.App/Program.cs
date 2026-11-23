using System.Globalization;
using System.Text.Json;
using Spice.Ir;
using Spice.Schematic;
using Spice.Sdk;
using Spice.Starlark;

namespace Spice.App;

/// <summary>
/// .NET 11 Spice V2 Workbench Host consuming strictly public Spice.Ir, Spice.Schematic,
/// Spice.Starlark, and Spice.Sdk APIs over the in-process C++20 libspice_capi ABI (ARCH-API-002).
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "info" => RunInfo(),
                "serve" => await RunServeAsync(args[1..]),
                "run" => await RunDeckAsync(args[1..]),
                "schematic" => await RunSchematicAsync(args[1..]),
                "campaign" => await RunCampaignAsync(args[1..]),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"error: unknown command '{cmd}'");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Spice V2 .NET 11 Workbench Host (spice-app)
            Usage:
              spice-app info
              spice-app serve [--port 5180]
              spice-app run <deck.cir> [--analysis op|dc|ac|tran|noise|hb|pss|sp]
                                       [--source V1] [--start 0] [--stop 1.2] [--step 0.1]
                                       [--f0 1e6] [--harmonics 7] [--out-p out]
              spice-app schematic <schematic.json> [--analysis op|ac|tran] [--svg out.svg] [--spice out.cir]
              spice-app campaign [--preset rc-sweep]
            """);
    }

    private static async Task<int> RunServeAsync(string[] args)
    {
        int port = 5180;
        for (int i = 0; i < args.Length; ++i)
        {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out var p) && p >= 0 && p <= 65535)
            {
                port = p;
            }
        }
        await using var server = new WorkbenchServer(port);
        server.Start();
        Console.WriteLine($"Spice V2 Workbench listening at {server.BaseUrl} (Ctrl+C to stop)");
        using var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };
        stop.Wait();
        return 0;
    }

    private static int RunInfo()
    {
        using var backend = new NativeCppBackend(enableAudits: true);
        var caps = backend.QueryCapabilities();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            backend = backend.BackendId,
            abi = $"{caps.AbiMajor}.{caps.AbiMinor}",
            klu_available = caps.KluAvailable,
            osdi_available = caps.OsdiAvailable,
            audits_available = caps.AuditsAvailable,
            dotnet_runtime = Environment.Version.ToString()
        }, JsonOpts));
        return 0;
    }

    private static async Task<int> RunDeckAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: missing <deck.cir> path");
            return 2;
        }
        var deckPath = args[0];
        var analysis = AnalysisKind.OperatingPoint;
        string? sweepSrc = null;
        string? outP = null;
        var numeric = new Dictionary<string, double>();

        for (int i = 1; i < args.Length; ++i)
        {
            switch (args[i])
            {
                case "--analysis" when i + 1 < args.Length:
                    analysis = ParseAnalysisKind(args[++i]);
                    break;
                case "--source" when i + 1 < args.Length:
                    sweepSrc = args[++i];
                    break;
                case "--out-p" when i + 1 < args.Length:
                    outP = args[++i];
                    break;
                case "--start" when i + 1 < args.Length:
                    numeric["start"] = ParseDouble(args[++i]);
                    break;
                case "--stop" when i + 1 < args.Length:
                    numeric["stop"] = ParseDouble(args[++i]);
                    break;
                case "--step" when i + 1 < args.Length:
                    numeric["step"] = ParseDouble(args[++i]);
                    break;
                case "--f0" when i + 1 < args.Length:
                    numeric["fundamental_hz"] = ParseDouble(args[++i]);
                    break;
                case "--harmonics" when i + 1 < args.Length:
                    numeric["harmonics"] = ParseDouble(args[++i]);
                    break;
                case "--stop-s" when i + 1 < args.Length:
                    numeric["stop_s"] = ParseDouble(args[++i]);
                    break;
                case "--step-s" when i + 1 < args.Length:
                    numeric["step_s"] = ParseDouble(args[++i]);
                    break;
            }
        }

        var text = await File.ReadAllTextAsync(deckPath);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(deckPath));
        using var backend = new NativeCppBackend(enableAudits: true);
        var res = await backend.RunSpiceDeckAsync(
            Path.GetFileNameWithoutExtension(deckPath),
            text,
            analysis,
            new SpiceDeckExecutionOptions(
                BaseDirectory: baseDir,
                SweepSourceId: sweepSrc,
                NoiseOutPositive: outP,
                NoiseInputSourceId: sweepSrc,
                NumericOptions: numeric));

        PrintManifestSummary(res);
        return res.Status == RunTerminalStatus.Succeeded ? 0 : 4;
    }

    private static async Task<int> RunSchematicAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: missing <schematic.json> path");
            return 2;
        }
        var jsonPath = args[0];
        var analysis = AnalysisKind.OperatingPoint;
        string? svgOut = null;
        string? spiceOut = null;

        for (int i = 1; i < args.Length; ++i)
        {
            if (args[i] == "--analysis" && i + 1 < args.Length) analysis = ParseAnalysisKind(args[++i]);
            else if (args[i] == "--svg" && i + 1 < args.Length) svgOut = args[++i];
            else if (args[i] == "--spice" && i + 1 < args.Length) spiceOut = args[++i];
        }

        var doc = SchematicCompiler.DeserializeJson(await File.ReadAllTextAsync(jsonPath));
        var compiled = SchematicCompiler.CompileToIr(doc);
        if (!compiled.Succeeded || compiled.Circuit is null)
        {
            Console.Error.WriteLine("ERC failed:");
            foreach (var d in compiled.Diagnostics) Console.Error.WriteLine($"  [{d.Code}] {d.Message}");
            return 3;
        }

        using var backend = new NativeCppBackend(enableAudits: true);
        var res = await backend.RunAsync(new SimulationRequestIr(doc.Name, compiled.Circuit, analysis));
        var opRes = analysis == AnalysisKind.OperatingPoint
            ? res
            : await backend.RunAsync(new SimulationRequestIr(doc.Name + "_op", compiled.Circuit, AnalysisKind.OperatingPoint));

        if (!string.IsNullOrWhiteSpace(svgOut))
        {
            var overlay = opRes.Status == RunTerminalStatus.Succeeded
                ? SchematicCompiler.BuildOperatingPointOverlay(doc, opRes)
                : null;
            await File.WriteAllTextAsync(svgOut, SchematicCompiler.ExportSvg(doc, overlay));
        }
        if (!string.IsNullOrWhiteSpace(spiceOut))
        {
            await File.WriteAllTextAsync(spiceOut, SchematicCompiler.ExportSpiceNetlist(doc, analysis));
        }

        PrintManifestSummary(res);
        return res.Status == RunTerminalStatus.Succeeded ? 0 : 4;
    }

    private static async Task<int> RunCampaignAsync(string[] args)
    {
        using var backend = new NativeCppBackend(enableAudits: true);
        double[] rValues = [1_000.0, 2_000.0, 4_000.0, 9_000.0];
        var results = await CampaignRunner.RunParallelMapAsync(
            rValues,
            (r2, idx) =>
            {
                var c = new CircuitBuilder($"divider_corner_{idx}")
                    .AddVoltageSource("V1", "in", "0", SiUnits.Volts(10.0))
                    .AddResistor("R1", "in", "out", SiUnits.KiloOhms(1.0))
                    .AddResistor("R2", "out", "0", r2)
                    .Build();
                return new SimulationRequestIr($"corner_{idx}", c, AnalysisKind.OperatingPoint);
            },
            backend,
            maxDegreeOfParallelism: 4);

        var summary = results.Select((r, i) => new
        {
            case_id = r.CaseId,
            r2_ohms = rValues[i],
            status = r.Status.ToString(),
            v_out = r.RealData?.SignalValues.TryGetValue("v(out)", out var vals) == true ? vals[0] : double.NaN
        });
        Console.WriteLine(JsonSerializer.Serialize(new { campaign = "rc-sweep", runs = summary }, JsonOpts));
        return results.All(r => r.Status == RunTerminalStatus.Succeeded) ? 0 : 4;
    }

    private static void PrintManifestSummary(ResultManifestIr res)
    {
        var payload = new
        {
            case_id = res.CaseId,
            backend = res.BackendId,
            status = res.Status.ToString(),
            error_code = res.ErrorCode,
            error_message = res.ErrorMessage,
            signals = res.Signals.Select(s => s.Id).ToArray(),
            samples = res.RealData?.AxisValues.Count ?? res.ComplexData?.AxisValues.Count ?? 0
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));
    }

    private static AnalysisKind ParseAnalysisKind(string s) => s.ToLowerInvariant() switch
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

    private static double ParseDouble(string s) =>
        double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
