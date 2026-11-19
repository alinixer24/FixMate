using System.IO;
using System.Windows;
using FixMate.App.Navigation;
using FixMate.App.Services;
using FixMate.App.ViewModels;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using FixMate.Infrastructure.Storage;
using FixMate.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace FixMate.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<ISystemInformationProvider, WindowsSystemInformationProvider>();
        services.AddSingleton<IMonitoringDataProvider, WindowsMonitoringDataProvider>();
        services.AddSingleton<IDiagnosticsProvider, WindowsDiagnosticsProvider>();
        services.AddSingleton<IMaintenanceProvider, WindowsMaintenanceProvider>();
        services.AddSingleton<IWindowsEventLogProvider, WindowsEventLogProvider>();
        services.AddSingleton<IProcessInformationProvider, WindowsProcessInformationProvider>();
        services.AddSingleton<IDriverInformationProvider, WindowsDriverInformationProvider>();
        services.AddSingleton<INetworkDiagnosticsService, WindowsNetworkDiagnosticsService>();
        services.AddSingleton<IAppSettingsStore, SqliteAppSettingsStore>();
        services.AddSingleton<IApplicationDataService, LocalApplicationDataService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<IMaintenanceReminderStore, SqliteMaintenanceReminderStore>();
        services.AddSingleton<IMaintenanceHistoryStore, SqliteMaintenanceHistoryStore>();
        services.AddSingleton<INetworkHistoryStore, SqliteNetworkHistoryStore>();
        services.AddSingleton<MaintenanceReminderScheduler>(provider => new MaintenanceReminderScheduler(
            provider.GetRequiredService<IMaintenanceReminderStore>(),
            provider.GetRequiredService<AppSettings>(),
            Current.Dispatcher));
        services.AddSingleton<AppSettings>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<SystemInformationViewModel>();
        services.AddSingleton<MonitoringViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<TroubleshootingViewModel>();
        services.AddSingleton<ErrorCenterViewModel>();
        services.AddSingleton<MaintenanceViewModel>();
        services.AddSingleton<ProcessManagerViewModel>();
        services.AddSingleton<DriverCenterViewModel>();
        services.AddSingleton<NetworkCenterViewModel>();
        services.AddSingleton<SoftwareCenterViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<MaintenanceSchedulerViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        try
        {
            var settingsStore = _serviceProvider.GetRequiredService<IAppSettingsStore>();
            var loadedSettings = await settingsStore.LoadAsync();
            var appSettings = _serviceProvider.GetRequiredService<AppSettings>();
            appSettings.MonitoringRefreshIntervalSeconds = loadedSettings.MonitoringRefreshIntervalSeconds;
            appSettings.ThemeMode = loadedSettings.ThemeMode;
            appSettings.AccentColor = loadedSettings.AccentColor;
            appSettings.ReduceAnimations = loadedSettings.ReduceAnimations;
            appSettings.LaunchAtStartup = loadedSettings.LaunchAtStartup;
            appSettings.StartMinimized = loadedSettings.StartMinimized;
            appSettings.GraphHistoryDurationMinutes = loadedSettings.GraphHistoryDurationMinutes;
            appSettings.CompactLayout = loadedSettings.CompactLayout;
            appSettings.NotificationsEnabled = loadedSettings.NotificationsEnabled;
            appSettings.SidebarCollapsed = loadedSettings.SidebarCollapsed;
            appSettings.CpuWarningThresholdPercent = loadedSettings.CpuWarningThresholdPercent;
            appSettings.MemoryWarningThresholdPercent = loadedSettings.MemoryWarningThresholdPercent;
            _serviceProvider.GetRequiredService<ThemeService>().Apply(appSettings);

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
            if (appSettings.StartMinimized || e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            {
                mainWindow.WindowState = WindowState.Minimized;
            }

            _serviceProvider.GetRequiredService<MaintenanceReminderScheduler>().Start();
        }
        catch (Exception exception)
        {
            var details = exception.ToString();
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(appData))
                {
                    throw new InvalidOperationException("The local application data folder is unavailable.");
                }

                var logDirectory = Path.Combine(appData, "FixMate", "Logs");
                Directory.CreateDirectory(logDirectory);
                var logPath = Path.Combine(logDirectory, "startup.log");
                File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] FixMate startup failed.{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
                details += $"\n\nDetails logged to: {logPath}";
            }
            catch (Exception loggingException)
            {
                details += $"\n\nStartup details could not be logged: {loggingException.Message}";
            }

            MessageBox.Show(
                $"FixMate could not start.\n\n{details}",
                "FixMate startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
