using FixMate.Core.Models;

namespace FixMate.Core.Interfaces;

public interface IMaintenanceProvider
{
    Task<IReadOnlyList<StartupEntry>> GetStartupEntriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StorageVolume>> GetStorageVolumesAsync(CancellationToken cancellationToken = default);
    Task<StorageAnalysisResult> AnalyzeStorageFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task SetStartupEntryEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken = default);
    Task<TemporaryFilesReport> ScanTemporaryFilesAsync(CancellationToken cancellationToken = default);
    Task<TemporaryFilesReport> CleanTemporaryFilesAsync(CancellationToken cancellationToken = default);
}
