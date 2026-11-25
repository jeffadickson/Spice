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
                "library" => RunLibrary(args[1..]),
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
              spice-app schematic <file(.spice.json|.spicenet|.spice.svg|.velosim.json)>
                                       [--analysis op|dc|ac|tran]
                                       [--dialect spice|ngspice|xyce|ltspice|spectre]
                                       [--svg out.svg] [--polyglot-svg out.spice.svg]
                                       [--spicenet out.spicenet] [--velosim out.velosim.json]
                                       [--circuit-json out.circuit.json]
                                       [--spice out.cir] [--bom out.csv]
                                       [--starlark out.star] [--bundle out.bundle.json]
              spice-app library [list]
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

    private static int RunLibrary(string[] args)
    {
        var lib = new ComponentLibraryStore(ComponentLibraryStore.GetDefaultLibraryPath());
        var items = lib.ListAll().Select(p => new
        {
            id = p.Id,
            name = p.Name,
            category = p.Category,
            kind = p.BaseDeviceKindId,
            built_in = p.IsBuiltIn,
            model = p.AttachedModel?.ModelName,
            model_kind = p.AttachedModel?.Kind.ToString(),
            pins = p.Pins.Select(x => x.Name).ToArray()
        });
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            storage_path = lib.StoragePath,
            count = lib.ListAll().Count,
            components = items
        }, JsonOpts));
        return 0;
    }

    private static async Task<int> RunSchematicAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: missing schematic/netlist path");
            return 2;
        }
        var inputPath = args[0];
        var analysis = AnalysisKind.OperatingPoint;
        var dialect = NetlistDialect.CanonicalSpice3f5;
        string? svgOut = null;
        string? polyglotSvgOut = null;
        string? spicenetOut = null;
        string? velosimOut = null;
        string? circuitJsonOut = null;
        string? spiceOut = null;
        string? bomOut = null;
        string? starlarkOut = null;
        string? bundleOut = null;

        for (int i = 1; i < args.Length; ++i)
        {
            if (args[i] == "--analysis" && i + 1 < args.Length) analysis = ParseAnalysisKind(args[++i]);
            else if (args[i] == "--dialect" && i + 1 < args.Length) dialect = ParseDialect(args[++i]);
            else if (args[i] == "--svg" && i + 1 < args.Length) svgOut = args[++i];
            else if (args[i] == "--polyglot-svg" && i + 1 < args.Length) polyglotSvgOut = args[++i];
            else if (args[i] == "--spicenet" && i + 1 < args.Length) spicenetOut = args[++i];
            else if (args[i] == "--velosim" && i + 1 < args.Length) velosimOut = args[++i];
            else if (args[i] == "--circuit-json" && i + 1 < args.Length) circuitJsonOut = args[++i];
            else if (args[i] == "--spice" && i + 1 < args.Length) spiceOut = args[++i];
            else if (args[i] == "--bom" && i + 1 < args.Length) bomOut = args[++i];
            else if (args[i] == "--starlark" && i + 1 < args.Length) starlarkOut = args[++i];
            else if (args[i] == "--bundle" && i + 1 < args.Length) bundleOut = args[++i];
        }

        var lib = new ComponentLibraryStore(ComponentLibraryStore.GetDefaultLibraryPath());
        var rawText = await File.ReadAllTextAsync(inputPath);
        var trimmed = rawText.TrimStart();
        SchematicDocument doc;
        FlagshipNetlistDocument? flagshipSource = null;

        if (inputPath.EndsWith(".spicenet", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("netlist ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            var parsed = FlagshipNetlist.Parse(rawText, lib);
            if (!parsed.Succeeded || parsed.Document is null)
            {
                Console.Error.WriteLine("Flagship netlist parse failed:");
                foreach (var d in parsed.Diagnostics) Console.Error.WriteLine($"  [{d.Code}] {d.Message}");
                return 3;
            }
            flagshipSource = parsed.Document;
            doc = FlagshipNetlist.ToSchematicDocument(parsed.Document, lib);
        }
        else if (trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = FlagshipNetlist.ExtractFromPolyglotSvg(rawText, lib);
            if (!parsed.Succeeded || parsed.Document is null)
            {
                Console.Error.WriteLine("Polyglot SVG netlist extraction failed:");
                foreach (var d in parsed.Diagnostics) Console.Error.WriteLine($"  [{d.Code}] {d.Message}");
                return 3;
            }
            flagshipSource = parsed.Document;
            doc = FlagshipNetlist.ToSchematicDocument(parsed.Document, lib);
        }
        else if (trimmed.Contains("\"velosim.netlist.v1\"", StringComparison.Ordinal))
        {
            var parsed = VeloSimIngestion.ImportVeloSimNetlistJson(rawText, lib);
            if (!parsed.Succeeded || parsed.Document is null)
            {
                Console.Error.WriteLine("VeloSim netlist ingestion failed:");
                foreach (var d in parsed.Diagnostics) Console.Error.WriteLine($"  [{d.Code}] {d.Message}");
                return 3;
            }
            flagshipSource = parsed.Document;
            doc = FlagshipNetlist.ToSchematicDocument(parsed.Document, lib);
        }
        else
        {
            doc = SchematicCompiler.DeserializeJson(rawText);
        }

        var compiled = SchematicCompiler.CompileToIr(doc, library: lib);
        if (!compiled.Succeeded || compiled.Circuit is null)
        {
            Console.Error.WriteLine("ERC failed:");
            foreach (var d in compiled.Diagnostics) Console.Error.WriteLine($"  [{d.Code}] {d.Message}");
            return 3;
        }

        using var backend = new NativeCppBackend(enableAudits: true);
        var exportOpts = new SchematicExportOptions(dialect, analysis);
        var canonicalDeck = SchematicExporter.ExportNetlist(doc, new SchematicExportOptions(NetlistDialect.CanonicalSpice3f5, analysis), library: lib);
        var usesFullDeck = (compiled.Circuit.Models is { Count: > 0 }) ||
                           compiled.Circuit.Devices.Any(d => d.KindId is "spice.mosfet" or "spice.vdmos" or "spice.bjt" or "spice.jfet" or "spice.mesfet" or "spice.switch" or "spice.cswitch" or "spice.core" or "spice.subckt" or "spice.osdi");
        var res = usesFullDeck
            ? await backend.RunSpiceDeckAsync(doc.Name, canonicalDeck, analysis)
            : await backend.RunAsync(new SimulationRequestIr(doc.Name, compiled.Circuit, analysis));
        var opRes = analysis == AnalysisKind.OperatingPoint
            ? res
            : (usesFullDeck
                ? await backend.RunSpiceDeckAsync(doc.Name + "_op", canonicalDeck, AnalysisKind.OperatingPoint)
                : await backend.RunAsync(new SimulationRequestIr(doc.Name + "_op", compiled.Circuit, AnalysisKind.OperatingPoint)));

        var overlay = opRes.Status == RunTerminalStatus.Succeeded
            ? SchematicCompiler.BuildOperatingPointOverlay(doc, opRes)
            : null;
        var flagshipOutDoc = flagshipSource ?? FlagshipNetlist.FromSchematic(doc, lib);

        if (!string.IsNullOrWhiteSpace(svgOut))
        {
            await File.WriteAllTextAsync(svgOut, SchematicCompiler.ExportSvg(doc, overlay, lib));
        }
        if (!string.IsNullOrWhiteSpace(polyglotSvgOut))
        {
            await File.WriteAllTextAsync(polyglotSvgOut, FlagshipNetlist.ExportPolyglotSvg(flagshipOutDoc, overlay, lib));
        }
        if (!string.IsNullOrWhiteSpace(spicenetOut))
        {
            await File.WriteAllTextAsync(spicenetOut, FlagshipNetlist.Format(flagshipOutDoc));
        }
        if (!string.IsNullOrWhiteSpace(velosimOut))
        {
            await File.WriteAllTextAsync(velosimOut, VeloSimIngestion.ExportVeloSimNetlistJson(flagshipOutDoc, lib));
        }
        if (!string.IsNullOrWhiteSpace(circuitJsonOut))
        {
            await File.WriteAllTextAsync(circuitJsonOut, VeloSimIngestion.ExportCircuitJson(flagshipOutDoc, lib));
        }
        if (!string.IsNullOrWhiteSpace(spiceOut))
        {
            await File.WriteAllTextAsync(spiceOut, SchematicExporter.ExportNetlist(doc, exportOpts, library: lib));
        }
        if (!string.IsNullOrWhiteSpace(bomOut))
        {
            await File.WriteAllTextAsync(bomOut, SchematicExporter.ExportBomCsv(doc, lib));
        }
        if (!string.IsNullOrWhiteSpace(starlarkOut))
        {
            await File.WriteAllTextAsync(starlarkOut, SchematicExporter.ExportStarlarkScript(doc, library: lib));
        }
        if (!string.IsNullOrWhiteSpace(bundleOut))
        {
            await File.WriteAllTextAsync(bundleOut, SchematicExporter.ExportBundleJson(doc, exportOpts, library: lib));
        }

        PrintManifestSummary(res);
        return res.Status == RunTerminalStatus.Succeeded ? 0 : 4;
    }

    private static NetlistDialect ParseDialect(string s) => s.ToLowerInvariant() switch
    {
        "ngspice" => NetlistDialect.Ngspice,
        "xyce" => NetlistDialect.Xyce,
        "ltspice" => NetlistDialect.Ltspice,
        "spectre" => NetlistDialect.Spectre,
        _ => NetlistDialect.CanonicalSpice3f5
    };

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
