using System.Diagnostics.Eventing.Reader;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsEventLogProvider : IWindowsEventLogProvider
{
    private const int MaximumAllowedEvents = 1000;
    private const string ErrorAndWarningQuery = "*[System[(Level=1 or Level=2)]]";

    public Task<IReadOnlyList<WindowsEventEntry>> GetRecentEventsAsync(
        int maximumEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEvents);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEvents, MaximumAllowedEvents);

        return Task.Run<IReadOnlyList<WindowsEventEntry>>(() =>
        {
            var events = new List<WindowsEventEntry>();
            foreach (var logName in new[] { "Application", "System" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var query = new EventLogQuery(logName, PathType.LogName, ErrorAndWarningQuery)
                {
                    ReverseDirection = true
                };
                using var reader = new EventLogReader(query);
                for (var index = 0; index < maximumEvents; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var record = reader.ReadEvent();
                    if (record is null)
                    {
                        break;
                    }

                    string message;
                    try
                    {
                        message = record.FormatDescription() ?? string.Empty;
                    }
                    catch (EventLogException)
                    {
                        message = "The event message could not be resolved on this computer. Use the technical details to identify the event.";
                    }

                    events.Add(new WindowsEventEntry(
                        record.LogName,
                        record.RecordId,
                        record.Id,
                        record.LevelDisplayName ?? "Unknown",
                        record.ProviderName ?? "Unknown",
                        record.TimeCreated is DateTime timeCreated ? new DateTimeOffset(timeCreated) : null,
                        record.MachineName ?? Environment.MachineName,
                        message));
                }
            }

            return events
                .OrderByDescending(entry => entry.TimeCreated)
                .Take(maximumEvents)
                .ToArray();
        }, cancellationToken);
    }
}
