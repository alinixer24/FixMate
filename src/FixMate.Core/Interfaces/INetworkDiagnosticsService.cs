using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface INetworkDiagnosticsService
{
    Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default);
    Task<NetworkDiagnosticResult> PingAsync(string host, CancellationToken cancellationToken = default);
    Task<NetworkDiagnosticResult> TraceRouteAsync(string host, CancellationToken cancellationToken = default);
    Task<NetworkDiagnosticResult> LookupDnsAsync(string host, CancellationToken cancellationToken = default);
    Task<NetworkDiagnosticResult> TestConnectivityAsync(string host, CancellationToken cancellationToken = default);
}
