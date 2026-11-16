using System.Globalization;
using Microsoft.Win32;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsMaintenanceProvider : IMaintenanceProvider
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run\FixMateDisabled";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string DisabledStartupSuffix = ".fixmate-disabled";
    private static readonly TimeSpan CleanupAge = TimeSpan.FromDays(7);

    public Task<IReadOnlyList<StartupEntry>> GetStartupEntriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<StartupEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<StartupEntry>();
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            using var disabledRunKey = Registry.CurrentUser.OpenSubKey(DisabledRunKeyPath);
            if (runKey is not null)
            {
                foreach (var name in runKey.GetValueNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entries.Add(new StartupEntry(
                        $"registry:{name}",
                        name,
                        Convert.ToString(runKey.GetValue(name)) ?? string.Empty,
                        "Current-user Run registry key",
                        true,
                        true));
                }
            }

            if (disabledRunKey is not null)
            {
                foreach (var name in disabledRunKey.GetValueNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entries.All(entry => !entry.Id.Equals($"registry:{name}", StringComparison.OrdinalIgnoreCase)))
                    {
                        entries.Add(new StartupEntry(
                            $"registry:{name}",
                            name,
                            Convert.ToString(disabledRunKey.GetValue(name)) ?? string.Empty,
                            "Current-user Run registry key",
                            false,
                            true));
                    }
                }
            }

            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var path in Directory.EnumerateFiles(startupFolder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var isDisabled = path.EndsWith(DisabledStartupSuffix, StringComparison.OrdinalIgnoreCase);
                    var displayPath = isDisabled ? path[..^DisabledStartupSuffix.Length] : path;
                    if (!isDisabled && !IsStartupFile(path))
                    {
                        continue;
                    }

                    entries.Add(new StartupEntry(
                        $"file:{path}",
                        Path.GetFileName(displayPath),
                        displayPath,
                        "Current-user Startup folder",
                        !isDisabled,
                        true));
                }
            }

            return entries.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<InstalledApplication>>(() =>
        {
            var applications = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var machineKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var userKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                AddInstalledApplications(machineKey, $"HKLM ({view})", applications, cancellationToken);
                AddInstalledApplications(userKey, $"HKCU ({view})", applications, cancellationToken);
            }

            return applications.Values
                .OrderBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<StorageVolume>> GetStorageVolumesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<StorageVolume>>(() =>
        {
            var volumes = new List<StorageVolume>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    continue;
                }

                volumes.Add(new StorageVolume(
                    drive.Name,
                    drive.RootDirectory.FullName,
                    drive.DriveFormat,
                    drive.TotalSize,
                    drive.AvailableFreeSpace));
            }

            return volumes.OrderBy(volume => volume.RootPath, StringComparer.OrdinalIgnoreCase).ToArray();
        }, cancellationToken);
    }

    public Task<StorageAnalysisResult> AnalyzeStorageFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return Task.Run(() => AnalyzeStorageFolder(folderPath, cancellationToken), cancellationToken);
    }

    public Task SetStartupEntryEnabledAsync(
        StartupEntry entry,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.CanChange)
            {
                throw new InvalidOperationException("This startup entry is read-only.");
            }

            if (entry.Id.StartsWith("registry:", StringComparison.Ordinal))
            {
                SetRegistryStartupEntry(entry.Id["registry:".Length..], enabled);
                return;
            }

            if (entry.Id.StartsWith("file:", StringComparison.Ordinal))
            {
                SetStartupFileEnabled(entry.Id["file:".Length..], enabled);
                return;
            }

            throw new ArgumentException("The startup entry source is not recognized.", nameof(entry));
        }, cancellationToken);
    }

    public Task<TemporaryFilesReport> ScanTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ScanTemporaryFiles(cancellationToken), cancellationToken);
    }

    public Task<TemporaryFilesReport> CleanTemporaryFilesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => CleanTemporaryFiles(cancellationToken), cancellationToken);
    }

    private static void AddInstalledApplications(
        RegistryKey hiveKey,
        string registryView,
        IDictionary<string, InstalledApplication> applications,
        CancellationToken cancellationToken)
    {
        using var uninstallKey = hiveKey.OpenSubKey(UninstallKeyPath);
        if (uninstallKey is null)
        {
            return;
        }

        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var applicationKey = uninstallKey.OpenSubKey(subKeyName);
            if (applicationKey is null ||
                Convert.ToString(applicationKey.GetValue("SystemComponent"), CultureInfo.InvariantCulture) == "1" ||
                Convert.ToString(applicationKey.GetValue("NoDisplay"), CultureInfo.InvariantCulture) == "1" ||
                !string.IsNullOrWhiteSpace(Convert.ToString(applicationKey.GetValue("ParentKeyName"), CultureInfo.InvariantCulture)))
            {
                continue;
            }

            var name = Convert.ToString(applicationKey.GetValue("DisplayName"), CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var version = Convert.ToString(applicationKey.GetValue("DisplayVersion"), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            var publisher = Convert.ToString(applicationKey.GetValue("Publisher"), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            var installDateText = Convert.ToString(applicationKey.GetValue("InstallDate"), CultureInfo.InvariantCulture);
            DateTime? installDate = DateTime.TryParseExact(
                installDateText,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate)
                ? parsedDate
                : null;
            var identity = $"{name}\0{version}\0{publisher}";
            applications.TryAdd(identity, new InstalledApplication(name, version, publisher, installDate, registryView));
        }
    }

    private static StorageAnalysisResult AnalyzeStorageFolder(string folderPath, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(folderPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The selected folder does not exist: {root}");
        }

        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The selected folder is a reparse point; storage analysis was stopped for safety.");
        }

        var directories = new Stack<string>();
        var largestFiles = new PriorityQueue<StorageFile, long>();
        directories.Push(root);
        var filesScanned = 0;
        var skippedItems = 0;
        long totalBytes = 0;

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            skippedItems++;
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            directories.Push(entry);
                            continue;
                        }

                        var info = new FileInfo(entry);
                        var file = new StorageFile(info.Name, info.FullName, info.Length, info.LastWriteTime);
                        filesScanned++;
                        totalBytes += file.SizeBytes;
                        if (largestFiles.Count < 50)
                        {
                            largestFiles.Enqueue(file, file.SizeBytes);
                        }
                        else if (largestFiles.TryPeek(out _, out var smallestSize) && file.SizeBytes > smallestSize)
                        {
                            largestFiles.Dequeue();
                            largestFiles.Enqueue(file, file.SizeBytes);
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        skippedItems++;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                skippedItems++;
            }
        }

        return new StorageAnalysisResult(
            root,
            filesScanned,
            totalBytes,
            skippedItems,
            largestFiles.UnorderedItems
                .Select(item => item.Element)
                .OrderByDescending(file => file.SizeBytes)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
    }

    private static void SetRegistryStartupEntry(string name, bool enabled)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        using var disabledKey = Registry.CurrentUser.OpenSubKey(DisabledRunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(DisabledRunKeyPath, writable: true);

        if (enabled)
        {
            if (disabledKey.GetValueNames().All(valueName => !valueName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("The disabled startup entry no longer exists.");
            }

            if (runKey.GetValueNames().Any(valueName => valueName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("An enabled startup entry with this name already exists.");
            }

            var value = disabledKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var kind = disabledKey.GetValueKind(name);
            runKey.SetValue(name, value!, kind);
            disabledKey.DeleteValue(name);
            return;
        }

        if (runKey.GetValueNames().All(valueName => !valueName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The enabled startup entry no longer exists.");
        }

        if (disabledKey.GetValueNames().Any(valueName => valueName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A disabled startup entry with this name already exists.");
        }

        var activeValue = runKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var activeKind = runKey.GetValueKind(name);
        disabledKey.SetValue(name, activeValue!, activeKind);
        try
        {
            runKey.DeleteValue(name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            disabledKey.DeleteValue(name, throwOnMissingValue: false);
            throw;
        }
    }

    private static void SetStartupFileEnabled(string path, bool enabled)
    {
        var disabledPath = path.EndsWith(DisabledStartupSuffix, StringComparison.OrdinalIgnoreCase)
            ? path
            : path + DisabledStartupSuffix;
        if (enabled)
        {
            if (!File.Exists(disabledPath))
            {
                throw new FileNotFoundException("The disabled startup file no longer exists.", disabledPath);
            }

            var targetPath = disabledPath[..^DisabledStartupSuffix.Length];
            if (File.Exists(targetPath))
            {
                throw new IOException("A startup file with the original name already exists.");
            }

            File.Move(disabledPath, targetPath);
        }
        else
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The startup file no longer exists.", path);
            }

            if (File.Exists(disabledPath))
            {
                throw new IOException("A disabled startup file with this name already exists.");
            }

            File.Move(path, disabledPath);
        }
    }

    private static TemporaryFilesReport ScanTemporaryFiles(CancellationToken cancellationToken)
    {
        var root = GetTemporaryRoot();
        if (!Directory.Exists(root))
        {
            return new TemporaryFilesReport(0, 0, 0);
        }

        var cutoff = DateTime.UtcNow - CleanupAge;
        var candidates = EnumerateOldTemporaryFiles(root, cutoff, cancellationToken);
        return new TemporaryFilesReport(candidates.Count, candidates.Bytes, candidates.Skipped);
    }

    private static TemporaryFilesReport CleanTemporaryFiles(CancellationToken cancellationToken)
    {
        var root = GetTemporaryRoot();
        if (!Directory.Exists(root))
        {
            return new TemporaryFilesReport(0, 0, 0);
        }

        var cutoff = DateTime.UtcNow - CleanupAge;
        var candidates = EnumerateOldTemporaryFiles(root, cutoff, cancellationToken);
        var deletedCount = 0;
        long deletedBytes = 0;
        var skipped = candidates.Skipped;
        foreach (var candidate in candidates.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(candidate.Path);
                if (info.LastWriteTimeUtc >= cutoff || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var length = info.Length;
                info.Delete();
                deletedCount++;
                deletedBytes += length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                skipped++;
            }
        }

        return new TemporaryFilesReport(candidates.Count, candidates.Bytes, skipped, deletedCount, deletedBytes);
    }

    private static (List<(string Path, long Bytes)> Files, long Bytes, int Skipped, int Count) EnumerateOldTemporaryFiles(
        string root,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The current user's temporary folder is a reparse point; cleanup was stopped for safety.");
        }

        var files = new List<(string Path, long Bytes)>();
        var directories = new Stack<string>();
        directories.Push(root);
        long totalBytes = 0;
        var skipped = 0;

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFileSystemEntries(directory).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                skipped++;
                continue;
            }

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        directories.Push(path);
                        continue;
                    }

                    var info = new FileInfo(path);
                    if (info.LastWriteTimeUtc < cutoff)
                    {
                        files.Add((path, info.Length));
                        totalBytes += info.Length;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    skipped++;
                }
            }
        }

        return (files, totalBytes, skipped, files.Count);
    }

    private static string GetTemporaryRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The current user's local application data folder is unavailable.");
        }

        return Path.GetFullPath(Path.Combine(localAppData, "Temp"));
    }

    private static bool IsStartupFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".url" or ".exe" or ".bat" or ".cmd" or ".ps1";
}
