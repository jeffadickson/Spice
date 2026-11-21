using System.Runtime.InteropServices;
using Spice.Ir;

namespace Spice.Sdk;

internal static partial class NativeCapi
{
    private const string LibraryName = "spice_capi";
    internal const uint AbiMajor = 2;
    internal const uint AbiMinor = 0;

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

    [LibraryImport(LibraryName, EntryPoint = "spice_circuit_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CircuitCreate(string name, out nint circuit);

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

    [LibraryImport(LibraryName, EntryPoint = "spice_run_op")]
    internal static partial int RunOp(nint engine, nint circuit, int backend, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_ac")]
    internal static unsafe partial int RunAc(
        nint engine, nint circuit, int backend, double* frequenciesHz, nuint frequencyCount, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_run_transient")]
    internal static partial int RunTransient(
        nint engine, nint circuit, int backend, double stepS, double stopS, int method, int startup, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_destroy")]
    internal static partial void ResultDestroy(nint result);

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
                var p = dev.PinConnections["p"];
                var n = dev.PinConnections["n"];
                int addStatus = dev.KindId switch
                {
                    "spice.resistor" => NativeCapi.AddResistor(circuitHandle, dev.Id, p, n, dev.Parameters["ohms"]),
                    "spice.capacitor" => NativeCapi.AddCapacitor(
                        circuitHandle, dev.Id, p, n, dev.Parameters["farads"],
                        dev.Parameters.ContainsKey("ic_volts") ? 1 : 0,
                        dev.Parameters.GetValueOrDefault("ic_volts", 0.0)),
                    "spice.inductor" => NativeCapi.AddInductor(
                        circuitHandle, dev.Id, p, n, dev.Parameters["henries"],
                        dev.Parameters.ContainsKey("ic_amps") ? 1 : 0,
                        dev.Parameters.GetValueOrDefault("ic_amps", 0.0)),
                    "spice.vsrc" => AddSource(circuitHandle, isVoltage: true, dev, p, n),
                    "spice.isrc" => AddSource(circuitHandle, isVoltage: false, dev, p, n),
                    "spice.vccs" => NativeCapi.AddVccs(
                        circuitHandle, dev.Id, p, n, dev.PinConnections["cp"], dev.PinConnections["cn"],
                        dev.Parameters["siemens"]),
                    "spice.vcvs" => NativeCapi.AddVcvs(
                        circuitHandle, dev.Id, p, n, dev.PinConnections["cp"], dev.PinConnections["cn"],
                        dev.Parameters["gain"]),
                    _ => 4
                };
                if (addStatus != 0)
                {
                    return Task.FromResult(new ResultManifestIr(
                        request.CaseId, BackendId, RunTerminalStatus.Failed, false, [],
                        "device_add_failed", $"Failed adding {dev.Id} ({dev.KindId}), status {addStatus}."));
                }
            }

            nint resultHandle;
            int runStatus;
            var opts = request.Options ?? new Dictionary<string, double>();

            switch (request.Analysis)
            {
                case AnalysisKind.OperatingPoint:
                    runStatus = NativeCapi.RunOp(_engine.DangerousGetHandle(), circuitHandle, backend: 0, out resultHandle);
                    break;
                case AnalysisKind.Ac:
                {
                    var freqs = BuildAcFrequencies(opts);
                    unsafe
                    {
                        fixed (double* fPtr = freqs)
                        {
                            runStatus = NativeCapi.RunAc(
                                _engine.DangerousGetHandle(), circuitHandle, backend: 0,
                                fPtr, (nuint)freqs.Length, out resultHandle);
                        }
                    }
                    break;
                }
                case AnalysisKind.Transient:
                {
                    var step = opts.GetValueOrDefault("step_s", 1e-6);
                    var stop = opts.GetValueOrDefault("stop_s", 1e-3);
                    var method = opts.GetValueOrDefault("method", 1.0) < 0.5 ? 0 : 1; // 0=BE, 1=Trap
                    var startup = opts.GetValueOrDefault("uic", 0.0) > 0.5 ? 1 : 0;
                    runStatus = NativeCapi.RunTransient(
                        _engine.DangerousGetHandle(), circuitHandle, backend: 0,
                        step, stop, method, startup, out resultHandle);
                    break;
                }
                default:
                    return Task.FromResult(new ResultManifestIr(
                        request.CaseId, BackendId, RunTerminalStatus.Failed, false, [],
                        "unsupported", $"NativeCppBackend does not yet expose {request.Analysis} via C ABI."));
            }

            if (runStatus != 0 || resultHandle == nint.Zero)
            {
                return Task.FromResult(new ResultManifestIr(
                    request.CaseId, BackendId, RunTerminalStatus.Failed, false, [],
                    "solve_failed", $"Native C++20 solve failed with status {runStatus}."));
            }

            try
            {
                return Task.FromResult(ReadResultManifest(request, resultHandle));
            }
            finally
            {
                NativeCapi.ResultDestroy(resultHandle);
            }
        }
        finally
        {
            NativeCapi.CircuitDestroy(circuitHandle);
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

    private static double[] BuildAcFrequencies(IReadOnlyDictionary<string, double> opts)
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

    private ResultManifestIr ReadResultManifest(SimulationRequestIr request, nint resultHandle)
    {
        _ = NativeCapi.ResultSignalCount(resultHandle, out var sigCountN);
        _ = NativeCapi.ResultSampleCount(resultHandle, out var sampleCountN);
        int sigCount = checked((int)sigCountN);
        int sampleCount = checked((int)sampleCountN);

        var signals = new List<SignalDescriptor>(sigCount);
        bool isComplex = request.Analysis == AnalysisKind.Ac;
        for (int i = 0; i < sigCount; ++i)
        {
            _ = NativeCapi.ResultSignalName(resultHandle, (nuint)i, out var namePtr);
            var name = Marshal.PtrToStringUTF8(namePtr) ?? $"sig_{i}";
            var isCurrent = name.StartsWith("i(", StringComparison.OrdinalIgnoreCase);
            signals.Add(new SignalDescriptor(name, isCurrent ? "current" : "voltage", isCurrent ? "A" : "V", isComplex));
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
                request.CaseId, BackendId, RunTerminalStatus.Succeeded, Complete: true, Signals: signals,
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
            var (axisName, axisUnit) = request.Analysis == AnalysisKind.Transient ? ("time", "s") : ("index", "");
            return new ResultManifestIr(
                request.CaseId, BackendId, RunTerminalStatus.Succeeded, Complete: true, Signals: signals,
                RealData: new RealSeriesIr(axisName, axisUnit, axis, map));
        }
    }

    public void Dispose() => _engine.Dispose();
}
