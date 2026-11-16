using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface IProcessInformationProvider
{
    Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(CancellationToken cancellationToken = default);
}
