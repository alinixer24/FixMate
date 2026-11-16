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

    public SystemInformationViewModel(ISystemInformationProvider provider)
    {
        _provider = provider;
        RefreshCommand = new AsyncRelayCommand(LoadAsync, HandleLoadError);
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

    public ICommand RefreshCommand { get; }

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

    private void HandleLoadError(Exception exception)
    {
        ErrorMessage = $"System information could not be loaded: {exception.Message}";
    }
}
