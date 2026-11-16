using System.Windows;
using FixMate.App.Navigation;
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
        services.AddSingleton<IAppSettingsStore, SqliteAppSettingsStore>();
        services.AddSingleton<AppSettings>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<SystemInformationViewModel>();
        services.AddSingleton<MonitoringViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<TroubleshootingViewModel>();
        services.AddSingleton<ErrorCenterViewModel>();
        services.AddSingleton<MaintenanceViewModel>();
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

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"FixMate could not initialize its local settings database.\n\n{exception.Message}",
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
