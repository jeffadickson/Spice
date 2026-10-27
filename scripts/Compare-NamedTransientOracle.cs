#:property PublishAot=false
#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulator.Client;

// Compare a named real observable at explicit shared times. Linear interpolation
// is declared evidence processing, not hidden simulator accuracy or extrapolation.
if (args is ["--self-test"])
{
    CheckInputBounds();
    var rows = Parse("Index time v(out)\n0 0 0\n1 1 2\n", "v(out)");
    if (At(rows, 0.25) != 0.5) throw new InvalidDataException("Interpolation regression.");
    foreach (var text in new[] { "Index time v(other)\n0 0 0\n", "Index time v(out)\n0 -1 0\n",
        "Index time v(out)\n0 0 NaN\n", "Index time v(out)\n0 0 1\n1 0 2\n" })
    {
        try { Parse(text, "v(out)"); throw new InvalidOperationException("Malformed table accepted."); }
        catch (InvalidDataException) { }
    }
    try { At(rows, 2.0); throw new InvalidOperationException("Extrapolation accepted."); }
    catch (InvalidDataException) { }
    Console.WriteLine("Named transient parser/interpolation: six contracts passed.");
    return;
}
if (args.Length != 7) throw new ArgumentException("Expected ngspice, engine, deck, request, observable, absolute tolerance, comma-separated times.");
var tolerance = double.Parse(args[5], CultureInfo.InvariantCulture);
var times = args[6].Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
if (!double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 0.01 || times.Length is < 1 or > 32 ||
    times.Any(value => !double.IsFinite(value) || value < 0) || times.Distinct().Count() != times.Length)
    throw new ArgumentException("Invalid bounded comparison policy.");
var files = args.Take(4).Select(Path.GetFullPath).ToArray();
foreach (var file in files) if (!File.Exists(file)) throw new FileNotFoundException("Missing oracle input.", file);
if (!File.Exists("Cargo.toml") || !Directory.Exists("evidence")) throw new ArgumentException("Run at checkout root.");
var build = Path.GetFullPath("build");
if (!Directory.Exists(build)) Directory.CreateDirectory(build);
var parent = Path.Combine(build, "ngspice-oracles");
if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
var run = Path.Combine(parent, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);
var executed = Path.Combine(run, "oracle.cir");
File.WriteAllBytes(executed, ReadBoundedInput(files[2]));
var log = Path.Combine(run, "ngspice.log");
var version = await Run(files[0], ["--version"], run);
await Run(files[0], ["-n", "-b", "-o", log, executed], run);
if (!File.Exists(log) || new FileInfo(log).Length > 8_388_608) throw new InvalidDataException("Missing/oversized table.");
var reference = Parse(File.ReadAllText(log), args[4]);
var request = JsonNode.Parse(ReadBoundedInput(files[3])) as JsonObject ?? throw new InvalidDataException("Missing request.");
if (request["analysis"]?["kind"]?.GetValue<string>() != "transient_configured") throw new InvalidDataException("Expected configured transient.");
var comparisons = new List<object>();
var passed = true;
foreach (var backend in new[] { "lu", "faer" })
{
    var selected = (JsonObject)request.DeepClone();
    selected["backend"] = backend;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var manifest = (await new SimulationClient(files[1]).RunJsonAsync(selected.ToJsonString(), timeout.Token)).ReadManifest();
    var column = Array.FindIndex(manifest.Signals, value => value.Id.Equals(args[4], StringComparison.OrdinalIgnoreCase));
    if (column < 0 || manifest.Samples.Length == 0) throw new InvalidDataException("Missing named waveform.");
    var observed = new SortedDictionary<double, double>();
    foreach (var sample in manifest.Samples)
        if (!double.IsFinite(sample.TimeS) || !double.IsFinite(sample.Values[column]) || !observed.TryAdd(sample.TimeS, sample.Values[column]))
            throw new InvalidDataException("Invalid/duplicate simulator sample.");
    foreach (var time in times)
    {
        var expected = At(reference, time);
        var actual = At(observed, time);
        var difference = Math.Abs(expected - actual);
        passed &= double.IsFinite(difference) && difference <= tolerance;
        comparisons.Add(new { backend, time_s = time, observable = args[4], expected, actual, difference, tolerance });
    }
}
string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
var destination = Path.Combine("evidence", "named-transient-" + Path.GetFileNameWithoutExtension(files[2]) + ".json");
File.WriteAllText(destination, JsonSerializer.Serialize(new { status = passed ? "passed" : "failed",
    ngspice_version = version, deck_sha256 = Hash(files[2]), executed_deck_sha256 = Hash(executed),
    request_sha256 = Hash(files[3]), ngspice_sha256 = Hash(files[0]), engine_sha256 = Hash(files[1]),
    helper_sha256 = Hash("scripts/Compare-NamedTransientOracle.cs"), comparisons,
    interpolation = "Piecewise linear on each recorded grid; no extrapolation. Print rounding and finite time steps limit comparison.",
    limitations = "Finite named cases, not full SPICE, rigorous global error or arbitrary event conformance."
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(destination);
if (!passed) Environment.ExitCode = 1;

static SortedDictionary<double, double> Parse(string text, string observable)
{
    var rows = new SortedDictionary<double, double>();
    var active = false;
    foreach (var line in text.Split('\n'))
    {
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 3 && fields[0].Equals("Index", StringComparison.OrdinalIgnoreCase))
        {
            active = fields[1].Equals("time", StringComparison.OrdinalIgnoreCase) && fields[2].Equals(observable, StringComparison.OrdinalIgnoreCase);
            continue;
        }
        if (!active || fields.Length == 0 || line.Trim().All(value => value == '-')) continue;
        if (fields.Length != 3 || !int.TryParse(fields[0], out var index)) { active = false; continue; }
        if (index < 0 || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ||
            !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(time) || !double.IsFinite(value) || time < 0 || rows.Count >= 100_000)
            throw new InvalidDataException("Malformed transient table row.");
        // Rounded duplicate times are allowed only when their printed value is
        // identical. Distinct values at one printed time are ambiguous evidence.
        if (!rows.TryAdd(time, value) && rows[time] != value) throw new InvalidDataException("Ambiguous printed time.");
    }
    if (rows.Count == 0) throw new InvalidDataException("Missing named real time table.");
    return rows;
}

static double At(SortedDictionary<double, double> rows, double time)
{
    if (time < rows.First().Key || time > rows.Last().Key) throw new InvalidDataException("Reference time outside recorded grid.");
    if (rows.TryGetValue(time, out var exact)) return exact;
    var before = rows.Last(pair => pair.Key < time);
    var after = rows.First(pair => pair.Key > time);
    var result = before.Value + (after.Value - before.Value) * ((time - before.Key) / (after.Key - before.Key));
    return double.IsFinite(result) ? result : throw new InvalidDataException("Interpolation overflow.");
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
        var result = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
        {
            if (result.Length + count > 1_048_576) throw new InvalidDataException("Oracle diagnostics exceeded bound.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
    try
    {
        var output = Read(process.StandardOutput); var errors = Read(process.StandardError);
        await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token));
        if (process.ExitCode != 0) throw new IOException("Oracle exit " + process.ExitCode);
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