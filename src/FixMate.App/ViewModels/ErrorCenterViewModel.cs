using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class ErrorCenterViewModel : ViewModelBase
{
    private const int MaximumEvents = 200;
    private readonly IWindowsEventLogProvider _provider;
    private bool _isBusy;
    private string _status = "Windows events have not been loaded.";
    private string _errorMessage = string.Empty;
    private string _searchText = string.Empty;
    private string _sourceFilter = string.Empty;
    private string _eventIdFilter = string.Empty;
    private string _selectedDateRange = "7 days";
    private WindowsEventEntry? _selectedEvent;

    public ErrorCenterViewModel(IWindowsEventLogProvider provider)
    {
        _provider = provider;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleError);
        OpenEventViewerCommand = new RelayCommand(OpenEventViewer);
    }

    public ObservableCollection<WindowsEventEntry> Events { get; } = [];
    public IReadOnlyList<string> DateRangeOptions { get; } = ["24 hours", "7 days", "30 days", "All loaded"];
    public ICommand RefreshCommand { get; }
    public ICommand OpenEventViewerCommand { get; }
    public bool CanRefresh => !_isBusy;
    public string OperationStatusText => _isBusy ? "Reading Windows event logs..." : string.Empty;

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
            OnPropertyChanged(nameof(FilteredEvents));
        }
    }

    public string SourceFilter
    {
        get => _sourceFilter;
        set
        {
            if (_sourceFilter == value)
            {
                return;
            }

            _sourceFilter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredEvents));
        }
    }

    public string EventIdFilter
    {
        get => _eventIdFilter;
        set
        {
            if (_eventIdFilter == value)
            {
                return;
            }

            _eventIdFilter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredEvents));
        }
    }

    public string SelectedDateRange
    {
        get => _selectedDateRange;
        set
        {
            if (_selectedDateRange == value)
            {
                return;
            }

            _selectedDateRange = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredEvents));
        }
    }

    public WindowsEventEntry? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (ReferenceEquals(_selectedEvent, value))
            {
                return;
            }

            _selectedEvent = value;
            OnPropertyChanged();
        }
    }

    public IEnumerable<WindowsEventEntry> FilteredEvents
    {
        get
        {
            var search = SearchText.Trim();
            var source = SourceFilter.Trim();
            var eventId = EventIdFilter.Trim();
            var cutoff = GetCutoff();

            return Events.Where(entry =>
                (cutoff is null || entry.TimeCreated >= cutoff) &&
                (source.Length == 0 || entry.Provider.Contains(source, StringComparison.CurrentCultureIgnoreCase) ||
                    entry.LogName.Contains(source, StringComparison.CurrentCultureIgnoreCase)) &&
                (eventId.Length == 0 || entry.EventId.ToString().Contains(eventId, StringComparison.OrdinalIgnoreCase)) &&
                (search.Length == 0 ||
                    entry.Message.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    entry.Provider.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    entry.LogName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                    entry.EventId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public async Task LoadInitialDataAsync()
    {
        try
        {
            await RefreshEventsAsync();
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
    }

    private async Task RefreshAsync() => await RefreshEventsAsync();

    private async Task RefreshEventsAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        Status = "Loading the most recent Application and System errors and warnings...";
        try
        {
            var events = await _provider.GetRecentEventsAsync(MaximumEvents);
            SelectedEvent = null;
            Events.Clear();
            foreach (var entry in events)
            {
                Events.Add(entry);
            }

            OnPropertyChanged(nameof(FilteredEvents));
            Status = $"Loaded {events.Count:N0} recent error or warning event(s) from the Application and System logs. This is a read-only view.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void HandleError(Exception exception)
    {
        ErrorMessage = $"Windows events could not be read: {exception.Message}";
        Status = "The event-log read did not complete.";
    }

    private void OpenEventViewer()
    {
        try
        {
            if (Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true }) is null)
            {
                throw new InvalidOperationException("Windows did not start Event Viewer.");
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Event Viewer could not be opened: {exception.Message}";
        }
    }

    private DateTimeOffset? GetCutoff() => SelectedDateRange switch
    {
        "24 hours" => DateTimeOffset.Now.AddHours(-24),
        "7 days" => DateTimeOffset.Now.AddDays(-7),
        "30 days" => DateTimeOffset.Now.AddDays(-30),
        "All loaded" => null,
        _ => throw new InvalidOperationException($"Unsupported event date range: {SelectedDateRange}")
    };

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(OperationStatusText));
    }
}
