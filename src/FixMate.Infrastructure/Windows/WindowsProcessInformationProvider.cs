using System.ComponentModel;
using System.Diagnostics;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsProcessInformationProvider : IProcessInformationProvider
{
    private readonly object _sampleLock = new();
    private readonly Dictionary<int, (TimeSpan CpuTime, long Timestamp)> _previousSamples = [];

    public Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<RunningProcessInfo>>(() =>
        {
            var processes = new List<RunningProcessInfo>();
            var activeProcessIds = new HashSet<int>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        process.Refresh();
                        var id = process.Id;
                        var cpuTime = process.TotalProcessorTime;
                        var timestamp = Stopwatch.GetTimestamp();
                        activeProcessIds.Add(id);
                        processes.Add(new RunningProcessInfo
                        {
                            Id = id,
                            Name = process.ProcessName,
                            WorkingSetBytes = process.WorkingSet64,
                            CpuUsagePercent = GetCpuUsagePercent(id, cpuTime, timestamp),
                            WindowTitle = process.MainWindowTitle
                        });
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException or NotSupportedException)
                    {
                        // The process may exit or deny access while the snapshot is being collected.
                    }
                }
            }

            lock (_sampleLock)
            {
                foreach (var processId in _previousSamples.Keys.Where(id => !activeProcessIds.Contains(id)).ToArray())
                {
                    _previousSamples.Remove(processId);
                }
            }

            return processes
                .OrderByDescending(process => process.WorkingSetBytes)
                .ThenBy(process => process.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    private double? GetCpuUsagePercent(int processId, TimeSpan cpuTime, long timestamp)
    {
        lock (_sampleLock)
        {
            var current = (cpuTime, timestamp);
            if (!_previousSamples.TryGetValue(processId, out var previous))
            {
                _previousSamples[processId] = current;
                return null;
            }

            _previousSamples[processId] = current;
            var elapsed = Stopwatch.GetElapsedTime(previous.Timestamp, timestamp);
            var cpuDelta = cpuTime - previous.CpuTime;
            if (elapsed <= TimeSpan.Zero || cpuDelta < TimeSpan.Zero)
            {
                return null;
            }

            return Math.Clamp(cpuDelta.TotalSeconds / elapsed.TotalSeconds / Environment.ProcessorCount * 100, 0, 100);
        }
    }
}
