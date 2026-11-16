using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface IWindowsEventLogProvider
{
    Task<IReadOnlyList<WindowsEventEntry>> GetRecentEventsAsync(
        int maximumEvents,
        CancellationToken cancellationToken = default);
}
