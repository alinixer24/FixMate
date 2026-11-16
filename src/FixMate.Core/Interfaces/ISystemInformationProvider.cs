using FixMate.Core.Results;

namespace FixMate.Core.Interfaces;

public interface ISystemInformationProvider
{
    Task<SystemInformationSnapshot> GetSystemInformationAsync(CancellationToken cancellationToken = default);
}
