using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FixMate.App.Navigation;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigationService;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private string _sidebarError = string.Empty;
    private string _searchText = string.Empty;
    private string _searchStatusText = string.Empty;

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
        NetworkCenterViewModel networkCenter,
        SoftwareCenterViewModel softwareCenter,
        ReportsViewModel reports,
        MaintenanceSchedulerViewModel maintenanceScheduler,
        SettingsViewModel settingsViewModel,
        AppSettings settings,
        IAppSettingsStore settingsStore)
    {
        _navigationService = navigationService;
        _settings = settings;
        _settingsStore = settingsStore;
        _settings.ReduceAnimationsChanged += OnReduceAnimationsChanged;
        _settings.SidebarCollapsedChanged += OnSidebarCollapsedChanged;
        _navigationService.CurrentViewModelChanged += OnCurrentViewModelChanged;

        ShowOverviewCommand = new RelayCommand(() => _navigationService.NavigateTo(overview));
        ShowSystemInformationCommand = new RelayCommand(() => NavigateAndRefresh(systemInformation, systemInformation.RefreshCommand));
        ShowMonitoringCommand = new RelayCommand(() => _navigationService.NavigateTo(monitoring));
        ShowDiagnosticsCommand = new RelayCommand(() => _navigationService.NavigateTo(diagnostics));
        ShowTroubleshootingCommand = new RelayCommand(() => _navigationService.NavigateTo(troubleshooting));
        ShowErrorCenterCommand = new RelayCommand(() => NavigateAndRefresh(errorCenter, errorCenter.RefreshCommand));
        ShowMaintenanceCommand = new RelayCommand(() => _navigationService.NavigateTo(maintenance));
        ShowProcessManagerCommand = new RelayCommand(() => NavigateAndRefresh(processManager, processManager.RefreshCommand));
        ShowDriverCenterCommand = new RelayCommand(() => NavigateAndRefresh(driverCenter, driverCenter.RefreshCommand));
        ShowSettingsCommand = new RelayCommand(() => _navigationService.NavigateTo(settingsViewModel));
        ToggleSidebarCommand = new AsyncRelayCommand(ToggleSidebarAsync, HandleSidebarError);
        var showNetworkCenterCommand = new RelayCommand(() => _navigationService.NavigateTo(networkCenter));
        var showSoftwareCenterCommand = new RelayCommand(() => _navigationService.NavigateTo(softwareCenter));
        var showReportsCommand = new RelayCommand(() => _navigationService.NavigateTo(reports));
        var showMaintenanceSchedulerCommand = new RelayCommand(() => _navigationService.NavigateTo(maintenanceScheduler));
        SearchCommand = new RelayCommand(NavigateToSearchResult);
        RefreshCommand = new RelayCommand(RefreshCurrentPage);
        ShowNotificationsCommand = new RelayCommand(() => NavigateAndRefresh(errorCenter, errorCenter.RefreshCommand));

        NavigationItems =
        [
            new NavigationItemViewModel("Overview", "\uE80F", overview, ShowOverviewCommand),
            new NavigationItemViewModel("My PC", "\uE950", systemInformation, ShowSystemInformationCommand),
            new NavigationItemViewModel("Live Monitor", "\uE9D9", monitoring, ShowMonitoringCommand),
            new NavigationItemViewModel("Diagnostics", "\uE9CE", diagnostics, ShowDiagnosticsCommand),
            new NavigationItemViewModel("Troubleshooting", "\uE897", troubleshooting, ShowTroubleshootingCommand),
            new NavigationItemViewModel("Maintenance", "\uE90F", maintenance, ShowMaintenanceCommand),
            new NavigationItemViewModel("Error Center", "\uE783", errorCenter, ShowErrorCenterCommand),
            new NavigationItemViewModel("Process Manager", "\uE9F5", processManager, ShowProcessManagerCommand),
            new NavigationItemViewModel("Driver Center", "\uE950", driverCenter, ShowDriverCenterCommand),
            new NavigationItemViewModel("Network Center", "\uE774", networkCenter, showNetworkCenterCommand),
            new NavigationItemViewModel("Software Center", "\uE71D", softwareCenter, showSoftwareCenterCommand),
            new NavigationItemViewModel("Reports", "\uE9F9", reports, showReportsCommand),
            new NavigationItemViewModel("Maintenance Scheduler", "\uE787", maintenanceScheduler, showMaintenanceSchedulerCommand),
            new NavigationItemViewModel("Settings", "\uE713", settingsViewModel, ShowSettingsCommand)
        ];

        OnReduceAnimationsChanged(this, EventArgs.Empty);
        OnSidebarCollapsedChanged(this, EventArgs.Empty);
        _navigationService.NavigateTo(overview);
    }

    public ViewModelBase? CurrentViewModel => _navigationService.CurrentViewModel;
    public string CurrentPageTitle => CurrentViewModel switch
    {
        OverviewViewModel => "Overview",
        SystemInformationViewModel => "My PC",
        MonitoringViewModel => "Live Monitor",
        DiagnosticsViewModel => "Diagnostics",
        TroubleshootingViewModel => "Troubleshooting",
        ErrorCenterViewModel => "Error Center",
        MaintenanceViewModel => "Maintenance",
        ProcessManagerViewModel => "Process Manager",
        DriverCenterViewModel => "Driver Center",
        NetworkCenterViewModel => "Network Center",
        SoftwareCenterViewModel => "Software Center",
        ReportsViewModel => "Reports",
        MaintenanceSchedulerViewModel => "Maintenance Scheduler",
        SettingsViewModel => "Settings",
        PageViewModel page => page.Title,
        _ => "FixMate"
    };

    public string CurrentPageSubtitle => CurrentViewModel switch
    {
        OverviewViewModel => "Your PC at a glance",
        SystemInformationViewModel => "Hardware and Windows details",
        MonitoringViewModel => "Live performance from Windows",
        DiagnosticsViewModel => "Checks and diagnostic results",
        TroubleshootingViewModel => "Guided help for common issues",
        ErrorCenterViewModel => "Recent Windows events",
        MaintenanceViewModel => "Storage and maintenance tools",
        ProcessManagerViewModel => "Running applications and processes",
        DriverCenterViewModel => "Installed device drivers",
        NetworkCenterViewModel => "Local adapter details and selected-target diagnostics",
        SoftwareCenterViewModel => "Installed software inventory from Windows",
        ReportsViewModel => "Preview, export, and print system information",
        MaintenanceSchedulerViewModel => "Locally stored recurring maintenance reminders",
        SettingsViewModel => "Personalize your FixMate experience",
        PageViewModel page => page.Description,
        _ => string.Empty
    };

    public string DeviceLabel => $"LOCAL DEVICE  ·  {Environment.MachineName}";
    public GridLength SidebarColumnWidth => new(_settings.SidebarCollapsed ? 68 : 238);
    public Thickness SidebarContentMargin => _settings.SidebarCollapsed ? new Thickness(8, 20, 8, 20) : new Thickness(16, 20, 16, 20);
    public bool IsSidebarExpanded => !_settings.SidebarCollapsed;
    public string SidebarToggleLabel => _settings.SidebarCollapsed ? "Expand sidebar" : "Collapse sidebar";
    public string SidebarToggleIcon => _settings.SidebarCollapsed ? "\uE76C" : "\uE76B";
    public string SidebarError { get => _sidebarError; private set => SetProperty(ref _sidebarError, value); }
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
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
    public ICommand ToggleSidebarCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ShowNotificationsCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string SearchStatusText
    {
        get => _searchStatusText;
        private set => SetProperty(ref _searchStatusText, value);
    }

    private void OnReduceAnimationsChanged(object? sender, EventArgs e)
    {
        foreach (var item in NavigationItems)
        {
            item.ReduceAnimations = _settings.ReduceAnimations;
        }
    }

    private void OnSidebarCollapsedChanged(object? sender, EventArgs e)
    {
        foreach (var item in NavigationItems)
        {
            item.ShowTitle = IsSidebarExpanded;
        }

        OnPropertyChanged(nameof(SidebarColumnWidth));
        OnPropertyChanged(nameof(SidebarContentMargin));
        OnPropertyChanged(nameof(IsSidebarExpanded));
        OnPropertyChanged(nameof(SidebarToggleLabel));
        OnPropertyChanged(nameof(SidebarToggleIcon));
    }

    private async Task ToggleSidebarAsync()
    {
        SidebarError = string.Empty;
        _settings.SidebarCollapsed = !_settings.SidebarCollapsed;
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch
        {
            _settings.SidebarCollapsed = !_settings.SidebarCollapsed;
            throw;
        }

    }

    private void HandleSidebarError(Exception exception)
    {
        SidebarError = $"Sidebar preference could not be saved: {exception.Message}";
    }

    private void OnCurrentViewModelChanged(object? sender, EventArgs e)
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = ReferenceEquals(item.Target, CurrentViewModel);
        }

        OnPropertyChanged(nameof(CurrentViewModel));
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageSubtitle));
    }

    private void NavigateAndRefresh(ViewModelBase viewModel, ICommand refreshCommand)
    {
        _navigationService.NavigateTo(viewModel);
        refreshCommand.Execute(null);
    }

    private void NavigateToSearchResult()
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            SearchStatusText = string.Empty;
            return;
        }

        var match = NavigationItems.FirstOrDefault(item => item.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            SearchStatusText = "No FixMate section matches that search.";
            return;
        }

        SearchStatusText = string.Empty;
        match.NavigateCommand.Execute(null);
    }

    private void RefreshCurrentPage()
    {
        switch (CurrentViewModel)
        {
            case OverviewViewModel overview:
                _ = overview.RefreshAsync();
                break;
            case SystemInformationViewModel systemInformation:
                systemInformation.RefreshCommand.Execute(null);
                break;
            case MonitoringViewModel monitoring:
                monitoring.RefreshCommand.Execute(null);
                break;
            case DiagnosticsViewModel diagnostics:
                diagnostics.RunDiagnosticsCommand.Execute(null);
                break;
            case MaintenanceViewModel maintenance:
                maintenance.RefreshStorageVolumesCommand.Execute(null);
                maintenance.RefreshStartupCommand.Execute(null);
                maintenance.RefreshApplicationsCommand.Execute(null);
                break;
            case ErrorCenterViewModel errorCenter:
                errorCenter.RefreshCommand.Execute(null);
                break;
            case ProcessManagerViewModel processManager:
                processManager.RefreshCommand.Execute(null);
                break;
            case DriverCenterViewModel driverCenter:
                driverCenter.RefreshCommand.Execute(null);
                break;
            case NetworkCenterViewModel networkCenter:
                networkCenter.RefreshCommand.Execute(null);
                break;
            case SoftwareCenterViewModel softwareCenter:
                softwareCenter.RefreshCommand.Execute(null);
                break;
            case ReportsViewModel reports:
                reports.RefreshHistoryCommand.Execute(null);
                break;
            case MaintenanceSchedulerViewModel scheduler:
                scheduler.RefreshCommand.Execute(null);
                break;
        }
    }
}
