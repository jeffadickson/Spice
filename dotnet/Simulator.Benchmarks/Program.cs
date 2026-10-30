using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace Simulator.Benchmarks;
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if(args.Contains("--corpus-root",StringComparer.Ordinal))return await CorpusCommand.Run(args);
        if (args is ["--help"]) { Console.WriteLine("--root DIR --engine EXE --ngspice EXE [--manifest compat/benchmarks/suite.json] [--warmups 1] [--iterations 3] [--budget-seconds 300] [--report NEW.json]"); return 0; }
        BenchmarkReport? report = null; string? destination = null;
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i += 2)
                if (i + 1 >= args.Length || args[i] is not ("--root" or "--engine" or "--ngspice" or "--manifest" or "--warmups" or "--iterations" or "--budget-seconds" or "--report") || !options.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("Unknown, duplicated or incomplete option; use --help.");
            string Option(string name, string fallback) => options.GetValueOrDefault(name, fallback);
            var root = Path.GetFullPath(Option("--root", Directory.GetCurrentDirectory()));
            if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "Cargo.toml"))) throw new DirectoryNotFoundException("Expected Spice checkout root.");
            var engine = Path.GetFullPath(Option("--engine", "target/release/circuit-simulator" + (OperatingSystem.IsWindows() ? ".exe" : "")), root);
            var ngspice = Path.GetFullPath(Option("--ngspice", "ngspice"), root);
            if (!File.Exists(engine) || !File.Exists(ngspice)) throw new FileNotFoundException("Supply existing engine and ngspice executable paths.");
            var warmups = int.Parse(Option("--warmups", "1"), CultureInfo.InvariantCulture);
            var iterations = int.Parse(Option("--iterations", "3"), CultureInfo.InvariantCulture);
            var seconds = int.Parse(Option("--budget-seconds", "300"), CultureInfo.InvariantCulture);
            if (seconds is < 1 or > 3600) throw new ArgumentException("Budget must be 1..3600 seconds.");
            var manifest = Suites.SafeInput(root, Option("--manifest", "compat/benchmarks/suite.json"));
            var suite = JsonSerializer.Deserialize(Runner.Input(manifest), BenchmarkJson.Default.SuiteDefinition) ?? throw new InvalidDataException("Missing suite.");
            Suites.Validate(suite, warmups, iterations);
            var build = Path.Combine(root, "build"); Directory.CreateDirectory(build);
            var parent = Path.Combine(build, "benchmarks"); Directory.CreateDirectory(parent);
            var raw = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(raw);
            destination = Path.GetFullPath(Option("--report", Path.Combine(raw, "report.json")), root);
            if (!Directory.Exists(Path.GetDirectoryName(destination)) || File.Exists(destination)) throw new IOException("Report parent must exist and report must be new.");
            var hashes = new Dictionary<string, string> { ["engine_executable"] = Runner.Hash(engine), ["ngspice_executable"] = Runner.Hash(ngspice), ["suite"] = Runner.Hash(manifest) };
            foreach (var folder in new[] { "src", "dotnet/Simulator.Benchmarks", "dotnet/Simulator.Client" })
                foreach (var file in Directory.GetFiles(Path.Combine(root, folder), folder == "src" ? "*.rs" : "*.cs"))
                    hashes[Path.GetRelativePath(root, file).Replace('\\', '/')] = Runner.Hash(file);
            foreach (var file in new[] { "Cargo.toml", "Cargo.lock", "rust-toolchain.toml", "global.json", "dotnet/Simulator.Benchmarks/Simulator.Benchmarks.csproj" }) hashes[file] = Runner.Hash(Path.Combine(root, file));
            var helper = RuntimeFeature.IsDynamicCodeSupported ? Path.Combine(AppContext.BaseDirectory, "Simulator.Benchmarks.dll") : Environment.ProcessPath;
            if (helper is not null && File.Exists(helper)) hashes["executed_helper"] = Runner.Hash(helper);
            var revision = await ProcessRunner.Run("git", ["rev-parse", "HEAD"], null, root, TimeSpan.FromSeconds(10));
            var dirty = await ProcessRunner.Run("git", ["status", "--porcelain"], null, root, TimeSpan.FromSeconds(10));
            if (revision.ExitCode != 0 || dirty.ExitCode != 0) throw new IOException("Source provenance unavailable.");
            report = new("1", "in_progress", null, revision.Output.Trim(), dirty.Output.Length != 0,
                RuntimeInformation.OSDescription + " / " + RuntimeInformation.ProcessArchitecture,
                RuntimeFeature.IsDynamicCodeSupported ? "managed" : "native_aot", warmups, iterations, seconds,
                Stopwatch.Frequency, Stopwatch.IsHighResolution,
                "Spawn + stdin + child setup/solve/serialization + pipe drain + exit; excludes parent comparison/artifact writing; includes supervision overhead.",
                "OS-reported peak resident working set includes shared pages; unavailable metrics are null, not zero.",
                hashes, "unavailable until completed version probe", [],
                ["Small supported paired fixtures; different output formats and transient step controllers prevent pure solver-speed claims.",
                 "Warmups excluded; raw repetitions and order rotation retained; machine load/affinity were not controlled.",
                 "Named-point accuracy is finite evidence, not full SPICE or a rigorous global error bound.",
                 "ngspice compatible work counters are unavailable; absent engine counters remain null.",
                 "Executable/source hashes identify inputs; dynamic libraries and the entire OS image are not frozen."], raw);
            using var cancel = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
            Console.CancelKeyPress += handler;
            try { report = await Runner.Run(root, engine, ngspice, suite, report, cancel.Token); }
            finally { Console.CancelKeyPress -= handler; }
            File.WriteAllText(destination, JsonSerializer.Serialize(report, BenchmarkJson.Default.BenchmarkReport));
            Console.WriteLine(destination);
            return report.Status == "passed" ? 0 : 1;
        }
        catch (Exception error)
        {
            var message = error.GetType().Name + ": " + error.Message;
            if (message.Length > 2048) message = message[..2048];
            if (report is not null && destination is not null)
            {
                // Failed setup/provenance/deadline invalidates eligibility, even
                // when an earlier case already happened to match numerically.
                var invalid = report.Cases.Select(c => c with { Timings = c.Timings.Select(t => t with {
                    Eligible = false, MedianMs = null, MinimumMs = null, MaximumMs = null }).ToArray() }).ToList();
                File.WriteAllText(destination, JsonSerializer.Serialize(report with { Status = "failed", Failure = message, Cases = invalid }, BenchmarkJson.Default.BenchmarkReport));
            }
            Console.Error.WriteLine(message); return 1;
        }
    }
}
