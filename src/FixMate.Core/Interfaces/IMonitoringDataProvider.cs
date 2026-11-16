using FixMate.Core.Results;

namespace FixMate.Core.Interfaces;

public interface IMonitoringDataProvider
{
    Task<MonitoringSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
