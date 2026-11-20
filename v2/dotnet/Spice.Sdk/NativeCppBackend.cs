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

    [LibraryImport(LibraryName, EntryPoint = "spice_run_op")]
    internal static partial int RunOp(nint engine, nint circuit, int backend, out nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_destroy")]
    internal static partial void ResultDestroy(nint result);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_signal_count")]
    internal static partial int ResultSignalCount(nint result, out nuint count);

    [LibraryImport(LibraryName, EntryPoint = "spice_result_signal_name")]
    internal static partial int ResultSignalName(nint result, nuint index, out nint utf8Name);

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
/// In-process C++20 simulation backend using flat LibraryImport P/Invoke and SafeHandle
/// ownership with zero JSON subprocess serialization (ARCH-API-002, ARCH-API-003).
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

    public Task<ResultManifestIr> RunAsync(
        SimulationRequestIr request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Analysis != AnalysisKind.OperatingPoint)
        {
            return Task.FromResult(new ResultManifestIr(
                request.CaseId,
                BackendId,
                RunTerminalStatus.Failed,
                Complete: false,
                Signals: [],
                ErrorCode: "unsupported",
                ErrorMessage: $"NativeCppBackend managed bridge currently wires OperatingPoint; AC/TRAN marshaling follows in M1a/M2a integration."));
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
                    "spice.capacitor" => NativeCapi.AddCapacitor(circuitHandle, dev.Id, p, n, dev.Parameters["farads"], 0, 0.0),
                    "spice.inductor" => NativeCapi.AddInductor(circuitHandle, dev.Id, p, n, dev.Parameters["henries"], 0, 0.0),
                    "spice.vsrc" => NativeCapi.AddVoltageSource(circuitHandle, dev.Id, p, n, dev.Parameters["dc_volts"], 0.0, 0.0),
                    "spice.isrc" => NativeCapi.AddCurrentSource(circuitHandle, dev.Id, p, n, dev.Parameters["dc_amps"], 0.0, 0.0),
                    _ => 4
                };
                if (addStatus != 0)
                {
                    return Task.FromResult(new ResultManifestIr(
                        request.CaseId, BackendId, RunTerminalStatus.Failed, false, [],
                        "device_add_failed", $"Failed adding {dev.Id} ({dev.KindId}), status {addStatus}."));
                }
            }

            var runStatus = NativeCapi.RunOp(_engine.DangerousGetHandle(), circuitHandle, backend: 0, out var resultHandle);
            if (runStatus != 0 || resultHandle == nint.Zero)
            {
                return Task.FromResult(new ResultManifestIr(
                    request.CaseId, BackendId, RunTerminalStatus.Failed, false, [],
                    "solve_failed", $"spice_run_op failed with status {runStatus}."));
            }

            try
            {
                _ = NativeCapi.ResultSignalCount(resultHandle, out var sigCount);
                var signals = new List<SignalDescriptor>((int)sigCount);
                for (nuint i = 0; i < sigCount; ++i)
                {
                    _ = NativeCapi.ResultSignalName(resultHandle, i, out var namePtr);
                    var name = Marshal.PtrToStringUTF8(namePtr) ?? $"sig_{i}";
                    var isCurrent = name.StartsWith("i(", StringComparison.Ordinal);
                    signals.Add(new SignalDescriptor(name, isCurrent ? "current" : "voltage", isCurrent ? "A" : "V", false));
                }
                return Task.FromResult(new ResultManifestIr(
                    request.CaseId, BackendId, RunTerminalStatus.Succeeded, Complete: true, Signals: signals));
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

    public void Dispose() => _engine.Dispose();
}
