using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Spice.Ir;

namespace Spice.Sdk;

internal static partial class NativeCapi
{
    private const string LibraryName = "spice_capi";
    internal const uint AbiMajor = 2;
    internal const uint AbiMinor = 1;

    static NativeCapi()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeCapi).Assembly, ResolveNativeLibrary);
    }

    private static nint ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return nint.Zero;
        }
        var explicitPath = Environment.GetEnvironmentVariable("SPICE_V2_CAPI_LIB");
        if (!string.IsNullOrWhiteSpace(explicitPath) && NativeLibrary.TryLoad(explicitPath, out var explicitHandle))
        {
            return explicitHandle;
        }
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var defaultHandle))
        {
            return defaultHandle;
        }
        return nint.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EngineOptions
    {
        public uint StructSize;
        public uint AbiMajor;
        public uint AbiMinor;
        public uint EnableAudits;
        public ulong MaxUnknowns;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Capabilities
    {
        public uint StructSize;
        public uint AbiMajor;
        public uint AbiMinor;
        public uint KluAvailable;
        public uint OsdiAvailable;
        public uint AuditsAvailable;
        public ulong DefaultMaxUnknowns;
    }

    [LibraryImport(LibraryName, EntryPoint = "spice_engine_create")]
    internal static partial int EngineCreate(in EngineOptions options, out nint engine);

    [LibraryImport(LibraryName, EntryPoint = "spice_engine_destroy")]
    internal static partial void EngineDestroy(nint engine);

    [LibraryImport(LibraryName, EntryPoint = "spice_engine_capabilities")]
    internal static partial int EngineCapabilities(nint engine, ref Capabilities caps);

    [LibraryImport(LibraryName, EntryPoint = "spice_engine_last_error")]
    internal static unsafe partial int EngineLastError(nint engine, byte* buffer, nuint bufferBytes, out nuint requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CircuitCreate(string name, out nint circuit);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_parse_spice", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CircuitParseSpice(nint engine, string spiceNetlist, string? baseDir, out nint circuit);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_destroy")]
    internal static partial void CircuitDestroy(nint circuit);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_add_resistor", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddResistor(nint circuit, string id, string nodeP, string nodeN, double ohms);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_add_capacitor", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddCapacitor(
        nint circuit, string id, string nodeP, string nodeN, double farads, int hasIc, double icVolts);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_add_inductor", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddInductor(
        nint circuit, string id, string nodeP, string nodeN, double henries, int hasIc, double icAmps);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_add_voltage_source", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddVoltageSource(
        nint circuit, string id, string nodeP, string nodeN, double dcVolts, double acReal, double acImag);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_add_current_source", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddCurrentSource(
        nint circuit, string id, string nodeP, string nodeN, double dcAmps, double acReal, double acImag);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_add_vccs", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddVccs(
        nint circuit, string id, string nodeP, string nodeN, string ctrlP, string ctrlN, double siemens);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_add_vcvs", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddVcvs(
        nint circuit, string id, string nodeP, string nodeN, string ctrlP, string ctrlN, double gain);

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_add_diode", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddDiode(
        nint circuit, string id, string nodeP, string nodeN, double isAmps, double nEmission, double cjoFarads);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_op")]
    internal static partial int RunOp(nint engine, nint circuit, int backend, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_dc_sweep", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RunDcSweep(
        nint engine, nint circuit, int backend, string sourceId, double startVal, double stopVal, double stepVal, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_ac")]
    internal static unsafe partial int RunAc(
        nint engine, nint circuit, int backend, double* frequenciesHz, nuint frequencyCount, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_transient")]
    internal static partial int RunTransient(
        nint engine, nint circuit, int backend, double stepS, double stopS, int method, int startup, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_noise", StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial int RunNoise(
        nint engine, nint circuit, int backend,
        string outP, string outN, string inputSourceId,
        double* frequenciesHz, nuint frequencyCount, double temperatureK, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_hb")]
    internal static partial int RunHb(
        nint engine, nint circuit, int backend, double fundamentalHz, nuint harmonics, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_pss")]
    internal static partial int RunPss(
        nint engine, nint circuit, int backend, double fundamentalHz, nuint stepsPerPeriod, int method, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_sp")]
    internal static unsafe partial int RunSp(
        nint engine, nint circuit, int backend,
        nint* portPNodesUtf8, nint* portNNodesUtf8, nuint portCount,
        double z0Ohms, double* frequenciesHz, nuint frequencyCount, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_destroy")]
    internal static partial void ResultDestroy(nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_is_complex")]
    internal static partial int ResultIsComplex(nint result, out int isComplex);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_signal_count")]
    internal static partial int ResultSignalCount(nint result, out nuint count);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_signal_name")]
    internal static partial int ResultSignalName(nint result, nuint index, out nint utf8Name);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_sample_count")]
    internal static partial int ResultSampleCount(nint result, out nuint count);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_axis_data")]
    internal static unsafe partial int ResultAxisData(nint result, out double* axis);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_real_data")]
    internal static unsafe partial int ResultRealData(nint result, out double* values);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_complex_data")]
    internal static unsafe partial int ResultComplexData(nint result, out double* interleavedReIm);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_audit_passed")]
    internal static partial int ResultAuditPassed(nint result, out int passed);
}

internal sealed class NativeEngineHandle : SafeHandle
{
    internal NativeEngineHandle(nint handle) : base(nint.Zero, ownsHandle: true) => SetHandle(handle);
    public override bool IsInvalid => handle == nint.Zero;
    protected override bool ReleaseHandle()
    {
        NativeCapi.EngineDestroy(handle);
        return true;
    }
}

/// <summary>
/// Options for running SPICE3f5 / BSIM3 / BSIM4 / BJT / JFET / OSDI decks in-process
/// via NativeCppBackend.RunSpiceDeckAsync.
/// </summary>
public sealed record SpiceDeckExecutionOptions(
    string? BaseDirectory = null,
    string? SweepSourceId = null,
    string? NoiseOutPositive = null,
    string? NoiseOutNegative = "0",
    string? NoiseInputSourceId = null,
    IReadOnlyList<(string NodeP, string NodeN)>? SParamPorts = null,
    IReadOnlyDictionary<string, double>? NumericOptions = null);

/// <summary>
/// In-process C++20 simulation backend using flat LibraryImport P/Invoke, SafeHandle
/// ownership, and contiguous native result buffers (ARCH-API-002, ARCH-API-003).
/// </summary>
public sealed class NativeCppBackend : ISimulationBackend
{
    private readonly NativeEngineHandle _engine;

    public NativeCppBackend(bool enableAudits = true, ulong maxUnknowns = 0)
    {
        var opts = new NativeCapi.EngineOptions
        {
            StructSize = (uint)Marshal.SizeOf<NativeCapi.EngineOptions>(),
            AbiMajor = NativeCapi.AbiMajor,
            AbiMinor = NativeCapi.AbiMinor,
            EnableAudits = enableAudits ? 1u : 0u,
            MaxUnknowns = maxUnknowns
        };
        var status = NativeCapi.EngineCreate(in opts, out var raw);
        if (status != 0 || raw == nint.Zero)
        {
            throw new InvalidOperationException($"spice_engine_create failed with status {status}.");
        }
        _engine = new NativeEngineHandle(raw);
    }

    public string BackendId => "native_cpp_v2";

    public (uint AbiMajor, uint AbiMinor, bool KluAvailable, bool OsdiAvailable, bool AuditsAvailable) QueryCapabilities()
    {
        var caps = new NativeCapi.Capabilities
        {
            StructSize = (uint)Marshal.SizeOf<NativeCapi.Capabilities>()
        };
        var status = NativeCapi.EngineCapabilities(_engine.DangerousGetHandle(), ref caps);
        if (status != 0)
        {
            throw new InvalidOperationException($"spice_engine_capabilities failed with status {status}.");
        }
        return (caps.AbiMajor, caps.AbiMinor, caps.KluAvailable != 0, caps.OsdiAvailable != 0, caps.AuditsAvailable != 0);
    }

    public string QueryLastError()
    {
        unsafe
        {
            _ = NativeCapi.EngineLastError(_engine.DangerousGetHandle(), null, 0, out var required);
            if (required <= 1) return string.Empty;
            var buf = new byte[(int)required];
            fixed (byte* ptr = buf)
            {
                _ = NativeCapi.EngineLastError(_engine.DangerousGetHandle(), ptr, required, out _);
            }
            return Encoding.UTF8.GetString(buf, 0, Math.Max(0, buf.Length - 1));
        }
    }

    /// <summary>
    /// Parse and execute any canonical SPICE3f5 / BSIM3 / BSIM4 / BJT / JFET / OSDI deck
    /// completely in-process through libspice_capi (zero external processes).
    /// </summary>
    public Task<ResultManifestIr> RunSpiceDeckAsync(
        string caseId,
        string spiceNetlist,
        AnalysisKind analysis,
        SpiceDeckExecutionOptions? deckOptions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parseStatus = NativeCapi.CircuitParseSpice(
            _engine.DangerousGetHandle(),
            spiceNetlist,
            deckOptions?.BaseDirectory,
            out var circuitHandle);

        if (parseStatus != 0 || circuitHandle == nint.Zero)
        {
            var err = QueryLastError();
            return Task.FromResult(new ResultManifestIr(
                caseId, BackendId, RunTerminalStatus.Failed, false, [],
                "parse_failed", string.IsNullOrEmpty(err) ? $"spice_circuit_parse_spice failed ({parseStatus})." : err));
        }

        try
        {
            var opts = deckOptions?.NumericOptions ?? new Dictionary<string, double>();
            return Task.FromResult(ExecuteCircuitHandle(
                caseId,
                circuitHandle,
                analysis,
                opts,
                deckOptions?.SweepSourceId,
                deckOptions?.NoiseOutPositive,
                deckOptions?.NoiseOutNegative ?? "0",
                deckOptions?.NoiseInputSourceId,
                deckOptions?.SParamPorts));
        }
        finally
        {
            NativeCapi.CircuitDestroy(circuitHandle);
        }
    }

    public Task<ResultManifestIr> RunAsync(
        SimulationRequestIr request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validationErrors = DeviceRegistry.Default.ValidateCircuit(request.Circuit);
        if (validationErrors.Count > 0)
        {
            return Task.FromResult(new ResultManifestIr(
                request.CaseId, BackendId, RunTerminalStatus.Failed, false, [],
                "invalid_circuit", string.Join("; ", validationErrors.Select(e => $"{e.Code}: {e.Message}"))));
        }

        var createStatus = NativeCapi.CircuitCreate(request.Circuit.Name, out var circuitHandle);
        if (createStatus != 0 || circuitHandle == nint.Zero)
        {
            return Task.FromResult(new ResultManifestIr(
                request.CaseId, BackendId, RunTerminalStatus.Failed, false, [],
                "create_failed", $"spice_circuit_create failed ({createStatus})."));
        }

        try
        {
            foreach (var dev in request.Circuit.Devices)
            {
                int addStatus = dev.KindId switch
                {
                    "spice.resistor" => NativeCapi.AddResistor(
                        circuitHandle, dev.Id, dev.PinConnections["p"], dev.PinConnections["n"], dev.Parameters["ohms"]),
                    "spice.capacitor" => NativeCapi.AddCapacitor(
                        circuitHandle, dev.Id, dev.PinConnections["p"], dev.PinConnections["n"], dev.Parameters["farads"],
                        dev.Parameters.ContainsKey("ic_volts") ? 1 : 0,
                        dev.Parameters.GetValueOrDefault("ic_volts", 0.0)),
                    "spice.inductor" => NativeCapi.AddInductor(
                        circuitHandle, dev.Id, dev.PinConnections["p"], dev.PinConnections["n"], dev.Parameters["henries"],
                        dev.Parameters.ContainsKey("ic_amps") ? 1 : 0,
                        dev.Parameters.GetValueOrDefault("ic_amps", 0.0)),
                    "spice.vsrc" => AddSource(circuitHandle, isVoltage: true, dev, dev.PinConnections["p"], dev.PinConnections["n"]),
                    "spice.isrc" => AddSource(circuitHandle, isVoltage: false, dev, dev.PinConnections["p"], dev.PinConnections["n"]),
                    "spice.vccs" => NativeCapi.AddVccs(
                        circuitHandle, dev.Id, dev.PinConnections["p"], dev.PinConnections["n"],
                        dev.PinConnections["cp"], dev.PinConnections["cn"], dev.Parameters["siemens"]),
                    "spice.vcvs" => NativeCapi.AddVcvs(
                        circuitHandle, dev.Id, dev.PinConnections["p"], dev.PinConnections["n"],
                        dev.PinConnections["cp"], dev.PinConnections["cn"], dev.Parameters["gain"]),
                    "spice.diode" => NativeCapi.AddDiode(
                        circuitHandle, dev.Id, dev.PinConnections["p"], dev.PinConnections["n"],
                        dev.Parameters.GetValueOrDefault("is_amps", 1e-14),
                        dev.Parameters.GetValueOrDefault("n_emission", 1.0),
                        dev.Parameters.GetValueOrDefault("cjo_farads", 0.0)),
                    _ => 4
                };
                if (addStatus != 0)
                {
                    return Task.FromResult(new ResultManifestIr(
                        request.CaseId, BackendId, RunTerminalStatus.Failed, false, [],
                        "device_add_failed", $"Failed adding {dev.Id} ({dev.KindId}), status {addStatus}."));
                }
            }

            var opts = request.Options ?? new Dictionary<string, double>();
            return Task.FromResult(ExecuteCircuitHandle(
                request.CaseId,
                circuitHandle,
                request.Analysis,
                opts,
                request.SweepSourceId,
                noiseOutP: null,
                noiseOutN: "0",
                noiseInSrc: request.SweepSourceId,
                spPorts: null));
        }
        finally
        {
            NativeCapi.CircuitDestroy(circuitHandle);
        }
    }

    private ResultManifestIr ExecuteCircuitHandle(
        string caseId,
        nint circuitHandle,
        AnalysisKind analysis,
        IReadOnlyDictionary<string, double> opts,
        string? sweepSourceId,
        string? noiseOutP,
        string noiseOutN,
        string? noiseInSrc,
        IReadOnlyList<(string NodeP, string NodeN)>? spPorts)
    {
        nint resultHandle;
        int runStatus;
        var eng = _engine.DangerousGetHandle();

        switch (analysis)
        {
            case AnalysisKind.OperatingPoint:
                runStatus = NativeCapi.RunOp(eng, circuitHandle, backend: 0, out resultHandle);
                break;

            case AnalysisKind.DcSweep:
            {
                var src = sweepSourceId ?? "V1";
                var start = opts.GetValueOrDefault("start", 0.0);
                var stop = opts.GetValueOrDefault("stop", 1.0);
                var step = opts.GetValueOrDefault("step", 0.1);
                runStatus = NativeCapi.RunDcSweep(eng, circuitHandle, backend: 0, src, start, stop, step, out resultHandle);
                break;
            }

            case AnalysisKind.Ac:
            {
                var freqs = BuildFrequencies(opts);
                unsafe
                {
                    fixed (double* fPtr = freqs)
                    {
                        runStatus = NativeCapi.RunAc(eng, circuitHandle, backend: 0, fPtr, (nuint)freqs.Length, out resultHandle);
                    }
                }
                break;
            }

            case AnalysisKind.Transient:
            {
                var step = opts.GetValueOrDefault("step_s", 1e-6);
                var stop = opts.GetValueOrDefault("stop_s", 1e-3);
                var methodVal = opts.GetValueOrDefault("method", 1.0);
                var method = methodVal < 0.5 ? 0 : (methodVal > 1.5 ? 2 : 1); // 0=BE, 1=Trap, 2=BDF2
                var startup = opts.GetValueOrDefault("uic", 0.0) > 0.5 ? 1 : 0;
                runStatus = NativeCapi.RunTransient(eng, circuitHandle, backend: 0, step, stop, method, startup, out resultHandle);
                break;
            }

            case AnalysisKind.Noise:
            {
                var freqs = BuildFrequencies(opts);
                var outP = noiseOutP ?? "out";
                var inSrc = noiseInSrc ?? sweepSourceId ?? "V1";
                var tempK = opts.GetValueOrDefault("temperature_k", 300.15);
                unsafe
                {
                    fixed (double* fPtr = freqs)
                    {
                        runStatus = NativeCapi.RunNoise(
                            eng, circuitHandle, backend: 0, outP, noiseOutN, inSrc,
                            fPtr, (nuint)freqs.Length, tempK, out resultHandle);
                    }
                }
                break;
            }

            case AnalysisKind.HarmonicBalance:
            {
                var f0 = opts.GetValueOrDefault("fundamental_hz", 1.0e6);
                var harmonics = (nuint)Math.Max(1, Math.Round(opts.GetValueOrDefault("harmonics", 7.0)));
                runStatus = NativeCapi.RunHb(eng, circuitHandle, backend: 0, f0, harmonics, out resultHandle);
                break;
            }

            case AnalysisKind.PeriodicSteadyState:
            {
                var f0 = opts.GetValueOrDefault("fundamental_hz", 1.0e6);
                var steps = (nuint)Math.Max(8, Math.Round(opts.GetValueOrDefault("steps_per_period", 64.0)));
                var method = opts.GetValueOrDefault("method", 0.0) < 0.5 ? 0 : 1;
                runStatus = NativeCapi.RunPss(eng, circuitHandle, backend: 0, f0, steps, method, out resultHandle);
                break;
            }

            case AnalysisKind.SParameters:
            {
                var ports = spPorts ?? [("p1", "0"), ("p2", "0")];
                var freqs = BuildFrequencies(opts);
                var z0 = opts.GetValueOrDefault("z0_ohms", 50.0);
                var pPtrs = new nint[ports.Count];
                var nPtrs = new nint[ports.Count];
                try
                {
                    for (int i = 0; i < ports.Count; ++i)
                    {
                        pPtrs[i] = Marshal.StringToCoTaskMemUTF8(ports[i].NodeP);
                        nPtrs[i] = Marshal.StringToCoTaskMemUTF8(ports[i].NodeN);
                    }
                    unsafe
                    {
                        fixed (nint* pp = pPtrs)
                        fixed (nint* pn = nPtrs)
                        fixed (double* fPtr = freqs)
                        {
                            runStatus = NativeCapi.RunSp(
                                eng, circuitHandle, backend: 0,
                                pp, pn, (nuint)ports.Count, z0,
                                fPtr, (nuint)freqs.Length, out resultHandle);
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < ports.Count; ++i)
                    {
                        if (pPtrs[i] != nint.Zero) Marshal.FreeCoTaskMem(pPtrs[i]);
                        if (nPtrs[i] != nint.Zero) Marshal.FreeCoTaskMem(nPtrs[i]);
                    }
                }
                break;
            }

            default:
                return new ResultManifestIr(
                    caseId, BackendId, RunTerminalStatus.Failed, false, [],
                    "unsupported", $"NativeCppBackend does not support {analysis}.");
        }

        if (runStatus != 0 || resultHandle == nint.Zero)
        {
            var err = QueryLastError();
            return new ResultManifestIr(
                caseId, BackendId, RunTerminalStatus.Failed, false, [],
                "solve_failed",
                string.IsNullOrEmpty(err) ? $"Native C++20 solve failed with status {runStatus}." : err);
        }

        try
        {
            return ReadResultManifest(caseId, analysis, resultHandle);
        }
        finally
        {
            NativeCapi.ResultDestroy(resultHandle);
        }
    }

    private static int AddSource(nint circuitHandle, bool isVoltage, DeviceInstanceIr dev, string p, string n)
    {
        var dcKey = isVoltage ? "dc_volts" : "dc_amps";
        var dc = dev.Parameters.GetValueOrDefault(dcKey, 0.0);
        var acMag = dev.Parameters.GetValueOrDefault("ac_mag", 0.0);
        var acPhaseRad = dev.Parameters.GetValueOrDefault("ac_phase_deg", 0.0) * Math.PI / 180.0;
        var acRe = acMag * Math.Cos(acPhaseRad);
        var acIm = acMag * Math.Sin(acPhaseRad);
        return isVoltage
            ? NativeCapi.AddVoltageSource(circuitHandle, dev.Id, p, n, dc, acRe, acIm)
            : NativeCapi.AddCurrentSource(circuitHandle, dev.Id, p, n, dc, acRe, acIm);
    }

    private static double[] BuildFrequencies(IReadOnlyDictionary<string, double> opts)
    {
        var fStart = opts.GetValueOrDefault("f_start_hz", 10.0);
        var fStop = opts.GetValueOrDefault("f_stop_hz", 1.0e6);
        var ptsPerDec = (int)Math.Max(1, Math.Round(opts.GetValueOrDefault("points_per_decade", 10.0)));
        if (fStop <= fStart) return [fStart];

        var decades = Math.Log10(fStop / fStart);
        var total = Math.Max(1, (int)Math.Ceiling(decades * ptsPerDec));
        var list = new List<double>(total + 1);
        for (int i = 0; i <= total; ++i)
        {
            var f = fStart * Math.Pow(10.0, (double)i / ptsPerDec);
            if (f > fStop * (1.0 + 1e-12)) break;
            list.Add(Math.Min(f, fStop));
        }
        return list.ToArray();
    }

    private ResultManifestIr ReadResultManifest(string caseId, AnalysisKind analysis, nint resultHandle)
    {
        _ = NativeCapi.ResultSignalCount(resultHandle, out var sigCountN);
        _ = NativeCapi.ResultSampleCount(resultHandle, out var sampleCountN);
        _ = NativeCapi.ResultIsComplex(resultHandle, out var isComplexInt);
        int sigCount = checked((int)sigCountN);
        int sampleCount = checked((int)sampleCountN);
        bool isComplex = isComplexInt != 0;

        var signals = new List<SignalDescriptor>(sigCount);
        for (int i = 0; i < sigCount; ++i)
        {
            _ = NativeCapi.ResultSignalName(resultHandle, (nuint)i, out var namePtr);
            var name = Marshal.PtrToStringUTF8(namePtr) ?? $"sig_{i}";
            var isCurrent = name.StartsWith("i(", StringComparison.OrdinalIgnoreCase);
            var isSParam = name.StartsWith("S", StringComparison.Ordinal) && name.Length >= 3 && char.IsDigit(name[1]);
            var quantity = isSParam ? "s_param" : (isCurrent ? "current" : "voltage");
            var unit = isSParam ? "" : (isCurrent ? "A" : "V");
            signals.Add(new SignalDescriptor(name, quantity, unit, isComplex));
        }

        double[] axis = new double[sampleCount];
        unsafe
        {
            if (NativeCapi.ResultAxisData(resultHandle, out var axisPtr) == 0 && axisPtr != null)
            {
                new ReadOnlySpan<double>(axisPtr, sampleCount).CopyTo(axis);
            }
        }

        if (isComplex)
        {
            var map = new Dictionary<string, IReadOnlyList<ComplexSample>>(StringComparer.OrdinalIgnoreCase);
            unsafe
            {
                if (NativeCapi.ResultComplexData(resultHandle, out var cPtr) == 0 && cPtr != null)
                {
                    var span = new ReadOnlySpan<double>(cPtr, 2 * sampleCount * sigCount);
                    for (int s = 0; s < sigCount; ++s)
                    {
                        var vals = new ComplexSample[sampleCount];
                        for (int k = 0; k < sampleCount; ++k)
                        {
                            int baseIdx = 2 * (k * sigCount + s);
                            vals[k] = new ComplexSample(span[baseIdx], span[baseIdx + 1]);
                        }
                        map[signals[s].Id] = vals;
                    }
                }
            }
            return new ResultManifestIr(
                caseId, BackendId, RunTerminalStatus.Succeeded, Complete: true, Signals: signals,
                ComplexData: new ComplexSeriesIr("frequency", "Hz", axis, map));
        }
        else
        {
            var map = new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);
            unsafe
            {
                if (NativeCapi.ResultRealData(resultHandle, out var rPtr) == 0 && rPtr != null)
                {
                    var span = new ReadOnlySpan<double>(rPtr, sampleCount * sigCount);
                    for (int s = 0; s < sigCount; ++s)
                    {
                        var vals = new double[sampleCount];
                        for (int k = 0; k < sampleCount; ++k)
                        {
                            vals[k] = span[k * sigCount + s];
                        }
                        map[signals[s].Id] = vals;
                    }
                }
            }
            var (axisName, axisUnit) = analysis switch
            {
                AnalysisKind.Transient or AnalysisKind.PeriodicSteadyState => ("time", "s"),
                AnalysisKind.DcSweep => ("sweep", "V"),
                AnalysisKind.Noise => ("frequency", "Hz"),
                _ => ("index", "")
            };
            return new ResultManifestIr(
                caseId, BackendId, RunTerminalStatus.Succeeded, Complete: true, Signals: signals,
                RealData: new RealSeriesIr(axisName, axisUnit, axis, map));
        }
    }

    public void Dispose() => _engine.Dispose();
}
