#:property PublishAot=false
#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Simulator.Client;

// Named complex columns and frequencies are mandatory: never infer a physical
// observable from the position of an unrelated numeric column.
if (args is ["--self-test"])
{
    CheckInputBounds();
    var good = Parse("Index frequency v(out)\n0 100 1.5, -0.5\n", "v(out)");
    if (good[100] != new System.Numerics.Complex(1.5, -0.5)) throw new InvalidDataException("Parser regression.");
    foreach (var text in new[] { "Index frequency v(other)\n0 100 1, 0\n",
        "Index frequency v(out)\n0 100 NaN, 0\n",
        "Index frequency v(out)\n0 100 1, 0\n1 100 2, 0\n",
        "Index frequency v(out)\n-1 100 1, 0\n",
        "Index frequency v(out)\n0 -100 1, 0\n",
        "Index frequency v(out)\n0 100 invalid, 0\n",
        "Index frequency v(out)\n0 100 1, Infinity\n" })
    {
        try { Parse(text, "v(out)"); throw new InvalidOperationException("Malformed oracle accepted."); }
        catch (InvalidDataException) { }
    }
    Console.WriteLine("Named AC parser: eight contracts passed.");
    return;
}
if (args.Length != 6) throw new ArgumentException("Expected ngspice, engine, deck, request, named observable, absolute tolerance.");
var tolerance = double.Parse(args[5], CultureInfo.InvariantCulture);
if (!double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 1e-3) throw new ArgumentException("Invalid tolerance.");
var ngspice = Path.GetFullPath(args[0]);
var engine = Path.GetFullPath(args[1]);
var deck = Path.GetFullPath(args[2]);
var requestPath = Path.GetFullPath(args[3]);
foreach (var file in new[] { ngspice, engine, deck, requestPath })
    if (!File.Exists(file)) throw new FileNotFoundException("Oracle input missing.", file);
var root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "Cargo.toml"))) throw new ArgumentException("Run at repository root.");
var build = Path.Combine(root, "build");
if (!Directory.Exists(build)) Directory.CreateDirectory(build);
var parent = Path.Combine(build, "ngspice-oracles");
if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
var run = Path.Combine(parent, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);
var executed = Path.Combine(run, "oracle.cir");
// Preserve the input verbatim and disable user spiceinit scripts. Printed
// precision is a fixture limitation and must be reflected in its tolerance.
File.WriteAllBytes(executed, ReadBoundedInput(deck));
var log = Path.Combine(run, "ngspice.log");
var version = await Run(ngspice, ["--version"], run);
await Run(ngspice, ["-n", "-b", "-o", log, executed], run);
if (!File.Exists(log) || new FileInfo(log).Length > 8_388_608) throw new InvalidDataException("Missing/oversized oracle table.");
var reference = Parse(File.ReadAllText(log), args[4]);
// Oracle input is the native public JSON contract. The typed SDK intentionally
// exposes fewer element families and must not reject valid native VCCS decks.
var request = JsonNode.Parse(ReadBoundedInput(requestPath)) as JsonObject
    ?? throw new InvalidDataException("Missing native request.");
if (request["analysis"]?["kind"]?.GetValue<string>() != "ac" ||
    request["analysis"]?["frequencies_hz"] is not JsonArray frequencies ||
    frequencies.Count is < 1 or > 10000) throw new InvalidDataException("Expected bounded AC request.");
var comparisons = new List<object>();
var passed = true;
foreach (var backend in new[] { "lu", "faer" })
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var selected = (JsonObject)request.DeepClone();
    selected["backend"] = backend;
    var manifest = (await new SimulationClient(engine).RunJsonAsync(selected.ToJsonString(), timeout.Token)).ReadManifest();
    var column = Array.FindIndex(manifest.Signals, signal => signal.Id.Equals(args[4], StringComparison.OrdinalIgnoreCase));
    if (column < 0 || manifest.AcSamples.Length != frequencies.Count) throw new InvalidDataException("Missing named simulator output.");
    foreach (var sample in manifest.AcSamples)
    {
        var matches = reference.Where(pair => Math.Abs(pair.Key - sample.FrequencyHz) <= 1e-12 * sample.FrequencyHz).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Oracle frequency is missing or ambiguous.");
        var expected = matches[0].Value;
        var value = sample.Values[column];
        var difference = Math.Max(Math.Abs(value.Real - expected.Real), Math.Abs(value.Imag - expected.Imaginary));
        comparisons.Add(new { backend, frequency_hz = sample.FrequencyHz, observable = args[4],
            expected_real = expected.Real, expected_imag = expected.Imaginary,
            actual_real = value.Real, actual_imag = value.Imag, maximum_component_difference = difference, tolerance });
        passed &= double.IsFinite(difference) && difference <= tolerance;
    }
}
string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
var report = new { status = passed ? "passed" : "failed", ngspice_version = version, deck_sha256 = Hash(deck),
    executed_deck_sha256 = Hash(executed), request_sha256 = Hash(requestPath),
    ngspice_sha256 = Hash(ngspice), engine_sha256 = Hash(engine), helper_sha256 = Hash("scripts/Compare-NamedAcOracle.cs"),
    comparisons, limitations = "Finite named observables; absolute component tolerance, no full SPICE or global conditioning claim." };
var evidence = Path.Combine(root, "evidence");
if (!Directory.Exists(evidence)) throw new DirectoryNotFoundException(evidence);
var destination = Path.Combine(evidence, "named-ac-" + Path.GetFileNameWithoutExtension(deck) + ".json");
File.WriteAllText(destination, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(destination);
if (!passed) Environment.ExitCode = 1;

static Dictionary<double, System.Numerics.Complex> Parse(string text, string observable)
{
    var rows = new Dictionary<double, System.Numerics.Complex>();
    var active = false;
    foreach (var line in text.Split('\n'))
    {
        var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 3 && words[0].Equals("Index", StringComparison.OrdinalIgnoreCase))
        {
            active = words[1].Equals("frequency", StringComparison.OrdinalIgnoreCase) && words[2].Equals(observable, StringComparison.OrdinalIgnoreCase);
            continue;
        }
        if (!active || string.IsNullOrWhiteSpace(line) || line.Trim().All(c => c == '-')) continue;
        var fields = line.Replace(",", " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 4 || !int.TryParse(fields[0], out var index)) { active = false; continue; }
        if (index < 0) throw new InvalidDataException("Negative oracle row index.");
        var numbers = new double[3];
        for (var column = 0; column < numbers.Length; column++)
            if (!double.TryParse(fields[column + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[column]))
                throw new InvalidDataException("Malformed numeric oracle column.");
        if (numbers.Any(v => !double.IsFinite(v)) || numbers[0] <= 0 || !rows.TryAdd(numbers[0], new(numbers[1], numbers[2])))
            throw new InvalidDataException("Invalid/duplicate oracle row.");
    }
    if (rows.Count == 0) throw new InvalidDataException("Missing named complex frequency table.");
    return rows;
}

static async Task<string> Run(string executable, string[] arguments, string directory)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false,
        RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException("Could not start oracle.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    async Task<string> Read(StreamReader reader)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) != 0)
        {
            if (text.Length + count > 1_048_576) throw new InvalidDataException("Oracle diagnostics exceeded bound.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
    try
    {
        var output = Read(process.StandardOutput);
        var errors = Read(process.StandardError);
        await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token));
        if (process.ExitCode != 0) throw new IOException("Oracle exited " + process.ExitCode);
        return await output + await errors;
    }
    finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
}

// Bound raw deck/request allocation before parsing or copying. Read exactly one
// immutable-size snapshot; truncated/growing files cannot bypass the byte cap.
static byte[] ReadBoundedInput(string path)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (stream.Length > 1_048_576) throw new InvalidDataException("Oracle input exceeds 1 MiB.");
    var bytes = new byte[checked((int)stream.Length)];
    stream.ReadExactly(bytes);
    if (stream.ReadByte() != -1) throw new InvalidDataException("Oracle input changed while reading.");
    return bytes;
}

static void CheckInputBounds()
{
    var path = Path.GetTempFileName();
    try
    {
        File.WriteAllBytes(path, [1, 2, 3]);
        if (!ReadBoundedInput(path).SequenceEqual(new byte[] {1, 2, 3})) throw new InvalidDataException("Input snapshot regression.");
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write)) stream.SetLength(1_048_577);
        try { ReadBoundedInput(path); throw new InvalidOperationException("Oversized input accepted."); }
        catch (InvalidDataException) { }
    }
    finally { File.Delete(path); }
}