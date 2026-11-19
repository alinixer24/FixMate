using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.Services;

public sealed class MaintenanceReminderScheduler : IDisposable
{
    private readonly IMaintenanceReminderStore _store;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;

    public MaintenanceReminderScheduler(IMaintenanceReminderStore store, AppSettings settings, Dispatcher dispatcher)
    {
        _store = store;
        _settings = settings;
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        if (_cancellation is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _loopTask = RunAsync(_cancellation.Token);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_settings.NotificationsEnabled)
                {
                    var dueReminders = await _store.GetDueRemindersAsync(DateTimeOffset.Now, cancellationToken);
                    foreach (var reminder in dueReminders.Where(reminder => reminder.Notify))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await _dispatcher.InvokeAsync(() =>
                        {
                            MessageBox.Show(
                                $"{reminder.Name}\n\nDue {reminder.DueAt.ToLocalTime():g}. Dismiss this reminder to record it as completed. Recurrence: {reminder.RepeatInterval}.",
                                "FixMate maintenance reminder",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }, DispatcherPriority.Normal, cancellationToken);
                        await _store.CompleteReminderAsync(reminder, DateTimeOffset.Now, cancellationToken);
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError($"FixMate reminder scheduler stopped: {exception}");
            await _dispatcher.InvokeAsync(() => MessageBox.Show(
                $"Maintenance reminders stopped because the local reminder database could not be updated.\n\n{exception.Message}",
                "FixMate reminder error",
                MessageBoxButton.OK,
                MessageBoxImage.Error));
        }
    }
}
