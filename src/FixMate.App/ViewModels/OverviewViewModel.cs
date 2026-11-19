using System.Windows.Input;
using FixMate.App.Navigation;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class OverviewViewModel : ViewModelBase
{
    private readonly IMaintenanceProvider _maintenanceProvider;
    private readonly ISystemInformationProvider _systemInformationProvider;
    private readonly NavigationService _navigationService;
    private readonly SystemInformationViewModel _systemInformation;
    private string _storageSummary = "Loading ready storage volumes...";
    private string _storageError = string.Empty;
    private string _storageUsageText = "Storage usage unavailable";
    private string _systemInformationError = string.Empty;
    private string _windowsVersion = "Detecting Windows version...";
    private string _processorModel = "Detecting processor...";
    private string _graphicsModel = "Graphics adapter unavailable until reported by Windows.";
    private string _installedMemory = "Detecting installed memory...";
    private double _storageUsedPercent;

    public OverviewViewModel(
        IMaintenanceProvider maintenanceProvider,
        ISystemInformationProvider systemInformationProvider,
        NavigationService navigationService,
        MonitoringViewModel monitoring,
        SystemInformationViewModel systemInformation,
        DiagnosticsViewModel diagnostics,
        MaintenanceViewModel maintenance,
        TroubleshootingViewModel troubleshooting)
    {
        _maintenanceProvider = maintenanceProvider;
        _systemInformationProvider = systemInformationProvider;
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

    public string Heading => $"Welcome to FixMate, {Environment.MachineName}";
    public string Introduction => "A clear, live view of this PC using information reported by Windows.";
    public string MachineName => Environment.MachineName;
    public string UptimeText
    {
        get
        {
            var uptime = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64));
            return uptime.TotalDays >= 1
                ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
                : $"{uptime.Hours}h {uptime.Minutes}m";
        }
    }

    public MonitoringViewModel Monitoring { get; }
    public ICommand ShowSystemInformationCommand { get; }
    public ICommand ShowMonitoringCommand { get; }
    public ICommand ShowDiagnosticsCommand { get; }
    public ICommand ShowMaintenanceCommand { get; }
    public ICommand ShowTroubleshootingCommand { get; }

    public string WindowsVersion { get => _windowsVersion; private set => SetProperty(ref _windowsVersion, value); }
    public string ProcessorModel { get => _processorModel; private set => SetProperty(ref _processorModel, value); }
    public string GraphicsModel { get => _graphicsModel; private set => SetProperty(ref _graphicsModel, value); }
    public string InstalledMemory { get => _installedMemory; private set => SetProperty(ref _installedMemory, value); }
    public string SystemInformationError { get => _systemInformationError; private set => SetProperty(ref _systemInformationError, value); }

    public string StorageSummary
    {
        get => _storageSummary;
        private set => SetProperty(ref _storageSummary, value);
    }

    public string StorageError
    {
        get => _storageError;
        private set => SetProperty(ref _storageError, value);
    }

    public string StorageUsageText
    {
        get => _storageUsageText;
        private set => SetProperty(ref _storageUsageText, value);
    }

    public double StorageUsedPercent
    {
        get => _storageUsedPercent;
        private set => SetProperty(ref _storageUsedPercent, value);
    }

    public async Task RefreshAsync()
    {
        await Task.WhenAll(LoadStorageSummaryAsync(), LoadSystemOverviewAsync());
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
                StorageUsageText = "Storage usage unavailable";
                StorageUsedPercent = 0;
                return;
            }

            var totalBytes = volumes.Sum(volume => volume.TotalBytes);
            var freeBytes = volumes.Sum(volume => volume.AvailableBytes);
            var usedBytes = Math.Max(0, totalBytes - freeBytes);
            StorageUsedPercent = totalBytes > 0 ? Math.Clamp(usedBytes * 100d / totalBytes, 0, 100) : 0;
            StorageUsageText = totalBytes > 0 ? $"{StorageUsedPercent:N0}% used" : "Storage usage unavailable";
            StorageSummary = $"{FormatBytes(freeBytes)} free of {FormatBytes(totalBytes)} across {volumes.Count:N0} ready volume(s)";
        }
        catch (Exception exception)
        {
            StorageSummary = "Storage capacity is unavailable.";
            StorageUsageText = "Storage usage unavailable";
            StorageUsedPercent = 0;
            StorageError = $"Storage information could not be loaded: {exception.Message}";
        }
    }

    private async Task LoadSystemOverviewAsync()
    {
        SystemInformationError = string.Empty;
        try
        {
            var snapshot = await _systemInformationProvider.GetSystemInformationAsync();
            WindowsVersion = snapshot.OperatingSystem.Status == DetectionStatus.Detected
                ? $"{snapshot.OperatingSystem.Name} · {snapshot.OperatingSystem.Architecture}"
                : snapshot.OperatingSystem.Details;
            ProcessorModel = FindModel(snapshot.Components, "CPU");
            GraphicsModel = FindModel(snapshot.Components, "GPU");
            InstalledMemory = FindModel(snapshot.Components, "RAM");
        }
        catch (Exception exception)
        {
            WindowsVersion = "Windows details unavailable.";
            ProcessorModel = "Processor details unavailable.";
            GraphicsModel = "Graphics adapter details unavailable.";
            InstalledMemory = "Installed memory details unavailable.";
            SystemInformationError = $"System details could not be loaded: {exception.Message}";
        }
    }

    private static string FindModel(IEnumerable<HardwareComponent> components, string category)
    {
        var component = components.FirstOrDefault(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
        if (component is null || component.Status != DetectionStatus.Detected)
        {
            return string.IsNullOrWhiteSpace(component?.Details) ? "Unavailable" : component.Details;
        }

        return string.Join(" ", new[] { component.Manufacturer, component.Model }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
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
