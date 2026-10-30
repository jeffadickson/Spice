using System.Diagnostics;
using System.Text;
namespace Simulator.Benchmarks;
internal sealed record ProcessResult(string Output, string Error, int ExitCode, double ElapsedMs, long? PeakWorkingSetBytes, string MemoryStatus);
internal static class ProcessRunner
{
    // Timing covers spawn, stdin, child execution, output drain and exit. Parent
    // numerical comparisons and artifact writes occur outside this interval.
    internal static async Task<ProcessResult> Run(string executable, string[] arguments, string? input, string directory, TimeSpan timeout, CancellationToken cancellation = default)
    {
        if (!Directory.Exists(directory) || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentException("Invalid process directory or budget.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = input is not null, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = directory };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["RUST_LOG"] = "off";
        start.Environment["LC_ALL"] = "C";
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        cancellation.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new IOException("Process did not start.");
        var output = Read(process.StandardOutput.BaseStream, limit.Token);
        var errors = Read(process.StandardError.BaseStream, limit.Token);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), limit.Token);
                process.StandardInput.Close();
            }
            var pending = new List<Task> { output, errors, process.WaitForExitAsync(limit.Token) };
            while (pending.Count > 0)
            {
                limit.Token.ThrowIfCancellationRequested();
                var done = await Task.WhenAny(pending);
                await done;
                pending.Remove(done);
            }
            if (limit.IsCancellationRequested) throw new TimeoutException("Process budget expired.");
            clock.Stop();
            long? memory = null;
            var status = "unavailable: OS peak statistic absent";
            try
            {
                var peak = process.PeakWorkingSet64;
                if (peak > 0) { memory = peak; status = "OS peak working set (shared and private resident pages)"; }
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            { status = "unavailable: " + error.GetType().Name; }
            return new(await output, await errors, process.ExitCode, clock.Elapsed.TotalMilliseconds, memory, status);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            cancellation.ThrowIfCancellationRequested();
            throw new TimeoutException("Process/pipe budget expired.");
        }
        finally
        {
            limit.Cancel();
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            try { await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException) { }
        }
    }

    private static async Task<string> Read(Stream stream, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            token.ThrowIfCancellationRequested();
            if (bytes.Length + count > 8_388_608) throw new InvalidDataException("Process output exceeds 8 MiB per pipe.");
            bytes.Write(buffer, 0, count);
        }
        try { return new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid process UTF-8.", error); }
    }
}
