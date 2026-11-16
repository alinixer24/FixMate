using System.Diagnostics;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.Infrastructure.Windows;

public sealed class WindowsProcessInformationProvider : IProcessInformationProvider
{
    public Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<RunningProcessInfo>>(() =>
        {
            var processes = new List<RunningProcessInfo>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        process.Refresh();
                        processes.Add(new RunningProcessInfo
                        {
                            Id = process.Id,
                            Name = process.ProcessName,
                            WorkingSetBytes = process.WorkingSet64,
                            WindowTitle = process.MainWindowTitle
                        });
                    }
                    catch (InvalidOperationException)
                    {
                        // The process may exit while the snapshot is being collected.
                    }
                }
            }

            return processes
                .OrderByDescending(process => process.WorkingSetBytes)
                .ThenBy(process => process.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }
}
