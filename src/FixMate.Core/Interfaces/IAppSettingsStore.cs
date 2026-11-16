using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
