using System.Collections.ObjectModel;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class ProcessManagerViewModel : ViewModelBase
{
    private readonly IProcessInformationProvider _provider;
    private bool _isBusy;
    private string _status = "Running processes have not been loaded.";
    private string _errorMessage = string.Empty;
    private string _searchText = string.Empty;

    public ProcessManagerViewModel(IProcessInformationProvider provider)
    {
        _provider = provider;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleError);
    }

    public ObservableCollection<RunningProcessInfo> Processes { get; } = [];
    public ICommand RefreshCommand { get; }
    public bool CanRefresh => !_isBusy;
    public string OperationStatusText => _isBusy ? "Reading running processes..." : string.Empty;

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
            OnPropertyChanged(nameof(FilteredProcesses));
        }
    }

    public IEnumerable<RunningProcessInfo> FilteredProcesses
    {
        get
        {
            var search = SearchText.Trim();
            return string.IsNullOrEmpty(search)
                ? Processes
                : Processes.Where(process =>
                    process.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    process.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    process.WindowTitle.Contains(search, StringComparison.CurrentCultureIgnoreCase));
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
        Status = "Loading the current process snapshot...";
        try
        {
            var processes = await _provider.GetRunningProcessesAsync();
            Processes.Clear();
            foreach (var process in processes)
            {
                Processes.Add(process);
            }

            OnPropertyChanged(nameof(FilteredProcesses));
            Status = $"Loaded {processes.Count:N0} process(es), ordered by memory use. Protected processes may expose limited details; this inventory is read-only.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void HandleError(Exception exception)
    {
        ErrorMessage = $"Running processes could not be loaded: {exception.Message}";
        Status = "The process snapshot did not complete.";
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(OperationStatusText));
    }
}
