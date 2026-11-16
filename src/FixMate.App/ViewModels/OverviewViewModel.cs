using System.Windows.Input;
using FixMate.App.Navigation;
using FixMate.Core.Interfaces;

namespace FixMate.App.ViewModels;

public sealed class OverviewViewModel : ViewModelBase
{
    private readonly IMaintenanceProvider _maintenanceProvider;
    private readonly NavigationService _navigationService;
    private readonly SystemInformationViewModel _systemInformation;
    private string _storageSummary = "Loading ready storage volumes...";
    private string _storageError = string.Empty;
    private double _storageUsedPercent;

    public OverviewViewModel(
        IMaintenanceProvider maintenanceProvider,
        NavigationService navigationService,
        MonitoringViewModel monitoring,
        SystemInformationViewModel systemInformation,
        DiagnosticsViewModel diagnostics,
        MaintenanceViewModel maintenance,
        TroubleshootingViewModel troubleshooting)
    {
        _maintenanceProvider = maintenanceProvider;
        _navigationService = navigationService;
        _systemInformation = systemInformation;
        Monitoring = monitoring;

        ShowSystemInformationCommand = new RelayCommand(() =>
        {
            _navigationService.NavigateTo(systemInformation);
            _systemInformation.RefreshCommand.Execute(null);
        });
        ShowMonitoringCommand = new RelayCommand(() => _navigationService.NavigateTo(monitoring));
        ShowDiagnosticsCommand = new RelayCommand(() => _navigationService.NavigateTo(diagnostics));
        ShowMaintenanceCommand = new RelayCommand(() => _navigationService.NavigateTo(maintenance));
        ShowTroubleshootingCommand = new RelayCommand(() => _navigationService.NavigateTo(troubleshooting));
    }

    public string Heading => $"Welcome back, {Environment.MachineName}";
    public string Introduction => "A live snapshot of your PC using readings reported by Windows. Select a section to investigate further.";
    public MonitoringViewModel Monitoring { get; }
    public ICommand ShowSystemInformationCommand { get; }
    public ICommand ShowMonitoringCommand { get; }
    public ICommand ShowDiagnosticsCommand { get; }
    public ICommand ShowMaintenanceCommand { get; }
    public ICommand ShowTroubleshootingCommand { get; }

    public string StorageSummary
    {
        get => _storageSummary;
        private set
        {
            _storageSummary = value;
            OnPropertyChanged();
        }
    }

    public string StorageError
    {
        get => _storageError;
        private set
        {
            _storageError = value;
            OnPropertyChanged();
        }
    }

    public double StorageUsedPercent
    {
        get => _storageUsedPercent;
        private set
        {
            _storageUsedPercent = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadStorageSummaryAsync()
    {
        StorageError = string.Empty;
        try
        {
            var volumes = await _maintenanceProvider.GetStorageVolumesAsync();
            if (volumes.Count == 0)
            {
                StorageSummary = "No ready fixed or removable volumes were reported by Windows.";
                StorageUsedPercent = 0;
                return;
            }

            var totalBytes = volumes.Sum(volume => volume.TotalBytes);
            var freeBytes = volumes.Sum(volume => volume.AvailableBytes);
            var usedBytes = Math.Max(0, totalBytes - freeBytes);
            StorageUsedPercent = totalBytes > 0 ? Math.Clamp(usedBytes * 100d / totalBytes, 0, 100) : 0;
            StorageSummary = $"{FormatBytes(freeBytes)} free of {FormatBytes(totalBytes)} across {volumes.Count:N0} ready volume(s)";
        }
        catch (Exception exception)
        {
            StorageSummary = "Storage capacity is unavailable.";
            StorageError = $"Storage information could not be loaded: {exception.Message}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:N1} {units[unit]}";
    }
}
