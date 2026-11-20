using Spice.Ir;

namespace Spice.Sdk;

/// <summary>
/// Strangler Fig backend contract shared by external GPL-isolated XyceBackend
/// (Day-1 execution and differential oracle) and in-process C++20 NativeCppBackend
/// (ARCH-COMPAT-002, ARCH-API-002).
/// </summary>
public interface ISimulationBackend : IDisposable
{
    string BackendId { get; }

    Task<ResultManifestIr> RunAsync(
        SimulationRequestIr request,
        CancellationToken cancellationToken = default);
}
