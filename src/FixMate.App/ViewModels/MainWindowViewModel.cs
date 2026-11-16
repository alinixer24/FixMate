using System.Windows.Input;
using FixMate.App.Navigation;

namespace FixMate.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigationService;

    public MainWindowViewModel(
        NavigationService navigationService,
        OverviewViewModel overview,
        SystemInformationViewModel systemInformation,
        MonitoringViewModel monitoring,
        DiagnosticsViewModel diagnostics,
        TroubleshootingViewModel troubleshooting,
        ErrorCenterViewModel errorCenter,
        MaintenanceViewModel maintenance,
        ProcessManagerViewModel processManager,
        DriverCenterViewModel driverCenter,
        SettingsViewModel settings)
    {
        _navigationService = navigationService;
        _navigationService.CurrentViewModelChanged += (_, _) => OnPropertyChanged(nameof(CurrentViewModel));

        ShowOverviewCommand = new RelayCommand(() => _navigationService.NavigateTo(overview));
        ShowSystemInformationCommand = new RelayCommand(() =>
        {
            _navigationService.NavigateTo(systemInformation);
            systemInformation.RefreshCommand.Execute(null);
        });
        ShowMonitoringCommand = new RelayCommand(() => _navigationService.NavigateTo(monitoring));
        ShowDiagnosticsCommand = new RelayCommand(() => _navigationService.NavigateTo(diagnostics));
        ShowTroubleshootingCommand = new RelayCommand(() => _navigationService.NavigateTo(troubleshooting));
        ShowErrorCenterCommand = new RelayCommand(() => _navigationService.NavigateTo(errorCenter));
        ShowMaintenanceCommand = new RelayCommand(() => _navigationService.NavigateTo(maintenance));
        ShowProcessManagerCommand = new RelayCommand(() =>
        {
            _navigationService.NavigateTo(processManager);
            processManager.RefreshCommand.Execute(null);
        });
        ShowDriverCenterCommand = new RelayCommand(() =>
        {
            _navigationService.NavigateTo(driverCenter);
            driverCenter.RefreshCommand.Execute(null);
        });
        ShowSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo(settings));

        _navigationService.NavigateTo(overview);
    }

    public ViewModelBase? CurrentViewModel => _navigationService.CurrentViewModel;

    public ICommand ShowOverviewCommand { get; }
    public ICommand ShowSystemInformationCommand { get; }
    public ICommand ShowMonitoringCommand { get; }
    public ICommand ShowDiagnosticsCommand { get; }
    public ICommand ShowTroubleshootingCommand { get; }
    public ICommand ShowErrorCenterCommand { get; }
    public ICommand ShowMaintenanceCommand { get; }
    public ICommand ShowProcessManagerCommand { get; }
    public ICommand ShowDriverCenterCommand { get; }
    public ICommand ShowSettingsCommand { get; }
}
