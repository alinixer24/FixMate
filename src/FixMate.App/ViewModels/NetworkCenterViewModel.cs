using System.Collections.ObjectModel;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class NetworkCenterViewModel : ViewModelBase
{
    private readonly INetworkDiagnosticsService _service;
    private readonly INetworkHistoryStore _historyStore;
    private string _target = string.Empty;
    private string _status = "Network adapters have not been loaded.";
    private string _errorMessage = string.Empty;
    private string _resultText = "Choose a network tool and enter a host name or IP address.";
    private bool _isBusy;

    public NetworkCenterViewModel(INetworkDiagnosticsService service, INetworkHistoryStore historyStore)
    {
        _service = service;
        _historyStore = historyStore;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleError);
        PingCommand = new AsyncRelayCommand(() => RunAsync(_service.PingAsync), HandleError);
        TraceRouteCommand = new AsyncRelayCommand(() => RunAsync(_service.TraceRouteAsync), HandleError);
        DnsLookupCommand = new AsyncRelayCommand(() => RunAsync(_service.LookupDnsAsync), HandleError);
        ConnectivityTestCommand = new AsyncRelayCommand(() => RunAsync(_service.TestConnectivityAsync), HandleError);
        OpenNetworkSettingsCommand = new RelayCommand(OpenNetworkSettings);
    }

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];
    public ObservableCollection<string> ConnectivityHistory { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand PingCommand { get; }
    public ICommand TraceRouteCommand { get; }
    public ICommand DnsLookupCommand { get; }
    public ICommand ConnectivityTestCommand { get; }
    public ICommand OpenNetworkSettingsCommand { get; }
    public bool CanRunTools => !_isBusy && !string.IsNullOrWhiteSpace(Target);

    public string Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, value))
            {
                OnPropertyChanged(nameof(CanRunTools));
            }
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }

    public async Task LoadAsync()
    {
        try
        {
            await RefreshAsync();
            await RefreshHistoryAsync();
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
    }

    private async Task RefreshHistoryAsync()
    {
        var history = await _historyStore.GetHistoryAsync();
        ConnectivityHistory.Clear();
        foreach (var result in history)
        {
            ConnectivityHistory.Add(FormatHistoryEntry(result));
        }
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var adapters = await _service.GetAdaptersAsync();
            Adapters.Clear();
            foreach (var adapter in adapters)
            {
                Adapters.Add(adapter);
            }

            Status = adapters.Count == 0
                ? "Windows did not report any network adapters."
                : $"Windows reported {adapters.Count:N0} network adapter(s). Addresses, gateways, DNS, and DHCP state are shown only when available.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunAsync(Func<string, CancellationToken, Task<NetworkDiagnosticResult>> operation)
    {
        var host = Target.Trim();
        if (host.Length == 0)
        {
            throw new InvalidOperationException("Enter a host name or IP address first.");
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var result = await operation(host, CancellationToken.None);
            ResultText = $"{result.Tool} · {result.CompletedAt.ToLocalTime():G}\n{result.Summary}\n\n{string.Join(Environment.NewLine, result.Details)}";
            await _historyStore.SaveResultAsync(result);
            await RefreshHistoryAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string FormatHistoryEntry(NetworkDiagnosticResult result)
    {
        return $"{result.CompletedAt.ToLocalTime():G} · {result.Tool} · {result.Target} · {(result.Succeeded ? "Responded / resolved" : "No response / no result")}";
    }

    private void HandleError(Exception exception)
    {
        ErrorMessage = $"Network operation could not complete: {exception.Message}";
    }

    private void OpenNetworkSettings()
    {
        try
        {
            if (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true }) is null)
            {
                throw new InvalidOperationException("Windows did not open Network & internet settings.");
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            HandleError(exception);
        }
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanRunTools));
    }
}
