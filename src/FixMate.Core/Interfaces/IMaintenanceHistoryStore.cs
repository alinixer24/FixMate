using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface IMaintenanceHistoryStore
{
    Task<IReadOnlyList<MaintenanceHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task RecordAsync(MaintenanceHistoryEntry entry, CancellationToken cancellationToken = default);
}
