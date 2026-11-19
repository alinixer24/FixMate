using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface INetworkHistoryStore
{
    Task<IReadOnlyList<NetworkDiagnosticResult>> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task SaveResultAsync(NetworkDiagnosticResult result, CancellationToken cancellationToken = default);
}
