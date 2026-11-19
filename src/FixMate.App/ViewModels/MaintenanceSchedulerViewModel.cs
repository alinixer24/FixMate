using System.Collections.ObjectModel;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class MaintenanceSchedulerViewModel : ViewModelBase
{
    private readonly IMaintenanceReminderStore _store;
    private string _newReminderName = string.Empty;
    private DateTime? _dueDate = DateTime.Today.AddDays(1);
    private string _dueTime = "09:00";
    private string _repeatInterval = "Never";
    private bool _notifyOnDue = true;
    private string _status = "Reminders are stored locally on this PC.";
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public MaintenanceSchedulerViewModel(IMaintenanceReminderStore store)
    {
        _store = store;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleError);
        AddReminderCommand = new AsyncRelayCommand(AddReminderAsync, HandleError);
        ToggleReminderCommand = new AsyncRelayCommand(ToggleReminderAsync, HandleError);
        CompleteReminderCommand = new AsyncRelayCommand(CompleteReminderAsync, HandleError);
    }

    public ObservableCollection<MaintenanceReminder> Reminders { get; } = [];
    public ObservableCollection<ReminderHistoryEntry> CompletedHistory { get; } = [];
    public IReadOnlyList<string> RepeatIntervals { get; } = ["Never", "Daily", "Weekly", "Monthly"];
    public ICommand RefreshCommand { get; }
    public ICommand AddReminderCommand { get; }
    public ICommand ToggleReminderCommand { get; }
    public ICommand CompleteReminderCommand { get; }
    public bool CanEdit => !_isBusy;

    public string NewReminderName { get => _newReminderName; set => SetProperty(ref _newReminderName, value); }
    public DateTime? DueDate { get => _dueDate; set => SetProperty(ref _dueDate, value); }
    public string DueTime { get => _dueTime; set => SetProperty(ref _dueTime, value); }
    public string RepeatInterval { get => _repeatInterval; set => SetProperty(ref _repeatInterval, value); }
    public bool NotifyOnDue { get => _notifyOnDue; set => SetProperty(ref _notifyOnDue, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public async Task LoadAsync()
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var reminders = await _store.GetRemindersAsync();
            var history = await _store.GetHistoryAsync();
            Reminders.Clear();
            foreach (var reminder in reminders)
            {
                Reminders.Add(reminder);
            }

            CompletedHistory.Clear();
            foreach (var entry in history)
            {
                CompletedHistory.Add(entry);
            }

            Status = $"{reminders.Count:N0} reminder(s), including disabled entries. Completed history is stored locally.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task AddReminderAsync()
    {
        var name = NewReminderName.Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Enter a reminder name.");
        }

        if (name.Length > 120)
        {
            throw new InvalidOperationException("Reminder names must be 120 characters or fewer.");
        }

        if (DueDate is null || !TimeSpan.TryParse(DueTime, out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("Choose a due date and enter a time between 00:00 and 23:59.");
        }

        if (!RepeatIntervals.Contains(RepeatInterval))
        {
            throw new InvalidOperationException("Choose a supported repeat interval.");
        }

        var localDueDate = DateTime.SpecifyKind(DueDate.Value.Date.Add(time), DateTimeKind.Local);
        var reminder = new MaintenanceReminder(Guid.NewGuid(), name, new DateTimeOffset(localDueDate), RepeatInterval, true, NotifyOnDue);
        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            await _store.SaveReminderAsync(reminder);
            NewReminderName = string.Empty;
            Status = $"Reminder added for {reminder.DueAt.ToLocalTime():g}.";
            await RefreshListsAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ToggleReminderAsync(object? parameter)
    {
        if (parameter is not MaintenanceReminder reminder)
        {
            throw new ArgumentException("A maintenance reminder is required.", nameof(parameter));
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            await _store.SaveReminderAsync(reminder with { IsEnabled = !reminder.IsEnabled });
            await RefreshListsAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CompleteReminderAsync(object? parameter)
    {
        if (parameter is not MaintenanceReminder reminder)
        {
            throw new ArgumentException("A maintenance reminder is required.", nameof(parameter));
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            await _store.CompleteReminderAsync(reminder, DateTimeOffset.Now);
            Status = reminder.RepeatInterval == "Never"
                ? "Reminder completed and disabled."
                : $"Reminder completed. Its next due time is {NextDueDisplay(reminder)}.";
            await RefreshListsAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshListsAsync()
    {
        var reminders = await _store.GetRemindersAsync();
        var history = await _store.GetHistoryAsync();
        Reminders.Clear();
        foreach (var reminder in reminders)
        {
            Reminders.Add(reminder);
        }

        CompletedHistory.Clear();
        foreach (var entry in history)
        {
            CompletedHistory.Add(entry);
        }
    }

    private static string NextDueDisplay(MaintenanceReminder reminder)
    {
        var next = reminder.RepeatInterval switch
        {
            "Daily" => reminder.DueAt.AddDays(1),
            "Weekly" => reminder.DueAt.AddDays(7),
            "Monthly" => reminder.DueAt.AddMonths(1),
            _ => reminder.DueAt
        };
        while (next <= DateTimeOffset.Now && reminder.RepeatInterval != "Never")
        {
            next = reminder.RepeatInterval switch
            {
                "Daily" => next.AddDays(1),
                "Weekly" => next.AddDays(7),
                "Monthly" => next.AddMonths(1),
                _ => next
            };
        }

        return next.ToLocalTime().ToString("g");
    }

    private void HandleError(Exception exception)
    {
        ErrorMessage = $"Maintenance reminder operation could not complete: {exception.Message}";
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanEdit));
    }
}
