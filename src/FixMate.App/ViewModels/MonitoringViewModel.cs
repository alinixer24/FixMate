using System.Collections.ObjectModel;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class MonitoringViewModel : ViewModelBase
{
    private const int HistoryLimit = 60;
    private readonly IMonitoringDataProvider _provider;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _samplingCancellation;
    private string _errorMessage = string.Empty;
    private string _lastUpdatedText = "Waiting for the first sample...";

    public MonitoringViewModel(IMonitoringDataProvider provider, AppSettings settings)
    {
        _provider = provider;
        _settings = settings;
    }

    public ObservableCollection<double> CpuHistory { get; } = [];
    public ObservableCollection<double> MemoryHistory { get; } = [];
    public ObservableCollection<double> DiskReadHistory { get; } = [];
    public ObservableCollection<double> DiskWriteHistory { get; } = [];
    public ObservableCollection<double> NetworkReceiveHistory { get; } = [];
    public ObservableCollection<double> NetworkSendHistory { get; } = [];

    public string CpuValue { get; private set; } = "--";
    public string CpuDetails { get; private set; } = "Waiting for a sample.";
    public string MemoryValue { get; private set; } = "--";
    public string MemoryDetails { get; private set; } = "Waiting for a sample.";
    public string DiskReadValue { get; private set; } = "--";
    public string DiskWriteValue { get; private set; } = "--";
    public string DiskDetails { get; private set; } = "Waiting for a sample.";
    public string NetworkReceiveValue { get; private set; } = "--";
    public string NetworkSendValue { get; private set; } = "--";
    public string NetworkDetails { get; private set; } = "Waiting for a sample.";

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set
        {
            _lastUpdatedText = value;
            OnPropertyChanged();
        }
    }

    public void Start()
    {
        if (_samplingCancellation is { IsCancellationRequested: false })
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _samplingCancellation = cancellation;
        _ = SampleContinuouslyAsync(cancellation);
    }

    public void Stop()
    {
        _samplingCancellation?.Cancel();
    }

    private async Task SampleContinuouslyAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        ErrorMessage = string.Empty;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var snapshot = await _provider.GetSnapshotAsync(token);
                ApplySnapshot(snapshot);
                await Task.Delay(TimeSpan.FromSeconds(_settings.MonitoringRefreshIntervalSeconds), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Live monitoring stopped: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_samplingCancellation, cancellation))
            {
                _samplingCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ApplySnapshot(FixMate.Core.Results.MonitoringSnapshot snapshot)
    {
        CpuValue = FormatPercent(snapshot.CpuUsagePercent);
        CpuDetails = snapshot.CpuUsagePercent.Details;
        MemoryValue = FormatPercent(snapshot.MemoryUsagePercent);
        MemoryDetails = snapshot.MemoryUsagePercent.Details;
        DiskReadValue = FormatRate(snapshot.DiskReadBytesPerSecond);
        DiskWriteValue = FormatRate(snapshot.DiskWriteBytesPerSecond);
        DiskDetails = JoinDetails(snapshot.DiskReadBytesPerSecond, snapshot.DiskWriteBytesPerSecond);
        NetworkReceiveValue = FormatRate(snapshot.NetworkReceiveBytesPerSecond);
        NetworkSendValue = FormatRate(snapshot.NetworkSendBytesPerSecond);
        NetworkDetails = JoinDetails(snapshot.NetworkReceiveBytesPerSecond, snapshot.NetworkSendBytesPerSecond);
        LastUpdatedText = $"Updated {snapshot.CollectedAt.ToLocalTime():T} · Refreshes every second";

        AddIfAvailable(CpuHistory, snapshot.CpuUsagePercent);
        AddIfAvailable(MemoryHistory, snapshot.MemoryUsagePercent);
        AddIfAvailable(DiskReadHistory, snapshot.DiskReadBytesPerSecond);
        AddIfAvailable(DiskWriteHistory, snapshot.DiskWriteBytesPerSecond);
        AddIfAvailable(NetworkReceiveHistory, snapshot.NetworkReceiveBytesPerSecond);
        AddIfAvailable(NetworkSendHistory, snapshot.NetworkSendBytesPerSecond);

        OnPropertyChanged(nameof(CpuValue));
        OnPropertyChanged(nameof(CpuDetails));
        OnPropertyChanged(nameof(MemoryValue));
        OnPropertyChanged(nameof(MemoryDetails));
        OnPropertyChanged(nameof(DiskReadValue));
        OnPropertyChanged(nameof(DiskWriteValue));
        OnPropertyChanged(nameof(DiskDetails));
        OnPropertyChanged(nameof(NetworkReceiveValue));
        OnPropertyChanged(nameof(NetworkSendValue));
        OnPropertyChanged(nameof(NetworkDetails));
    }

    private static void AddIfAvailable(ObservableCollection<double> history, MetricReading reading)
    {
        if (!reading.Value.HasValue)
        {
            return;
        }

        history.Add(reading.Value.Value);
        while (history.Count > HistoryLimit)
        {
            history.RemoveAt(0);
        }
    }

    private static string FormatPercent(MetricReading reading)
    {
        return reading.Value is { } value ? $"{value:N0}%" : "--";
    }

    private static string FormatRate(MetricReading reading)
    {
        return reading.Value is { } value ? $"{FormatBytesPerSecond(value)}/s" : "--";
    }

    private static string FormatBytesPerSecond(double bytesPerSecond)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, bytesPerSecond);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:N1} {units[unit]}";
    }

    private static string JoinDetails(MetricReading first, MetricReading second)
    {
        return string.Join(" ", new[] { first.Details, second.Details }
            .Where(details => !string.IsNullOrWhiteSpace(details))
            .Distinct(StringComparer.Ordinal));
    }
}
