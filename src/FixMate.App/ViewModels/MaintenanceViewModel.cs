using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class MaintenanceViewModel : ViewModelBase
{
    private static readonly TimeSpan CleanupAge = TimeSpan.FromDays(7);
    private readonly IMaintenanceProvider _provider;
    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private string _startupListStatus = "Loading startup items...";
    private string _applicationsStatus = "Installed applications have not been loaded.";
    private string _applicationSearchText = string.Empty;
    private string _storageFolderPath = string.Empty;
    private string _storageSearchText = string.Empty;
    private string _storageStatus = "Choose a folder to analyze its storage usage.";
    private string _volumeStatus = "Loading local storage volumes...";
    private CancellationTokenSource? _storageScanCancellation;
    private bool _isStorageScanRunning;
    private string _cleanupStatus = "Scan to review current-user temporary files older than seven days.";

    public MaintenanceViewModel(IMaintenanceProvider provider)
    {
        _provider = provider;
        RefreshStartupCommand = new AsyncRelayCommand(RefreshStartupAsync, HandleStartupError);
        ToggleStartupCommand = new AsyncRelayCommand(ToggleStartupAsync, HandleStartupError);
        RefreshApplicationsCommand = new AsyncRelayCommand(RefreshApplicationsAsync, HandleApplicationsError);
        OpenInstalledAppsCommand = new RelayCommand(OpenInstalledApps);
        RefreshStorageVolumesCommand = new AsyncRelayCommand(RefreshStorageVolumesAsync, HandleStorageError);
        ChooseStorageFolderCommand = new RelayCommand(ChooseStorageFolder);
        AnalyzeStorageCommand = new AsyncRelayCommand(AnalyzeStorageAsync, HandleStorageError);
        CancelStorageScanCommand = new RelayCommand(CancelStorageScan, () => IsStorageScanRunning);
        ScanTemporaryFilesCommand = new AsyncRelayCommand(ScanTemporaryFilesAsync, HandleCleanupError);
        CleanTemporaryFilesCommand = new AsyncRelayCommand(CleanTemporaryFilesAsync, HandleCleanupError);
    }

    public ObservableCollection<StartupEntry> StartupEntries { get; } = [];
    public ObservableCollection<InstalledApplication> InstalledApplications { get; } = [];
    public ObservableCollection<StorageVolume> StorageVolumes { get; } = [];
    public ObservableCollection<StorageFile> LargestStorageFiles { get; } = [];
    public ICommand RefreshStartupCommand { get; }
    public ICommand ToggleStartupCommand { get; }
    public ICommand RefreshApplicationsCommand { get; }
    public ICommand OpenInstalledAppsCommand { get; }
    public ICommand RefreshStorageVolumesCommand { get; }
    public ICommand ChooseStorageFolderCommand { get; }
    public ICommand AnalyzeStorageCommand { get; }
    public RelayCommand CancelStorageScanCommand { get; }
    public ICommand ScanTemporaryFilesCommand { get; }
    public ICommand CleanTemporaryFilesCommand { get; }
    public bool CanStartOperation => !_isBusy;
    public bool IsStorageScanRunning => _isStorageScanRunning;
    public string StorageFolderPath => _storageFolderPath;
    public string StorageStatus => _storageStatus;
    public string VolumeStatus => _volumeStatus;
    public string ApplicationsStatus => _applicationsStatus;
    public IEnumerable<StorageFile> FilteredStorageFiles
    {
        get
        {
            var search = _storageSearchText.Trim();
            return string.IsNullOrEmpty(search)
                ? LargestStorageFiles
                : LargestStorageFiles.Where(file =>
                    file.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    file.FullPath.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    public string StorageSearchText
    {
        get => _storageSearchText;
        set
        {
            if (_storageSearchText == value)
            {
                return;
            }

            _storageSearchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredStorageFiles));
        }
    }
    public IEnumerable<InstalledApplication> FilteredApplications
    {
        get
        {
            var search = _applicationSearchText.Trim();
            return string.IsNullOrEmpty(search)
                ? InstalledApplications
                : InstalledApplications.Where(application =>
                    application.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    application.Publisher.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    application.Version.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    public string ApplicationSearchText
    {
        get => _applicationSearchText;
        set
        {
            if (_applicationSearchText == value)
            {
                return;
            }

            _applicationSearchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredApplications));
        }
    }

    public async Task LoadInitialDataAsync()
    {
        try
        {
            await RefreshStartupAsync();
        }
        catch (Exception exception)
        {
            HandleStartupError(exception);
        }

        try
        {
            await RefreshApplicationsAsync();
        }
        catch (Exception exception)
        {
            HandleApplicationsError(exception);
        }

        try
        {
            await RefreshStorageVolumesAsync();
        }
        catch (Exception exception)
        {
            HandleStorageError(exception);
        }
    }

    public string OperationStatusText => _isBusy ? "Working..." : string.Empty;
    public string StartupListStatus => _startupListStatus;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public string CleanupStatus
    {
        get => _cleanupStatus;
        private set
        {
            _cleanupStatus = value;
            OnPropertyChanged();
        }
    }

    private async Task RefreshStartupAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var entries = await _provider.GetStartupEntriesAsync();
            StartupEntries.Clear();
            foreach (var entry in entries)
            {
                StartupEntries.Add(entry);
            }

            _startupListStatus = entries.Count == 0
                ? "No current-user startup items were found."
                : $"Found {entries.Count:N0} current-user startup item(s).";
            OnPropertyChanged(nameof(StartupListStatus));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshApplicationsAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var applications = await _provider.GetInstalledApplicationsAsync();
            InstalledApplications.Clear();
            foreach (var application in applications)
            {
                InstalledApplications.Add(application);
            }

            _applicationsStatus = applications.Count == 0
                ? "No installed applications were found in the standard Windows uninstall registry locations."
                : $"Found {applications.Count:N0} installed application(s). Search filters this list only.";
            OnPropertyChanged(nameof(ApplicationsStatus));
            OnPropertyChanged(nameof(FilteredApplications));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshStorageVolumesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var volumes = await _provider.GetStorageVolumesAsync();
            StorageVolumes.Clear();
            foreach (var volume in volumes)
            {
                StorageVolumes.Add(volume);
            }

            _volumeStatus = volumes.Count == 0
                ? "No ready fixed or removable storage volumes were found."
                : $"Found {volumes.Count:N0} ready local storage volume(s).";
            OnPropertyChanged(nameof(VolumeStatus));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ChooseStorageFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for read-only storage analysis",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _storageFolderPath = dialog.FolderName;
        OnPropertyChanged(nameof(StorageFolderPath));
        LargestStorageFiles.Clear();
        _storageStatus = "Folder selected. Start analysis to list its 50 largest files.";
        OnPropertyChanged(nameof(StorageStatus));
        OnPropertyChanged(nameof(FilteredStorageFiles));
    }

    private async Task AnalyzeStorageAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_storageFolderPath))
        {
            throw new InvalidOperationException("Choose a folder before starting storage analysis.");
        }

        var cancellation = new CancellationTokenSource();
        _storageScanCancellation = cancellation;
        SetBusy(true);
        _isStorageScanRunning = true;
        OnPropertyChanged(nameof(IsStorageScanRunning));
        CancelStorageScanCommand.NotifyCanExecuteChanged();
        _storageStatus = $"Scanning {_storageFolderPath}...";
        OnPropertyChanged(nameof(StorageStatus));
        LargestStorageFiles.Clear();
        OnPropertyChanged(nameof(FilteredStorageFiles));
        ErrorMessage = string.Empty;
        try
        {
            var result = await _provider.AnalyzeStorageFolderAsync(_storageFolderPath, cancellation.Token);
            LargestStorageFiles.Clear();
            foreach (var file in result.LargestFiles)
            {
                LargestStorageFiles.Add(file);
            }

            _storageStatus = $"Scanned {result.FilesScanned:N0} file(s), totaling {FormatBytes(result.TotalBytes)}. Showing the largest {result.LargestFiles.Count:N0} file(s)." +
                (result.SkippedItems > 0 ? $" {result.SkippedItems:N0} item(s) were inaccessible or skipped." : string.Empty);
            OnPropertyChanged(nameof(StorageStatus));
            OnPropertyChanged(nameof(FilteredStorageFiles));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _storageStatus = "Storage analysis was canceled. No files were changed.";
            OnPropertyChanged(nameof(StorageStatus));
        }
        finally
        {
            _storageScanCancellation = null;
            cancellation.Dispose();
            _isStorageScanRunning = false;
            OnPropertyChanged(nameof(IsStorageScanRunning));
            CancelStorageScanCommand.NotifyCanExecuteChanged();
            SetBusy(false);
        }
    }

    private void CancelStorageScan()
    {
        _storageScanCancellation?.Cancel();
    }

    private void OpenInstalledApps()
    {
        try
        {
            if (Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true }) is null)
            {
                throw new InvalidOperationException("Windows did not start the Installed Apps settings page.");
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Windows Installed Apps settings could not be opened: {exception.Message}";
        }
    }

    private async Task ToggleStartupAsync(object? parameter)
    {
        if (_isBusy)
        {
            return;
        }

        if (parameter is not StartupEntry entry)
        {
            throw new ArgumentException("A startup entry is required.", nameof(parameter));
        }

        var action = entry.IsEnabled ? "disable" : "re-enable";
        var answer = MessageBox.Show(
            $"{action} '{entry.Name}' for this Windows user?\n\nSource: {entry.Source}\nCommand or file: {entry.Command}\n\nThis changes only the current user's startup configuration. Continue?",
            "Confirm startup change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            await _provider.SetStartupEntryEnabledAsync(entry, !entry.IsEnabled);
            var entries = await _provider.GetStartupEntriesAsync();
            StartupEntries.Clear();
            foreach (var updatedEntry in entries)
            {
                StartupEntries.Add(updatedEntry);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ScanTemporaryFilesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var report = await _provider.ScanTemporaryFilesAsync();
            CleanupStatus = FormatReport(
                report,
                $"Review only; eligible files are older than {CleanupAge.TotalDays:N0} days.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CleanTemporaryFilesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var scan = await _provider.ScanTemporaryFilesAsync();
            if (scan.CandidateCount == 0)
            {
                CleanupStatus = FormatReport(scan, "No eligible files were found; nothing was deleted.");
                return;
            }

            var answer = MessageBox.Show(
                $"Delete {scan.CandidateCount:N0} current-user temporary file(s), approximately {FormatBytes(scan.CandidateBytes)}, last modified more than seven days ago?\n\nOnly files under your local temporary folder are considered. Files in use and reparse points are skipped; folders, the Recycle Bin, and Windows system folders are not cleaned.\n\nContinue?",
                "Confirm temporary file cleanup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            var report = await _provider.CleanTemporaryFilesAsync();
            CleanupStatus = FormatReport(
                report,
                $"Cleanup finished. Deleted {report.DeletedCount:N0} file(s), freeing approximately {FormatBytes(report.DeletedBytes)}.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void HandleStartupError(Exception exception)
    {
        ErrorMessage = $"Startup management could not complete: {exception.Message}";
        _startupListStatus = "Startup items could not be loaded or updated.";
        OnPropertyChanged(nameof(StartupListStatus));
    }

    private void HandleCleanupError(Exception exception)
    {
        ErrorMessage = $"Temporary file operation could not complete: {exception.Message}";
    }

    private void HandleApplicationsError(Exception exception)
    {
        _applicationsStatus = "Installed applications could not be loaded.";
        OnPropertyChanged(nameof(ApplicationsStatus));
        ErrorMessage = $"Installed application inventory could not be loaded: {exception.Message}";
    }

    private void HandleStorageError(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        _storageStatus = "Storage analysis could not complete.";
        OnPropertyChanged(nameof(StorageStatus));
        ErrorMessage = $"Storage analysis could not complete: {exception.Message}";
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanStartOperation));
        OnPropertyChanged(nameof(OperationStatusText));
    }

    private static string FormatReport(TemporaryFilesReport report, string headline)
    {
        var skipped = report.SkippedCount > 0
            ? $" {report.SkippedCount:N0} item(s) could not be inspected or removed."
            : string.Empty;
        return $"{headline} Found {report.CandidateCount:N0} eligible file(s), approximately {FormatBytes(report.CandidateBytes)}.{skipped}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = Math.Max(0, bytes);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:N1} {units[unit]}";
    }
}
