using System.Diagnostics;
using System.Text.Json;

namespace Simulator.Client;

/// <summary>Typed response metadata with owned JSON payload for the experimental schema.</summary>
public sealed record SimulationResponse(string SchemaVersion, string Status, bool Complete, JsonElement Payload)
{
    /// <summary>Failed envelopes are not valid successful manifests.</summary>
    public ResultManifest ReadManifest()
    {
        if (!Succeeded) throw new InvalidOperationException("No successful result manifest.");
        if (SchemaVersion != "1") throw new InvalidDataException("Unsupported outer result version.");
        // Explicit type metadata keeps trimmed/NativeAOT builds on the same wire contract.
        var manifest = Payload.Deserialize(WireContext.Default.ResultManifest) ?? throw new InvalidDataException("Missing manifest.");
        if (manifest.SchemaVersion != "1" || manifest.Status != "succeeded" || !manifest.Complete ||
            manifest.Signals is null || manifest.Samples is null || manifest.AcSamples is null ||
            manifest.Measurements is null || manifest.Assertions is null || manifest.Backend is null ||
            manifest.Signals.Any(s => s is null) || manifest.Measurements.Any(m => m is null) ||
            manifest.Assertions.Any(a => a is null) ||
            manifest.Samples.Any(s => s is null || s.Values is null || s.Values.Length != manifest.Signals.Length) ||
            manifest.AcSamples.Any(s => s is null || s.Values is null || s.Values.Length != manifest.Signals.Length || s.Values.Any(v => v is null)))
            throw new InvalidDataException("Malformed or incomplete result manifest.");
        return manifest;
    }
    /// <summary>True only for a successful, complete envelope; physical accuracy requires separate evidence.</summary>
    public bool Succeeded => Status == "succeeded" && Complete;
    /// <summary>Engine input/numerical failures are distinct from transport exceptions.</summary>
    public SimulationError? Error
    {
        get
        {
            JsonElement error = default;
            return Payload.TryGetProperty("error", out error)
                ? error.Deserialize(WireContext.Default.SimulationError) : null;
        }
    }
    /// <summary>Read the final voltage/current by descriptor ID; missing data is an error.</summary>
    public double FinalValue(string signalId)
    {
        if (!Succeeded) throw new InvalidOperationException("Simulation did not complete successfully.");
        var signals = Payload.GetProperty("signals").EnumerateArray().ToArray();
        var index = Array.FindIndex(signals, s => s.GetProperty("id").GetString() == signalId);
        if (index < 0) throw new KeyNotFoundException(signalId);
        var samples = Payload.GetProperty("samples");
        if (samples.GetArrayLength() == 0) throw new InvalidOperationException("No real samples recorded.");
        return samples[samples.GetArrayLength() - 1].GetProperty("values")[index].GetDouble();
    }
}

/// <summary>One-shot local executable transport, not yet a persistent worker/C ABI SDK.</summary>
public sealed class SimulationClient(string executable)
{
    /// <summary>Typed authoring path shared by C# and F#; Rust still validates semantics.</summary>
    public Task<SimulationResponse> RunAsync(SimulationRequest request, CancellationToken cancellation = default) =>
        RunJsonAsync(JsonSerializer.Serialize(request, WireContext.Default.SimulationRequest), cancellation);
    /// <summary>Rust validates the experimental request schema. No shell is invoked.</summary>
    public async Task<SimulationResponse> RunJsonAsync(string requestJson, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (System.Text.Encoding.UTF8.GetByteCount(requestJson) > 1_048_576)
            throw new ArgumentException("Request exceeds 1 MiB.", nameof(requestJson));
        using var process = Process.Start(CreateStartInfo(executable)) ?? throw new IOException("Could not start simulator.");
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput.BaseStream, cancellation);
            var errors = ReadBoundedAsync(process.StandardError.BaseStream, cancellation);
            await process.StandardInput.WriteAsync(requestJson.AsMemory(), cancellation);
            process.StandardInput.Close();
            var exit = process.WaitForExitAsync(cancellation);
            await DrainAsync([output, errors, exit], cancellation);
            var text = await output;
            var diagnostics = await errors;
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            // Keep field holders assigned independently of short-circuit guards.
            // This preserves Undefined for an unread field and lets mutation
            // instrumentation challenge guards without definite-assignment errors.
            JsonElement versionValue = default, statusValue = default, completion = default,
                code = default, message = default;
            // Validate types before reading them so malformed child output has
            // one transport-contract error rather than incidental JSON failures.
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema_version", out versionValue) ||
                versionValue.ValueKind != JsonValueKind.String || versionValue.GetString() != "1")
                throw new InvalidDataException("Unsupported or malformed result version.");
            var version = versionValue.GetString()!;
            if (!root.TryGetProperty("status", out statusValue) || statusValue.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("complete", out completion) || completion.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Malformed result envelope.");
            var status = statusValue.GetString()!;
            var complete = completion.GetBoolean();
            var hasError = root.TryGetProperty("error", out var error);
            if (status is not ("succeeded" or "failed") || complete != (status == "succeeded") ||
                (status == "succeeded" && hasError) ||
                (status == "failed" && (!hasError || error.ValueKind != JsonValueKind.Object ||
                    !error.TryGetProperty("code", out code) || code.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(code.GetString()) ||
                    !error.TryGetProperty("message", out message) || message.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(message.GetString()))))
                throw new InvalidDataException("Contradictory or incomplete result envelope.");
            if ((process.ExitCode == 0) != (status == "succeeded"))
                throw new IOException($"Unexpected native exit {process.ExitCode}: {diagnostics}");
            return new(version, status, complete, root.Clone());
        }
        finally
        {
            // Cancellation must not leave the native child running.
            if (!process.HasExited)
                await TerminateAsync(() => process.Kill(entireProcessTree: true),
                    () => process.WaitForExitAsync(CancellationToken.None));
        }
    }
    // Termination is asynchronous on the host. Keep ownership until exit is acknowledged.
    internal static async Task TerminateAsync(Action terminate, Func<Task> waitForExit)
    {
        terminate();
        await waitForExit();
    }

    internal static ProcessStartInfo CreateStartInfo(string executable) =>
        new(executable) {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };

    internal static async Task DrainAsync(List<Task> pending, CancellationToken cancellation)
    {
        // Observe failed readers promptly: otherwise a full pipe can block child exit.
        // Count cannot be negative. A property pattern also avoids treating
        // this property as a LINQ Count() call during mutation instrumentation.
        while (pending is { Count: > 0 })
        {
            // Observe cancellation even if an awaited task completes inline.
            cancellation.ThrowIfCancellationRequested();
            var completed = await Task.WhenAny(pending);
            await completed;
            pending.Remove(completed);
        }
    }

    internal static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken cancellation)
    {
        // Count wire bytes, not UTF-16 characters: multibyte text must obey
        // the same 32 MiB bound. Decode only after the bounded stream ends.
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (bytes.Length + read > 32 * 1024 * 1024)
                throw new InvalidDataException("Native output exceeded 32 MiB.");
            bytes.Write(buffer, 0, read);
        }
        try { return new System.Text.UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length); }
        catch (System.Text.DecoderFallbackException error) { throw new InvalidDataException("Native output is not valid UTF-8.", error); }
    }
}

