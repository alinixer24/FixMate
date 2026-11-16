using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class DriverCenterViewModel : ViewModelBase
{
    private readonly IDriverInformationProvider _provider;
    private bool _isBusy;
    private string _status = "Driver inventory has not been loaded.";
    private string _errorMessage = string.Empty;
    private string _searchText = string.Empty;

    public DriverCenterViewModel(IDriverInformationProvider provider)
    {
        _provider = provider;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleError);
        OpenDeviceManagerCommand = new RelayCommand(OpenDeviceManager);
    }

    public ObservableCollection<InstalledDriverInfo> Drivers { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand OpenDeviceManagerCommand { get; }
    public bool CanRefresh => !_isBusy;
    public string OperationStatusText => _isBusy ? "Reading installed drivers..." : string.Empty;

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredDrivers));
        }
    }

    public IEnumerable<InstalledDriverInfo> FilteredDrivers
    {
        get
        {
            var search = SearchText.Trim();
            return string.IsNullOrEmpty(search)
                ? Drivers
                : Drivers.Where(driver =>
                    driver.DeviceName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    driver.Manufacturer.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    driver.DriverProvider.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    driver.DriverVersion.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    driver.DeviceClass.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    driver.InfName.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    private async Task RefreshAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        Status = "Loading signed Plug and Play driver records from Windows...";
        try
        {
            var drivers = await _provider.GetInstalledDriversAsync();
            Drivers.Clear();
            foreach (var driver in drivers)
            {
                Drivers.Add(driver);
            }

            OnPropertyChanged(nameof(FilteredDrivers));
            Status = $"Loaded {drivers.Count:N0} driver record(s) from Windows. This inventory is informational; it does not install, update, or remove drivers.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void HandleError(Exception exception)
    {
        ErrorMessage = $"Driver inventory could not be loaded: {exception.Message}";
        Status = "The Windows driver inventory read did not complete.";
    }

    private void OpenDeviceManager()
    {
        try
        {
            if (Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true }) is null)
            {
                throw new InvalidOperationException("Windows did not start Device Manager.");
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Device Manager could not be opened: {exception.Message}";
        }
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(OperationStatusText));
    }
}
