using System.Diagnostics;
using System.Globalization;
using Spice.Ir;

namespace Spice.Sdk;

public sealed record ExternalProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public interface IXyceProcessRunner
{
    Task<ExternalProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}

public sealed class DefaultProcessRunner : IXyceProcessRunner
{
    public async Task<ExternalProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch '{executablePath}'.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort process cleanup on cancellation.
            }
            throw;
        }

        return new ExternalProcessResult(
            proc.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }
}

/// <summary>
/// GPL-isolated external-process Xyce Strangler Fig backend (ARCH-COMPAT-002).
/// Zero GPL or Trilinos code is linked into Spice V2; Xyce is invoked strictly
/// as an external executable over temporary SPICE decks and CSV outputs.
/// </summary>
public sealed class XyceBackend : ISimulationBackend
{
    private readonly string _xyceExecutablePath;
    private readonly IXyceProcessRunner _runner;
    private readonly DeviceRegistry _registry;

    public XyceBackend(
        string xyceExecutablePath = "Xyce",
        IXyceProcessRunner? runner = null,
        DeviceRegistry? registry = null)
    {
        if (string.IsNullOrWhiteSpace(xyceExecutablePath))
        {
            throw new ArgumentException("Xyce executable path must be non-empty.", nameof(xyceExecutablePath));
        }
        _xyceExecutablePath = xyceExecutablePath;
        _runner = runner ?? new DefaultProcessRunner();
        _registry = registry ?? DeviceRegistry.Default;
    }

    public string BackendId => "xyce_external_process";

    public async Task<ResultManifestIr> RunAsync(
        SimulationRequestIr request,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = _registry.ValidateCircuit(request.Circuit);
        if (validationErrors.Count > 0)
        {
            return new ResultManifestIr(
                request.CaseId,
                BackendId,
                RunTerminalStatus.Failed,
                Complete: false,
                Signals: [],
                ErrorCode: "invalid_circuit",
                ErrorMessage: string.Join("; ", validationErrors.Select(e => $"{e.Code}: {e.Message}")));
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"spice-v2-xyce-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var deckPath = Path.Combine(workDir, "circuit.cir");
            var deckText = SpiceNetlistEmitter.EmitNetlist(request, includeXycePrintCsv: true, registry: _registry);
            await File.WriteAllTextAsync(deckPath, deckText, cancellationToken).ConfigureAwait(false);

            ExternalProcessResult procResult;
            try
            {
                procResult = await _runner.RunAsync(
                    _xyceExecutablePath,
                    [deckPath],
                    workDir,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ResultManifestIr(
                    request.CaseId,
                    BackendId,
                    RunTerminalStatus.Cancelled,
                    Complete: false,
                    Signals: [],
                    ErrorCode: "cancelled",
                    ErrorMessage: "Xyce simulation was cancelled.");
            }
            catch (Exception ex)
            {
                return new ResultManifestIr(
                    request.CaseId,
                    BackendId,
                    RunTerminalStatus.Failed,
                    Complete: false,
                    Signals: [],
                    ErrorCode: "process_launch_failed",
                    ErrorMessage: ex.Message);
            }

            if (procResult.ExitCode != 0)
            {
                return new ResultManifestIr(
                    request.CaseId,
                    BackendId,
                    RunTerminalStatus.Failed,
                    Complete: false,
                    Signals: [],
                    ErrorCode: "xyce_exit_nonzero",
                    ErrorMessage: $"Xyce exited with code {procResult.ExitCode}: {procResult.StandardError.Trim()}");
            }

            var csvPath = ResolveXyceCsvPath(workDir, deckPath, request.Analysis);
            if (csvPath is null || !File.Exists(csvPath))
            {
                return new ResultManifestIr(
                    request.CaseId,
                    BackendId,
                    RunTerminalStatus.Failed,
                    Complete: false,
                    Signals: [],
                    ErrorCode: "missing_output",
                    ErrorMessage: "Xyce completed without emitting expected CSV output.");
            }

            var csvText = await File.ReadAllTextAsync(csvPath, cancellationToken).ConfigureAwait(false);
            return ParseXyceCsv(request, BackendId, csvText);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch
            {
                // Best-effort temp directory cleanup.
            }
        }
    }

    public static ResultManifestIr ParseXyceCsv(
        SimulationRequestIr request,
        string backendId,
        string csvText)
    {
        var lines = csvText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            return new ResultManifestIr(
                request.CaseId,
                backendId,
                RunTerminalStatus.Failed,
                Complete: false,
                Signals: [],
                ErrorCode: "empty_csv",
                ErrorMessage: "Xyce CSV output had fewer than 2 rows.");
        }

        var headers = lines[0].Split(',', StringSplitOptions.TrimEntries);
        var rows = new List<double[]>(lines.Length - 1);
        for (int r = 1; r < lines.Length; ++r)
        {
            var cols = lines[r].Split(',', StringSplitOptions.TrimEntries);
            if (cols.Length != headers.Length)
            {
                return new ResultManifestIr(
                    request.CaseId,
                    backendId,
                    RunTerminalStatus.Failed,
                    Complete: false,
                    Signals: [],
                    ErrorCode: "malformed_csv",
                    ErrorMessage: $"CSV row {r + 1} column count mismatch ({cols.Length} vs {headers.Length}).");
            }

            var parsed = new double[cols.Length];
            for (int c = 0; c < cols.Length; ++c)
            {
                if (!double.TryParse(cols[c], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[c]))
                {
                    return new ResultManifestIr(
                        request.CaseId,
                        backendId,
                        RunTerminalStatus.Failed,
                        Complete: false,
                        Signals: [],
                        ErrorCode: "malformed_csv_number",
                        ErrorMessage: $"Invalid floating-point token '{cols[c]}' at row {r + 1}, col {c + 1}.");
                }
            }
            rows.Add(parsed);
        }

        var axisValues = rows.Select(r => r[0]).ToList();

        if (request.Analysis == AnalysisKind.Ac)
        {
            var signals = new List<SignalDescriptor>();
            var complexMap = new Dictionary<string, IReadOnlyList<ComplexSample>>(StringComparer.OrdinalIgnoreCase);

            for (int c = 1; c + 1 < headers.Length; c += 2)
            {
                var sigId = NormalizeAcSignalId(headers[c]);
                var samples = new List<ComplexSample>(rows.Count);
                for (int r = 0; r < rows.Count; ++r)
                {
                    samples.Add(new ComplexSample(rows[r][c], rows[r][c + 1]));
                }
                var isCurrent = sigId.StartsWith("i(", StringComparison.OrdinalIgnoreCase);
                signals.Add(new SignalDescriptor(sigId, isCurrent ? "current" : "voltage", isCurrent ? "A" : "V", IsComplex: true));
                complexMap[sigId] = samples;
            }

            return new ResultManifestIr(
                request.CaseId,
                backendId,
                RunTerminalStatus.Succeeded,
                Complete: true,
                Signals: signals,
                ComplexData: new ComplexSeriesIr("frequency", "Hz", axisValues, complexMap));
        }
        else
        {
            var signals = new List<SignalDescriptor>();
            var realMap = new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);
            for (int c = 1; c < headers.Length; ++c)
            {
                var sigId = headers[c].ToLowerInvariant();
                var vals = rows.Select(r => r[c]).ToList();
                var isCurrent = sigId.StartsWith("i(", StringComparison.OrdinalIgnoreCase);
                signals.Add(new SignalDescriptor(sigId, isCurrent ? "current" : "voltage", isCurrent ? "A" : "V", IsComplex: false));
                realMap[sigId] = vals;
            }

            var (axisName, axisUnit) = request.Analysis switch
            {
                AnalysisKind.Transient => ("time", "s"),
                AnalysisKind.DcSweep => ("sweep", "V"),
                _ => ("index", "")
            };

            return new ResultManifestIr(
                request.CaseId,
                backendId,
                RunTerminalStatus.Succeeded,
                Complete: true,
                Signals: signals,
                RealData: new RealSeriesIr(axisName, axisUnit, axisValues, realMap));
        }
    }

    private static string? ResolveXyceCsvPath(string workDir, string deckPath, AnalysisKind analysis)
    {
        var ext = analysis switch
        {
            AnalysisKind.Ac => ".FD.csv",
            AnalysisKind.Transient => ".csv",
            _ => ".csv"
        };
        var preferred = deckPath + ext;
        if (File.Exists(preferred)) return preferred;
        return Directory.EnumerateFiles(workDir, "*.csv").FirstOrDefault();
    }

    private static string NormalizeAcSignalId(string header)
    {
        // Xyce emits "Re(V(OUT))" or "VR(OUT)" -> normalize to "v(out)".
        var trimmed = header.Trim();
        if (trimmed.StartsWith("VR(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(')'))
        {
            return $"v({trimmed[3..^1].ToLowerInvariant()})";
        }
        if (trimmed.StartsWith("Re(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(')'))
        {
            return trimmed[3..^1].ToLowerInvariant();
        }
        return trimmed.ToLowerInvariant();
    }

    public void Dispose()
    {
        // Stateless process launcher; nothing unmanaged to dispose.
    }
}
