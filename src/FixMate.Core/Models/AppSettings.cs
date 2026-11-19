namespace FixMate.Core.Models;

public sealed class AppSettings
{
    public static IReadOnlyList<int> SupportedRefreshIntervals { get; } = [1, 2, 5, 10];
    public static IReadOnlyList<int> SupportedGraphHistoryDurations { get; } = [1, 5, 15, 60];
    public static IReadOnlyList<int> SupportedWarningThresholds { get; } = [60, 70, 80, 85, 90, 95];
    public static IReadOnlyList<string> SupportedThemeModes { get; } = ["Dark", "Light", "System"];
    public static IReadOnlyList<string> SupportedAccentColors { get; } = ["#4D8DFF", "#9B7CFF", "#36C6B3", "#EA6C91"];

    private int _monitoringRefreshIntervalSeconds = 1;
    private int _graphHistoryDurationMinutes = 5;
    private int _cpuWarningThresholdPercent = 85;
    private int _memoryWarningThresholdPercent = 85;
    private bool _reduceAnimations;
    private bool _sidebarCollapsed;
    private string _themeMode = "Dark";
    private string _accentColor = "#4D8DFF";

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

    public event EventHandler? GraphHistoryDurationChanged;

    public int GraphHistoryDurationMinutes
    {
        get => _graphHistoryDurationMinutes;
        set
        {
            if (!SupportedGraphHistoryDurations.Contains(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Choose a graph history duration of 1, 5, 15, or 60 minutes.");
            }

            if (_graphHistoryDurationMinutes != value)
            {
                _graphHistoryDurationMinutes = value;
                GraphHistoryDurationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int CpuWarningThresholdPercent
    {
        get => _cpuWarningThresholdPercent;
        set => _cpuWarningThresholdPercent = ValidateWarningThreshold(value, nameof(value));
    }

    public int MemoryWarningThresholdPercent
    {
        get => _memoryWarningThresholdPercent;
        set => _memoryWarningThresholdPercent = ValidateWarningThreshold(value, nameof(value));
    }

    public string ThemeMode
    {
        get => _themeMode;
        set => _themeMode = SupportedThemeModes.Contains(value, StringComparer.Ordinal)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Choose Dark, Light, or System theme.");
    }

    public string AccentColor
    {
        get => _accentColor;
        set => _accentColor = SupportedAccentColors.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? value.ToUpperInvariant()
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Choose a supported FixMate accent color.");
    }

    public event EventHandler? ReduceAnimationsChanged;

    public bool ReduceAnimations
    {
        get => _reduceAnimations;
        set
        {
            if (_reduceAnimations != value)
            {
                _reduceAnimations = value;
                ReduceAnimationsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool LaunchAtStartup { get; set; }
    public bool StartMinimized { get; set; }
    public bool CompactLayout { get; set; }
    public bool NotificationsEnabled { get; set; } = true;

    public event EventHandler? SidebarCollapsedChanged;

    public bool SidebarCollapsed
    {
        get => _sidebarCollapsed;
        set
        {
            if (_sidebarCollapsed != value)
            {
                _sidebarCollapsed = value;
                SidebarCollapsedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static int ValidateWarningThreshold(int value, string parameterName)
    {
        return SupportedWarningThresholds.Contains(value)
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "Choose a warning threshold from 60, 70, 80, 85, 90, or 95 percent.");
    }
}
