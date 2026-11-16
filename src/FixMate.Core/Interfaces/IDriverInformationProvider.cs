using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface IDriverInformationProvider
{
    Task<IReadOnlyList<InstalledDriverInfo>> GetInstalledDriversAsync(CancellationToken cancellationToken = default);
}
