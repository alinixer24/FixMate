using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface IMaintenanceReminderStore
{
    Task<IReadOnlyList<MaintenanceReminder>> GetRemindersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaintenanceReminder>> GetDueRemindersAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task SaveReminderAsync(MaintenanceReminder reminder, CancellationToken cancellationToken = default);
    Task CompleteReminderAsync(MaintenanceReminder reminder, DateTimeOffset completedAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReminderHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);
}
