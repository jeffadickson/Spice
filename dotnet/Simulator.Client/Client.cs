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
        var manifest = Payload.Deserialize<ResultManifest>(WireFormat.Options) ?? throw new InvalidDataException("Missing manifest.");
        if (manifest.SchemaVersion != "1" || manifest.Status != "succeeded" || !manifest.Complete ||
            manifest.Signals is null || manifest.Samples is null || manifest.AcSamples is null ||
            manifest.Measurements is null || manifest.Assertions is null || manifest.Backend is null ||
            manifest.Samples.Any(s => s.Values is null || s.Values.Length != manifest.Signals.Length) ||
            manifest.AcSamples.Any(s => s.Values is null || s.Values.Length != manifest.Signals.Length))
            throw new InvalidDataException("Malformed or incomplete result manifest.");
        return manifest;
    }
    public bool Succeeded => Status == "succeeded" && Complete;
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
        RunJsonAsync(JsonSerializer.Serialize(request, WireFormat.Options), cancellation);
    /// <summary>Rust validates the experimental request schema. No shell is invoked.</summary>
    public async Task<SimulationResponse> RunJsonAsync(string requestJson, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (System.Text.Encoding.UTF8.GetByteCount(requestJson) > 1_048_576)
            throw new ArgumentException("Request exceeds 1 MiB.", nameof(requestJson));
        var start = new ProcessStartInfo(executable) {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new IOException("Could not start simulator.");
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput, cancellation);
            var errors = ReadBoundedAsync(process.StandardError, cancellation);
            await process.StandardInput.WriteAsync(requestJson.AsMemory(), cancellation);
            process.StandardInput.Close();
            var exit = process.WaitForExitAsync(cancellation);
            // Observe failed readers promptly: otherwise a full pipe can block child exit.
            var pending = new List<Task> { output, errors, exit };
            while (pending.Count != 0)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }
            var text = await output;
            var diagnostics = await errors;
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var version = root.GetProperty("schema_version").GetString()!;
            if (version != "1") throw new InvalidDataException("Unsupported result version.");
            var status = root.GetProperty("status").GetString()!;
            if (process.ExitCode != 0 && status != "failed")
                throw new IOException($"Unexpected native exit {process.ExitCode}: {diagnostics}");
            return new(version, status, root.GetProperty("complete").GetBoolean(), root.Clone());
        }
        finally
        {
            // Cancellation must not leave the native child running.
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellation)
    {
        var builder = new System.Text.StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellation)) != 0)
        {
            if (builder.Length + read > 32 * 1024 * 1024)
                throw new InvalidDataException("Native output exceeded 32 MiB.");
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
    }
}

