using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FixMate.App.Services;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private AppSettings _savedSettings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ThemeService _themeService;
    private readonly StartupService _startupService;
    private readonly IApplicationDataService _applicationDataService;
    private int _selectedRefreshIntervalSeconds;
    private string _selectedThemeMode;
    private string _selectedAccentColor;
    private bool _reduceAnimations;
    private bool _launchAtStartup;
    private bool _startMinimized;
    private bool _compactLayout;
    private bool _notificationsEnabled;
    private int _graphHistoryDurationMinutes;
    private int _cpuWarningThresholdPercent;
    private int _memoryWarningThresholdPercent;
    private bool _isSaving;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public SettingsViewModel(AppSettings settings, IAppSettingsStore settingsStore, ThemeService themeService, StartupService startupService, IApplicationDataService applicationDataService)
    {
        _settings = settings;
        _savedSettings = CreateSettingsSnapshot(settings);
        _settingsStore = settingsStore;
        _themeService = themeService;
        _startupService = startupService;
        _applicationDataService = applicationDataService;
        _selectedRefreshIntervalSeconds = settings.MonitoringRefreshIntervalSeconds;
        _selectedThemeMode = settings.ThemeMode;
        _selectedAccentColor = settings.AccentColor;
        _reduceAnimations = settings.ReduceAnimations;
        _launchAtStartup = settings.LaunchAtStartup;
        _startMinimized = settings.StartMinimized;
        _compactLayout = settings.CompactLayout;
        _notificationsEnabled = settings.NotificationsEnabled;
        _graphHistoryDurationMinutes = settings.GraphHistoryDurationMinutes;
        _cpuWarningThresholdPercent = settings.CpuWarningThresholdPercent;
        _memoryWarningThresholdPercent = settings.MemoryWarningThresholdPercent;
        SaveCommand = new AsyncRelayCommand(SaveAsync, HandleSaveError);
        RestoreDefaultsCommand = new AsyncRelayCommand(RestoreDefaultsAsync, HandleSaveError);
        ClearApplicationDataCommand = new AsyncRelayCommand(ClearApplicationDataAsync, HandleSaveError);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        OpenConfigurationFolderCommand = new RelayCommand(OpenConfigurationFolder);
    }

    public IReadOnlyList<int> RefreshIntervalOptions => AppSettings.SupportedRefreshIntervals;
    public IReadOnlyList<int> GraphHistoryOptions => AppSettings.SupportedGraphHistoryDurations;
    public IReadOnlyList<int> WarningThresholdOptions => AppSettings.SupportedWarningThresholds;
    public IReadOnlyList<string> ThemeModeOptions => AppSettings.SupportedThemeModes;
    public IReadOnlyList<AccentColorOption> AccentColorOptions { get; } =
    [
        new("Blue", "#4D8DFF"),
        new("Violet", "#9B7CFF"),
        new("Teal", "#36C6B3"),
        new("Rose", "#EA6C91")
    ];
    public ICommand SaveCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }
    public ICommand ClearApplicationDataCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand OpenConfigurationFolderCommand { get; }
    public string ApplicationVersion => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "Version unavailable";
    public string PrivacySummary => "FixMate currently stores preferences, reminders, reports, and logs locally. No telemetry or remote upload is implemented.";
    public string UpdateSummary => "Automatic update checks are unavailable because no trusted update service is configured.";
    public string LicenseSummary => "License information is not configured in this build.";
    public bool CanSave => !_isSaving;
    public bool ReduceAnimations
    {
        get => _reduceAnimations;
        set
        {
            if (_reduceAnimations != value)
            {
                _reduceAnimations = value;
                OnPropertyChanged();
                PreviewAppearance();
            }
        }
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (_launchAtStartup != value)
            {
                _launchAtStartup = value;
                OnPropertyChanged();
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (_startMinimized != value)
            {
                _startMinimized = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CompactLayout
    {
        get => _compactLayout;
        set
        {
            if (_compactLayout != value)
            {
                _compactLayout = value;
                OnPropertyChanged();
                PreviewAppearance();
            }
        }
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (_notificationsEnabled != value)
            {
                _notificationsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public int GraphHistoryDurationMinutes
    {
        get => _graphHistoryDurationMinutes;
        set
        {
            if (GraphHistoryOptions.Contains(value) && _graphHistoryDurationMinutes != value)
            {
                _graphHistoryDurationMinutes = value;
                OnPropertyChanged();
            }
        }
    }

    public int CpuWarningThresholdPercent
    {
        get => _cpuWarningThresholdPercent;
        set
        {
            if (WarningThresholdOptions.Contains(value) && _cpuWarningThresholdPercent != value)
            {
                _cpuWarningThresholdPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public int MemoryWarningThresholdPercent
    {
        get => _memoryWarningThresholdPercent;
        set
        {
            if (WarningThresholdOptions.Contains(value) && _memoryWarningThresholdPercent != value)
            {
                _memoryWarningThresholdPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            if (ThemeModeOptions.Contains(value, StringComparer.Ordinal) && _selectedThemeMode != value)
            {
                _selectedThemeMode = value;
                OnPropertyChanged();
                PreviewAppearance();
            }
        }
    }

    public string SelectedAccentColor
    {
        get => _selectedAccentColor;
        set
        {
            if (AppSettings.SupportedAccentColors.Contains(value, StringComparer.OrdinalIgnoreCase) && _selectedAccentColor != value)
            {
                _selectedAccentColor = value;
                OnPropertyChanged();
                PreviewAppearance();
            }
        }
    }

    public int SelectedRefreshIntervalSeconds
    {
        get => _selectedRefreshIntervalSeconds;
        set
        {
            if (!RefreshIntervalOptions.Contains(value) || _selectedRefreshIntervalSeconds == value)
            {
                return;
            }

            _selectedRefreshIntervalSeconds = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
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

    private async Task RestoreDefaultsAsync()
    {
        if (_isSaving)
        {
            return;
        }

        var answer = MessageBox.Show(
            Application.Current.MainWindow,
            "Restore FixMate preferences to their default values? This also updates the Windows sign-in launch setting.",
            "Restore default settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        SetSaving(true);
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var defaults = new AppSettings();
            await SaveSettingsAsync(defaults);
            ApplyEditorValues(defaults);
            StatusMessage = "FixMate settings have been restored to defaults.";
        }
        finally
        {
            SetSaving(false);
        }
    }

    private async Task ClearApplicationDataAsync()
    {
        if (_isSaving)
        {
            return;
        }

        var answer = MessageBox.Show(
            Application.Current.MainWindow,
            "This will remove saved reminders and completion history, report exports, and logs from this PC, then reset preferences. The application cannot undo this action. Continue?",
            "Clear FixMate application data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        SetSaving(true);
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        var previousSettings = CreateSettingsSnapshot(_savedSettings);
        var defaults = new AppSettings();
        try
        {
            try
            {
                _startupService.Apply(defaults.LaunchAtStartup, defaults.StartMinimized);
            }
            catch (Exception startupException)
            {
                RestoreStartupState(previousSettings, startupException);
                throw;
            }

            try
            {
                await _applicationDataService.ClearUserDataAsync();
                await _settingsStore.SaveAsync(defaults);
            }
            catch (Exception operationException)
            {
                CopySettings(previousSettings);
                ApplyEditorValues(previousSettings);
                _themeService.Apply(_settings);
                RestoreStartupState(previousSettings, operationException);
                throw;
            }

            CopySettings(defaults);
            _savedSettings = CreateSettingsSnapshot(defaults);
            ApplyEditorValues(defaults);
            _themeService.Apply(_settings);
            StatusMessage = "Local FixMate data was cleared and settings were reset.";
        }
        finally
        {
            SetSaving(false);
        }
    }

    private void ApplyEditorValues(AppSettings settings)
    {
        SelectedRefreshIntervalSeconds = settings.MonitoringRefreshIntervalSeconds;
        SelectedThemeMode = settings.ThemeMode;
        SelectedAccentColor = settings.AccentColor;
        ReduceAnimations = settings.ReduceAnimations;
        LaunchAtStartup = settings.LaunchAtStartup;
        StartMinimized = settings.StartMinimized;
        GraphHistoryDurationMinutes = settings.GraphHistoryDurationMinutes;
        CompactLayout = settings.CompactLayout;
        NotificationsEnabled = settings.NotificationsEnabled;
        CpuWarningThresholdPercent = settings.CpuWarningThresholdPercent;
        MemoryWarningThresholdPercent = settings.MemoryWarningThresholdPercent;
    }

    private void CopySettings(AppSettings source)
    {
        _settings.MonitoringRefreshIntervalSeconds = source.MonitoringRefreshIntervalSeconds;
        _settings.ThemeMode = source.ThemeMode;
        _settings.AccentColor = source.AccentColor;
        _settings.ReduceAnimations = source.ReduceAnimations;
        _settings.LaunchAtStartup = source.LaunchAtStartup;
        _settings.StartMinimized = source.StartMinimized;
        _settings.GraphHistoryDurationMinutes = source.GraphHistoryDurationMinutes;
        _settings.CompactLayout = source.CompactLayout;
        _settings.NotificationsEnabled = source.NotificationsEnabled;
        _settings.SidebarCollapsed = source.SidebarCollapsed;
        _settings.CpuWarningThresholdPercent = source.CpuWarningThresholdPercent;
        _settings.MemoryWarningThresholdPercent = source.MemoryWarningThresholdPercent;
    }

    private void OpenLogsFolder()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                throw new InvalidOperationException("The current user's local application data folder is unavailable.");
            }

            var path = Path.Combine(appData, "FixMate", "Logs");
            Directory.CreateDirectory(path);
            OpenFolder(path);
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            HandleActionError(exception);
        }
    }

    private void OpenConfigurationFolder()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                throw new InvalidOperationException("The current user's local application data folder is unavailable.");
            }

            OpenFolder(Path.Combine(appData, "FixMate"));
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            HandleActionError(exception);
        }
    }

    private static void OpenFolder(string path)
    {
        if (Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }) is null)
        {
            throw new InvalidOperationException("Windows did not open the requested FixMate folder.");
        }
    }

    private async Task SaveAsync()
    {
        if (_isSaving)
        {
            return;
        }

        SetSaving(true);
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var updatedSettings = new AppSettings
            {
                MonitoringRefreshIntervalSeconds = SelectedRefreshIntervalSeconds,
                ThemeMode = SelectedThemeMode,
                AccentColor = SelectedAccentColor,
                ReduceAnimations = ReduceAnimations,
                LaunchAtStartup = LaunchAtStartup,
                StartMinimized = StartMinimized,
                GraphHistoryDurationMinutes = GraphHistoryDurationMinutes,
                CompactLayout = CompactLayout,
                NotificationsEnabled = NotificationsEnabled,
                SidebarCollapsed = _settings.SidebarCollapsed,
                CpuWarningThresholdPercent = CpuWarningThresholdPercent,
                MemoryWarningThresholdPercent = MemoryWarningThresholdPercent
            };

            await SaveSettingsAsync(updatedSettings);
            StatusMessage = "Settings saved. Appearance and monitoring changes apply now; startup choices apply on the next sign-in or app launch.";
        }
        finally
        {
            SetSaving(false);
        }
    }

    private async Task SaveSettingsAsync(AppSettings updatedSettings)
    {
        var previousSettings = CreateSettingsSnapshot(_savedSettings);
        var startupSettingsChanged = updatedSettings.LaunchAtStartup != previousSettings.LaunchAtStartup ||
            updatedSettings.StartMinimized != previousSettings.StartMinimized;
        if (startupSettingsChanged)
        {
            try
            {
                _startupService.Apply(updatedSettings.LaunchAtStartup, updatedSettings.StartMinimized);
            }
            catch (Exception startupException)
            {
                ApplyEditorValues(previousSettings);
                RestoreStartupState(previousSettings, startupException);
                throw;
            }
        }

        try
        {
            await _settingsStore.SaveAsync(updatedSettings);
        }
        catch (Exception saveException)
        {
            CopySettings(previousSettings);
            _themeService.Apply(_settings);
            ApplyEditorValues(previousSettings);
            if (startupSettingsChanged)
            {
                RestoreStartupState(previousSettings, saveException);
            }

            throw;
        }

        CopySettings(updatedSettings);
        _savedSettings = CreateSettingsSnapshot(updatedSettings);
        ApplyEditorValues(updatedSettings);
        _themeService.Apply(_settings);
    }

    private void PreviewAppearance()
    {
        _settings.ThemeMode = SelectedThemeMode;
        _settings.AccentColor = SelectedAccentColor;
        _settings.ReduceAnimations = ReduceAnimations;
        _settings.CompactLayout = CompactLayout;
        _themeService.Apply(_settings);
        ErrorMessage = string.Empty;
        StatusMessage = "Appearance preview active. Save to keep these choices.";
    }

    private void RestoreStartupState(AppSettings settings, Exception originalException)
    {
        try
        {
            _startupService.Apply(settings.LaunchAtStartup, settings.StartMinimized);
        }
        catch (Exception rollbackException)
        {
            throw new AggregateException(
                "The settings operation failed, and FixMate could not restore the previous Windows startup configuration.",
                originalException,
                rollbackException);
        }
    }

    private static AppSettings CreateSettingsSnapshot(AppSettings settings)
    {
        return new AppSettings
        {
            MonitoringRefreshIntervalSeconds = settings.MonitoringRefreshIntervalSeconds,
            ThemeMode = settings.ThemeMode,
            AccentColor = settings.AccentColor,
            ReduceAnimations = settings.ReduceAnimations,
            LaunchAtStartup = settings.LaunchAtStartup,
            StartMinimized = settings.StartMinimized,
            GraphHistoryDurationMinutes = settings.GraphHistoryDurationMinutes,
            CompactLayout = settings.CompactLayout,
            NotificationsEnabled = settings.NotificationsEnabled,
            SidebarCollapsed = settings.SidebarCollapsed,
            CpuWarningThresholdPercent = settings.CpuWarningThresholdPercent,
            MemoryWarningThresholdPercent = settings.MemoryWarningThresholdPercent
        };
    }

    private void SetSaving(bool value)
    {
        _isSaving = value;
        OnPropertyChanged(nameof(CanSave));
    }

    private void HandleSaveError(Exception exception)
    {
        ErrorMessage = $"Settings operation could not complete: {exception.Message}";
    }

    private void HandleActionError(Exception exception)
    {
        ErrorMessage = $"The requested FixMate action could not complete: {exception.Message}";
    }
}

public sealed record AccentColorOption(string Name, string Value);
