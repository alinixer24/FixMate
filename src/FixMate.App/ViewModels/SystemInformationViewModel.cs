using System.Collections.ObjectModel;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class SystemInformationViewModel : ViewModelBase
{
    private readonly ISystemInformationProvider _provider;
    private OperatingSystemInfo _operatingSystem = new();
    private DateTimeOffset? _lastScanned;
    private bool _isLoading;
    private string _errorMessage = string.Empty;
    private string _reportStatus = string.Empty;

    public SystemInformationViewModel(ISystemInformationProvider provider)
    {
        _provider = provider;
        RefreshCommand = new AsyncRelayCommand(LoadAsync, HandleLoadError);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, HandleReportError);
    }

    public ObservableCollection<HardwareComponent> Components { get; } = [];

    public OperatingSystemInfo OperatingSystem
    {
        get => _operatingSystem;
        private set
        {
            _operatingSystem = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset? LastScanned
    {
        get => _lastScanned;
        private set
        {
            _lastScanned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastScannedText));
        }
    }

    public string LastScannedText => LastScanned is null
        ? "Not scanned yet"
        : $"Last scanned {LastScanned.Value.ToLocalTime():g}";

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScanStatusText));
        }
    }

    public string ScanStatusText => IsLoading ? "Scanning hardware..." : string.Empty;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public string ReportStatus
    {
        get => _reportStatus;
        private set
        {
            _reportStatus = value;
            OnPropertyChanged();
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportReportCommand { get; }

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var snapshot = await _provider.GetSystemInformationAsync();
            OperatingSystem = snapshot.OperatingSystem;
            Components.Clear();
            foreach (var component in snapshot.Components)
            {
                Components.Add(component);
            }

            LastScanned = snapshot.CollectedAt;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExportReportAsync()
    {
        if (LastScanned is null)
        {
            throw new InvalidOperationException("Refresh system information before creating a report.");
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save FixMate system report",
            FileName = $"FixMate-system-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExt = ".txt",
            Filter = "Text report (*.txt)|*.txt"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var report = new System.Text.StringBuilder()
            .AppendLine("FixMate system report")
            .AppendLine($"Generated: {DateTimeOffset.Now:g}")
            .AppendLine($"Snapshot collected: {LastScanned.Value:g}")
            .AppendLine()
            .AppendLine("WINDOWS")
            .AppendLine($"Name: {OperatingSystem.Name}")
            .AppendLine($"Machine: {OperatingSystem.MachineName}")
            .AppendLine($"Version: {OperatingSystem.Version}")
            .AppendLine($"Architecture: {OperatingSystem.Architecture}")
            .AppendLine($"Status: {OperatingSystem.Status}")
            .AppendLine($"Details: {OperatingSystem.Details}")
            .AppendLine()
            .AppendLine("HARDWARE")
            .ToString();

        var reportBuilder = new System.Text.StringBuilder(report);
        foreach (var component in Components)
        {
            reportBuilder.AppendLine($"[{component.Category}] {component.Name}");
            reportBuilder.AppendLine($"Model: {component.Model}");
            reportBuilder.AppendLine($"Manufacturer: {component.Manufacturer}");
            reportBuilder.AppendLine($"Status: {component.Status}");
            if (!string.IsNullOrWhiteSpace(component.Details))
            {
                reportBuilder.AppendLine($"Details: {component.Details}");
            }

            reportBuilder.AppendLine();
        }

        await System.IO.File.WriteAllTextAsync(dialog.FileName, reportBuilder.ToString());
        ReportStatus = $"Report saved to {dialog.FileName}";
        ErrorMessage = string.Empty;
    }

    private void HandleLoadError(Exception exception)
    {
        ErrorMessage = $"System information could not be loaded: {exception.Message}";
    }

    private void HandleReportError(Exception exception)
    {
        ErrorMessage = $"System report could not be saved: {exception.Message}";
    }
}
