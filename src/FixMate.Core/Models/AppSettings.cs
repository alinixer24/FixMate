namespace FixMate.Core.Models;

public sealed class AppSettings
{
    private static readonly int[] SupportedRefreshIntervals = [1, 2, 5, 10];
    private int _monitoringRefreshIntervalSeconds = 1;

    public int MonitoringRefreshIntervalSeconds
    {
        get => _monitoringRefreshIntervalSeconds;
        set
        {
            if (!SupportedRefreshIntervals.Contains(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The monitoring interval must be 1, 2, 5, or 10 seconds.");
            }

            _monitoringRefreshIntervalSeconds = value;
        }
    }
}
