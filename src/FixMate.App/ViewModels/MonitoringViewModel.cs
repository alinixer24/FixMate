using System.Collections.ObjectModel;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class MonitoringViewModel : ViewModelBase
{
    private const int MaximumHistorySamples = 3600;
    private readonly IMonitoringDataProvider _provider;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _sampleGate = new(1, 1);
    private CancellationTokenSource? _samplingCancellation;
    private bool _isPaused;
    private int _selectedRangeMinutes;
    private string _errorMessage = string.Empty;
    private string _lastUpdatedText = "Waiting for the first sample...";

    public MonitoringViewModel(IMonitoringDataProvider provider, AppSettings settings)
    {
        _provider = provider;
        _settings = settings;
        _selectedRangeMinutes = settings.GraphHistoryDurationMinutes;
        _settings.GraphHistoryDurationChanged += OnGraphHistoryDurationChanged;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleRefreshError);
        PauseResumeCommand = new RelayCommand(TogglePause);
        ClearHistoryCommand = new RelayCommand(ClearHistory);
    }

    public System.Windows.Input.ICommand RefreshCommand { get; }
    public System.Windows.Input.ICommand PauseResumeCommand { get; }
    public System.Windows.Input.ICommand ClearHistoryCommand { get; }
    public IReadOnlyList<int> RangeOptions => AppSettings.SupportedGraphHistoryDurations;
    public ObservableCollection<MonitoringSample> Samples { get; } = [];
    private Queue<DateTimeOffset> CpuSampleTimes { get; } = new();
    private Queue<DateTimeOffset> MemorySampleTimes { get; } = new();
    private Queue<DateTimeOffset> DiskReadSampleTimes { get; } = new();
    private Queue<DateTimeOffset> DiskWriteSampleTimes { get; } = new();
    private Queue<DateTimeOffset> NetworkReceiveSampleTimes { get; } = new();
    private Queue<DateTimeOffset> NetworkSendSampleTimes { get; } = new();
    public bool IsPaused => _isPaused;
    public string PauseResumeLabel => _isPaused ? "Resume monitoring" : "Pause monitoring";
    public int SelectedRangeMinutes
    {
        get => _selectedRangeMinutes;
        set
        {
            if (!RangeOptions.Contains(value) || _selectedRangeMinutes == value)
            {
                return;
            }

            _selectedRangeMinutes = value;
            OnPropertyChanged();
            RebuildVisibleHistory();
        }
    }

    public string CpuStatistics { get; private set; } = "No samples";
    public string MemoryStatistics { get; private set; } = "No samples";
    public string DiskReadStatistics { get; private set; } = "No samples";
    public string DiskWriteStatistics { get; private set; } = "No samples";
    public string NetworkReceiveStatistics { get; private set; } = "No samples";
    public string NetworkSendStatistics { get; private set; } = "No samples";

    public ObservableCollection<double> CpuHistory { get; } = [];
    public ObservableCollection<double> MemoryHistory { get; } = [];
    public ObservableCollection<double> DiskReadHistory { get; } = [];
    public ObservableCollection<double> DiskWriteHistory { get; } = [];
    public ObservableCollection<double> NetworkReceiveHistory { get; } = [];
    public ObservableCollection<double> NetworkSendHistory { get; } = [];

    public string CpuValue { get; private set; } = "--";
    public string CpuDetails { get; private set; } = "Waiting for a sample.";
    public string CpuThresholdStatus { get; private set; } = "Threshold unavailable until a CPU reading is collected.";
    public string MemoryValue { get; private set; } = "--";
    public string MemoryDetails { get; private set; } = "Waiting for a sample.";
    public string MemoryThresholdStatus { get; private set; } = "Threshold unavailable until a memory reading is collected.";
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
        if (_isPaused || _samplingCancellation is { IsCancellationRequested: false })
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

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseResumeLabel));
        if (_isPaused)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    private void ClearHistory()
    {
        Samples.Clear();
        CpuHistory.Clear();
        MemoryHistory.Clear();
        DiskReadHistory.Clear();
        DiskWriteHistory.Clear();
        NetworkReceiveHistory.Clear();
        NetworkSendHistory.Clear();
        CpuSampleTimes.Clear();
        MemorySampleTimes.Clear();
        DiskReadSampleTimes.Clear();
        DiskWriteSampleTimes.Clear();
        NetworkReceiveSampleTimes.Clear();
        NetworkSendSampleTimes.Clear();
        UpdateStatistics();
    }

    private void OnGraphHistoryDurationChanged(object? sender, EventArgs e)
    {
        if (RangeOptions.Contains(_settings.GraphHistoryDurationMinutes))
        {
            SelectedRangeMinutes = _settings.GraphHistoryDurationMinutes;
        }
    }

    private async Task SampleContinuouslyAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        ErrorMessage = string.Empty;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await SampleOnceAsync(token);
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

    private async Task RefreshAsync()
    {
        await SampleOnceAsync(CancellationToken.None);
    }

    private async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        await _sampleGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _provider.GetSnapshotAsync(cancellationToken);
            ApplySnapshot(snapshot);
            ErrorMessage = string.Empty;
        }
        finally
        {
            _sampleGate.Release();
        }
    }

    private void HandleRefreshError(Exception exception)
    {
        ErrorMessage = $"A fresh monitoring sample could not be collected: {exception.Message}";
    }

    private void ApplySnapshot(FixMate.Core.Results.MonitoringSnapshot snapshot)
    {
        CpuValue = FormatPercent(snapshot.CpuUsagePercent);
        CpuDetails = snapshot.CpuUsagePercent.Details;
        CpuThresholdStatus = FormatThresholdStatus(snapshot.CpuUsagePercent, _settings.CpuWarningThresholdPercent);
        MemoryValue = FormatPercent(snapshot.MemoryUsagePercent);
        MemoryDetails = snapshot.MemoryUsagePercent.Details;
        MemoryThresholdStatus = FormatThresholdStatus(snapshot.MemoryUsagePercent, _settings.MemoryWarningThresholdPercent);
        DiskReadValue = FormatRate(snapshot.DiskReadBytesPerSecond);
        DiskWriteValue = FormatRate(snapshot.DiskWriteBytesPerSecond);
        DiskDetails = JoinDetails(snapshot.DiskReadBytesPerSecond, snapshot.DiskWriteBytesPerSecond);
        NetworkReceiveValue = FormatRate(snapshot.NetworkReceiveBytesPerSecond);
        NetworkSendValue = FormatRate(snapshot.NetworkSendBytesPerSecond);
        NetworkDetails = JoinDetails(snapshot.NetworkReceiveBytesPerSecond, snapshot.NetworkSendBytesPerSecond);
        LastUpdatedText = $"Updated {snapshot.CollectedAt.ToLocalTime():T} · Refreshes every {_settings.MonitoringRefreshIntervalSeconds} second(s)";

        Samples.Add(new MonitoringSample(
            snapshot.CollectedAt,
            snapshot.CpuUsagePercent.Value,
            snapshot.MemoryUsagePercent.Value,
            snapshot.DiskReadBytesPerSecond.Value,
            snapshot.DiskWriteBytesPerSecond.Value,
            snapshot.NetworkReceiveBytesPerSecond.Value,
            snapshot.NetworkSendBytesPerSecond.Value));
        while (Samples.Count > MaximumHistorySamples)
        {
            Samples.RemoveAt(0);
        }

        AppendIfAvailable(CpuHistory, CpuSampleTimes, snapshot.CpuUsagePercent, snapshot.CollectedAt);
        AppendIfAvailable(MemoryHistory, MemorySampleTimes, snapshot.MemoryUsagePercent, snapshot.CollectedAt);
        AppendIfAvailable(DiskReadHistory, DiskReadSampleTimes, snapshot.DiskReadBytesPerSecond, snapshot.CollectedAt);
        AppendIfAvailable(DiskWriteHistory, DiskWriteSampleTimes, snapshot.DiskWriteBytesPerSecond, snapshot.CollectedAt);
        AppendIfAvailable(NetworkReceiveHistory, NetworkReceiveSampleTimes, snapshot.NetworkReceiveBytesPerSecond, snapshot.CollectedAt);
        AppendIfAvailable(NetworkSendHistory, NetworkSendSampleTimes, snapshot.NetworkSendBytesPerSecond, snapshot.CollectedAt);
        TrimVisibleHistory();
        UpdateStatistics();

        OnPropertyChanged(nameof(CpuValue));
        OnPropertyChanged(nameof(CpuDetails));
        OnPropertyChanged(nameof(CpuThresholdStatus));
        OnPropertyChanged(nameof(MemoryValue));
        OnPropertyChanged(nameof(MemoryDetails));
        OnPropertyChanged(nameof(MemoryThresholdStatus));
        OnPropertyChanged(nameof(DiskReadValue));
        OnPropertyChanged(nameof(DiskWriteValue));
        OnPropertyChanged(nameof(DiskDetails));
        OnPropertyChanged(nameof(NetworkReceiveValue));
        OnPropertyChanged(nameof(NetworkSendValue));
        OnPropertyChanged(nameof(NetworkDetails));
    }

    private static void AppendIfAvailable(ObservableCollection<double> history, Queue<DateTimeOffset> sampleTimes, MetricReading reading, DateTimeOffset collectedAt)
    {
        if (reading.Value is { } value)
        {
            history.Add(value);
            sampleTimes.Enqueue(collectedAt);
        }
    }

    private void RebuildVisibleHistory()
    {
        CpuHistory.Clear();
        MemoryHistory.Clear();
        DiskReadHistory.Clear();
        DiskWriteHistory.Clear();
        NetworkReceiveHistory.Clear();
        NetworkSendHistory.Clear();

        var cutoff = DateTimeOffset.Now.AddMinutes(-SelectedRangeMinutes);
        foreach (var sample in Samples.Where(sample => sample.CollectedAt >= cutoff))
        {
            AddIfAvailable(CpuHistory, CpuSampleTimes, sample.CpuPercent, sample.CollectedAt);
            AddIfAvailable(MemoryHistory, MemorySampleTimes, sample.MemoryPercent, sample.CollectedAt);
            AddIfAvailable(DiskReadHistory, DiskReadSampleTimes, sample.DiskReadBytesPerSecond, sample.CollectedAt);
            AddIfAvailable(DiskWriteHistory, DiskWriteSampleTimes, sample.DiskWriteBytesPerSecond, sample.CollectedAt);
            AddIfAvailable(NetworkReceiveHistory, NetworkReceiveSampleTimes, sample.NetworkReceiveBytesPerSecond, sample.CollectedAt);
            AddIfAvailable(NetworkSendHistory, NetworkSendSampleTimes, sample.NetworkSendBytesPerSecond, sample.CollectedAt);
        }

        UpdateStatistics();
    }

    private void TrimVisibleHistory()
    {
        var cutoff = DateTimeOffset.Now.AddMinutes(-SelectedRangeMinutes);
        TrimSeries(CpuHistory, CpuSampleTimes, cutoff);
        TrimSeries(MemoryHistory, MemorySampleTimes, cutoff);
        TrimSeries(DiskReadHistory, DiskReadSampleTimes, cutoff);
        TrimSeries(DiskWriteHistory, DiskWriteSampleTimes, cutoff);
        TrimSeries(NetworkReceiveHistory, NetworkReceiveSampleTimes, cutoff);
        TrimSeries(NetworkSendHistory, NetworkSendSampleTimes, cutoff);
    }

    private static void TrimSeries(ObservableCollection<double> history, Queue<DateTimeOffset> sampleTimes, DateTimeOffset cutoff)
    {
        while (sampleTimes.TryPeek(out var oldest) && oldest < cutoff)
        {
            sampleTimes.Dequeue();
            history.RemoveAt(0);
        }
    }

    private static void AddIfAvailable(ObservableCollection<double> history, Queue<DateTimeOffset> sampleTimes, double? value, DateTimeOffset collectedAt)
    {
        if (value is { } available)
        {
            history.Add(available);
            sampleTimes.Enqueue(collectedAt);
        }
    }

    private void UpdateStatistics()
    {
        var cutoff = DateTimeOffset.Now.AddMinutes(-SelectedRangeMinutes);
        var samples = Samples.Where(sample => sample.CollectedAt >= cutoff).ToArray();
        CpuStatistics = FormatStatistics(samples.Select(sample => sample.CpuPercent));
        MemoryStatistics = FormatStatistics(samples.Select(sample => sample.MemoryPercent));
        DiskReadStatistics = FormatStatistics(samples.Select(sample => sample.DiskReadBytesPerSecond), perSecond: true);
        DiskWriteStatistics = FormatStatistics(samples.Select(sample => sample.DiskWriteBytesPerSecond), perSecond: true);
        NetworkReceiveStatistics = FormatStatistics(samples.Select(sample => sample.NetworkReceiveBytesPerSecond), perSecond: true);
        NetworkSendStatistics = FormatStatistics(samples.Select(sample => sample.NetworkSendBytesPerSecond), perSecond: true);
        OnPropertyChanged(nameof(CpuStatistics));
        OnPropertyChanged(nameof(MemoryStatistics));
        OnPropertyChanged(nameof(DiskReadStatistics));
        OnPropertyChanged(nameof(DiskWriteStatistics));
        OnPropertyChanged(nameof(NetworkReceiveStatistics));
        OnPropertyChanged(nameof(NetworkSendStatistics));
    }

    private static string FormatStatistics(IEnumerable<double?> values, bool perSecond = false)
    {
        var available = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (available.Length == 0)
        {
            return "No samples in this range";
        }

        return $"Min {FormatStatistic(available.Min(), perSecond)} · Max {FormatStatistic(available.Max(), perSecond)} · Avg {FormatStatistic(available.Average(), perSecond)}";
    }

    private static string FormatStatistic(double value, bool perSecond)
    {
        return perSecond ? $"{FormatBytesPerSecond(value)}/s" : $"{value:N1}%";
    }

    private static string FormatThresholdStatus(MetricReading reading, int threshold)
    {
        return reading.Value is { } value
            ? value >= threshold ? $"Warning: {value:N0}% meets/exceeds {threshold}% threshold" : $"Below {threshold}% warning threshold"
            : $"Unavailable: {reading.Details}";
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
