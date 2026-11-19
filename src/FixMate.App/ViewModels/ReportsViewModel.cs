using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Documents;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using Microsoft.Win32;

namespace FixMate.App.ViewModels;

public sealed class ReportsViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] ReportTypes =
    [
        "Complete system report", "Hardware report", "Diagnostic report", "Storage report",
        "Network report", "Windows error report", "Monitoring session report", "Maintenance history report"
    ];

    private readonly ISystemInformationProvider _systemInformationProvider;
    private readonly IMaintenanceProvider _maintenanceProvider;
    private readonly DiagnosticsViewModel _diagnostics;
    private readonly IWindowsEventLogProvider _eventLogProvider;
    private readonly INetworkDiagnosticsService _networkService;
    private readonly INetworkHistoryStore _networkHistoryStore;
    private readonly IMaintenanceHistoryStore _maintenanceHistoryStore;
    private readonly IMaintenanceReminderStore _reminderStore;
    private readonly MonitoringViewModel _monitoring;
    private readonly string _historyDirectory;
    private string _selectedReportType = ReportTypes[0];
    private string _previewText = "Select report sections and choose Preview to collect the latest available information.";
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ReportsViewModel(
        ISystemInformationProvider systemInformationProvider,
        IMaintenanceProvider maintenanceProvider,
        DiagnosticsViewModel diagnostics,
        IWindowsEventLogProvider eventLogProvider,
        INetworkDiagnosticsService networkService,
        INetworkHistoryStore networkHistoryStore,
        IMaintenanceHistoryStore maintenanceHistoryStore,
        IMaintenanceReminderStore reminderStore,
        MonitoringViewModel monitoring)
    {
        _systemInformationProvider = systemInformationProvider;
        _maintenanceProvider = maintenanceProvider;
        _diagnostics = diagnostics;
        _eventLogProvider = eventLogProvider;
        _networkService = networkService;
        _networkHistoryStore = networkHistoryStore;
        _maintenanceHistoryStore = maintenanceHistoryStore;
        _reminderStore = reminderStore;
        _monitoring = monitoring;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            throw new InvalidOperationException("The current user's local application data folder is unavailable.");
        }

        _historyDirectory = Path.Combine(appData, "FixMate", "Reports");
        ReportSections = [];
        UpdateSections();
        PreviewCommand = new AsyncRelayCommand(GeneratePreviewAsync, HandleError);
        ExportHtmlCommand = new AsyncRelayCommand(() => ExportAsync("html"), HandleError);
        ExportJsonCommand = new AsyncRelayCommand(() => ExportAsync("json"), HandleError);
        ExportCsvCommand = new AsyncRelayCommand(() => ExportAsync("csv"), HandleError);
        PrintCommand = new AsyncRelayCommand(PrintAsync, HandleError);
        RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync, HandleError);
    }

    public ObservableCollection<string> AvailableReportTypes { get; } = new(ReportTypes);
    public ObservableCollection<ReportSectionOption> ReportSections { get; }
    public ObservableCollection<string> ReportHistory { get; } = [];
    public ICommand PreviewCommand { get; }
    public ICommand ExportHtmlCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand RefreshHistoryCommand { get; }

    public string SelectedReportType
    {
        get => _selectedReportType;
        set
        {
            if (SetProperty(ref _selectedReportType, value))
            {
                UpdateSections();
                PreviewText = $"{value}: select sections and choose Preview to collect the latest available information.";
            }
        }
    }

    public string PreviewText { get => _previewText; private set => SetProperty(ref _previewText, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool CanRun => !_isBusy;

    public async Task LoadAsync()
    {
        try
        {
            await RefreshHistoryAsync();
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
    }

    private void UpdateSections()
    {
        var keys = SelectedReportType switch
        {
            "Hardware report" => new[] { "system", "hardware" },
            "Diagnostic report" => new[] { "diagnostics" },
            "Storage report" => new[] { "storage" },
            "Network report" => new[] { "network" },
            "Windows error report" => new[] { "events" },
            "Monitoring session report" => new[] { "monitoring" },
            "Maintenance history report" => new[] { "maintenance" },
            _ => new[] { "system", "hardware", "storage", "software", "diagnostics", "network", "events", "monitoring", "maintenance" }
        };

        var definitions = new Dictionary<string, (string Title, string Description)>
        {
            ["system"] = ("Windows and device", "OS name, version and computer name"),
            ["hardware"] = ("Hardware", "CPU, GPU, memory and detected components"),
            ["storage"] = ("Storage", "Ready local volume capacities"),
            ["software"] = ("Installed software", "Windows uninstall registry inventory"),
            ["diagnostics"] = ("Diagnostics", "Most recent available check results; no tests are run to create a report"),
            ["network"] = ("Network adapters", "Local interface configuration; no external request is made"),
            ["events"] = ("Windows events", "Recent errors and warnings from Windows event logs"),
            ["monitoring"] = ("Monitoring session", "Current in-memory CPU, memory, disk and network history"),
            ["maintenance"] = ("Maintenance history", "Availability of persisted maintenance operation history")
        };

        ReportSections.Clear();
        foreach (var key in keys)
        {
            var definition = definitions[key];
            ReportSections.Add(new ReportSectionOption(key, definition.Title, definition.Description));
        }
    }

    private async Task GeneratePreviewAsync()
    {
        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var document = await CollectReportAsync();
            PreviewText = FormatPreview(document);
            StatusMessage = $"Preview built at {document.GeneratedAt.ToLocalTime():G}. Unsupported or unavailable sources are identified in the report.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ExportAsync(string format)
    {
        var dialog = new SaveFileDialog
        {
            Title = $"Export {SelectedReportType}",
            FileName = $"FixMate-{SafeFilePart(SelectedReportType)}-{DateTime.Now:yyyyMMdd-HHmmss}.{format}",
            DefaultExt = $".{format}",
            Filter = format switch
            {
                "html" => "HTML report (*.html)|*.html",
                "json" => "JSON report (*.json)|*.json",
                _ => "CSV report (*.csv)|*.csv"
            }
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var document = await CollectReportAsync();
            var content = format switch
            {
                "html" => ToHtml(document),
                "json" => JsonSerializer.Serialize(document, JsonOptions),
                _ => ToCsv(document)
            };
            await File.WriteAllTextAsync(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            PreviewText = FormatPreview(document);
            StatusMessage = $"Exported {document.Rows.Count:N0} report item(s) to {dialog.FileName}.";
            await RefreshHistoryAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task PrintAsync()
    {
        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var document = await CollectReportAsync();
            var printDialog = new System.Windows.Controls.PrintDialog();
            if (printDialog.ShowDialog() != true)
            {
                return;
            }

            var flowDocument = new FlowDocument { PagePadding = new System.Windows.Thickness(48), ColumnWidth = printDialog.PrintableAreaWidth };
            flowDocument.Blocks.Add(new Paragraph(new Run($"{document.ReportType} · {document.GeneratedAt.ToLocalTime():G}"))
            {
                FontSize = 18,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Margin = new System.Windows.Thickness(0, 0, 0, 14)
            });
            foreach (var row in document.Rows)
            {
                flowDocument.Blocks.Add(new Paragraph(new Run($"{row.Section} · {row.Name} [{row.Status}]: {row.Value}"))
                {
                    Margin = new System.Windows.Thickness(0, 0, 0, 8)
                });
            }

            printDialog.PrintDocument(((IDocumentPaginatorSource)flowDocument).DocumentPaginator, "FixMate report");
            StatusMessage = "Report sent to the selected printer.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<ReportDocument> CollectReportAsync()
    {
        var selectedSections = ReportSections.Where(section => section.IsIncluded).Select(section => section.Key).ToHashSet(StringComparer.Ordinal);
        if (selectedSections.Count == 0)
        {
            throw new InvalidOperationException("Select at least one report section.");
        }

        var rows = new List<ReportRow>();
        foreach (var key in selectedSections)
        {
            switch (key)
            {
                case "system":
                case "hardware":
                    await AddSystemInformationAsync(rows, key);
                    break;
                case "storage":
                    await AddStorageAsync(rows);
                    break;
                case "software":
                    await AddSoftwareAsync(rows);
                    break;
                case "diagnostics":
                    AddDiagnostics(rows);
                    break;
                case "network":
                    await AddNetworkAsync(rows);
                    break;
                case "events":
                    await AddEventsAsync(rows);
                    break;
                case "monitoring":
                    AddMonitoring(rows);
                    break;
                case "maintenance":
                    await AddMaintenanceHistoryAsync(rows);
                    break;
            }
        }

        return new ReportDocument(SelectedReportType, DateTimeOffset.Now, Environment.MachineName, rows);
    }

    private async Task AddSystemInformationAsync(ICollection<ReportRow> rows, string section)
    {
        var snapshot = await _systemInformationProvider.GetSystemInformationAsync();
        if (section == "system")
        {
            rows.Add(new ReportRow("Windows", "Computer name", snapshot.OperatingSystem.MachineName, "Reported"));
            rows.Add(new ReportRow("Windows", "Operating system", snapshot.OperatingSystem.Name, snapshot.OperatingSystem.Status.ToString()));
            rows.Add(new ReportRow("Windows", "Version and architecture", $"{snapshot.OperatingSystem.Version} · {snapshot.OperatingSystem.Architecture}", snapshot.OperatingSystem.Status.ToString()));
            rows.Add(new ReportRow("Windows", "OS details", snapshot.OperatingSystem.Details, snapshot.OperatingSystem.Status.ToString()));
        }

        if (section == "hardware")
        {
            foreach (var component in snapshot.Components)
            {
                rows.Add(new ReportRow(component.Category, component.Name, $"{component.Manufacturer} {component.Model} {component.Details}".Trim(), component.Status.ToString()));
            }

            if (snapshot.Components.Count == 0)
            {
                rows.Add(new ReportRow("Hardware", "Components", "Windows returned no hardware component records.", "Unavailable"));
            }
        }
    }

    private async Task AddStorageAsync(ICollection<ReportRow> rows)
    {
        var volumes = await _maintenanceProvider.GetStorageVolumesAsync();
        if (volumes.Count == 0)
        {
            rows.Add(new ReportRow("Storage", "Volumes", "No ready fixed or removable volumes were reported.", "Unavailable"));
            return;
        }

        foreach (var volume in volumes)
        {
            rows.Add(new ReportRow("Storage", volume.RootPath, $"{volume.FileSystem}; total {volume.TotalBytes:N0} bytes; available {volume.AvailableBytes:N0} bytes", "Reported"));
        }
    }

    private async Task AddSoftwareAsync(ICollection<ReportRow> rows)
    {
        var applications = await _maintenanceProvider.GetInstalledApplicationsAsync();
        foreach (var app in applications)
        {
            rows.Add(new ReportRow("Installed software", app.Name, $"Version: {(app.HasVersionInformation ? app.Version : "Unavailable")}; publisher: {app.Publisher}; install date: {app.InstallDate?.ToString("d") ?? "Unavailable"}", "Registry record"));
        }

        if (applications.Count == 0)
        {
            rows.Add(new ReportRow("Installed software", "Inventory", "No standard Windows uninstall entries were found.", "Unavailable"));
        }
    }

    private void AddDiagnostics(ICollection<ReportRow> rows)
    {
        var results = _diagnostics.Results;
        if (results.Count == 0)
        {
            rows.Add(new ReportRow("Diagnostics", "Current results", "No diagnostic results are available.", "Unavailable"));
            return;
        }

        foreach (var result in results)
        {
            rows.Add(new ReportRow("Diagnostics", result.TestName, result.Summary, result.Status.ToString()));
        }
    }

    private async Task AddNetworkAsync(ICollection<ReportRow> rows)
    {
        var adapters = await _networkService.GetAdaptersAsync();
        foreach (var adapter in adapters)
        {
            rows.Add(new ReportRow("Network", adapter.Name, $"{adapter.ConnectionType}; {adapter.Status}; MAC {adapter.MacAddress}; IP {string.Join(", ", adapter.IpAddresses)}; gateways {string.Join(", ", adapter.Gateways)}; DNS {string.Join(", ", adapter.DnsServers)}; DHCP {(adapter.DhcpEnabled?.ToString() ?? "Unavailable")}", "Local adapter data"));
        }

        var history = await _networkHistoryStore.GetHistoryAsync();
        foreach (var result in history)
        {
            rows.Add(new ReportRow("Network history", $"{result.CompletedAt.ToLocalTime():g} · {result.Tool} · {result.Target}", result.Summary, result.Succeeded ? "Succeeded" : "No response / failed"));
        }

        if (history.Count == 0)
        {
            rows.Add(new ReportRow("Network history", "Connectivity tests", "No saved connectivity tests are available.", "Unavailable"));
        }

        if (adapters.Count == 0)
        {
            rows.Add(new ReportRow("Network", "Adapters", "Windows did not report network adapters.", "Unavailable"));
        }
    }

    private async Task AddMaintenanceHistoryAsync(ICollection<ReportRow> rows)
    {
        var operationHistory = await _maintenanceHistoryStore.GetHistoryAsync();
        foreach (var entry in operationHistory)
        {
            rows.Add(new ReportRow(
                "Maintenance operation",
                $"{entry.CompletedAt.ToLocalTime():g} · {entry.Operation}",
                entry.Details,
                entry.Outcome));
        }

        var reminderHistory = await _reminderStore.GetHistoryAsync();
        foreach (var entry in reminderHistory)
        {
            rows.Add(new ReportRow(
                "Reminder completion",
                entry.ReminderName,
                $"Completed {entry.CompletedAt.ToLocalTime():g}; next due {(entry.NextDueAt?.ToLocalTime().ToString("g") ?? "Not scheduled")}",
                "Recorded"));
        }

        if (operationHistory.Count == 0 && reminderHistory.Count == 0)
        {
            rows.Add(new ReportRow("Maintenance history", "History", "No maintenance operations or reminder completions have been recorded.", "Unavailable"));
        }
    }

    private async Task AddEventsAsync(ICollection<ReportRow> rows)
    {
        var events = await _eventLogProvider.GetRecentEventsAsync(200);
        foreach (var entry in events)
        {
            rows.Add(new ReportRow("Windows event", $"{entry.TimeCreated?.ToLocalTime():g} · {entry.Provider} · {entry.EventId}", entry.Message, entry.Level));
        }

        if (events.Count == 0)
        {
            rows.Add(new ReportRow("Windows events", "Recent events", "No events were returned from the queried logs.", "Unavailable"));
        }
    }

    private void AddMonitoring(ICollection<ReportRow> rows)
    {
        AddHistory(rows, "CPU", _monitoring.CpuHistory);
        AddHistory(rows, "Memory", _monitoring.MemoryHistory);
        AddHistory(rows, "Disk read bytes/sec", _monitoring.DiskReadHistory);
        AddHistory(rows, "Disk write bytes/sec", _monitoring.DiskWriteHistory);
        AddHistory(rows, "Network receive bytes/sec", _monitoring.NetworkReceiveHistory);
        AddHistory(rows, "Network send bytes/sec", _monitoring.NetworkSendHistory);
    }

    private static void AddHistory(ICollection<ReportRow> rows, string metric, IEnumerable<double> values)
    {
        var samples = values.ToArray();
        rows.Add(samples.Length == 0
            ? new ReportRow("Monitoring", metric, "No available samples were collected in this application session.", "Unavailable")
            : new ReportRow("Monitoring", metric, $"{samples.Length} samples; min {samples.Min():N2}; max {samples.Max():N2}; average {samples.Average():N2}; latest {samples[^1]:N2}", "Session samples"));
    }

    private async Task RefreshHistoryAsync()
    {
        Directory.CreateDirectory(_historyDirectory);
        var entries = await Task.Run(() => Directory.EnumerateFiles(_historyDirectory)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Take(30)
            .Select(file => $"{file.CreationTime.ToLocalTime():g} · {file.Name} · {file.Length:N0} bytes")
            .ToArray());
        ReportHistory.Clear();
        foreach (var entry in entries)
        {
            ReportHistory.Add(entry);
        }
    }

    private void HandleError(Exception exception)
    {
        ErrorMessage = $"Report operation could not complete: {exception.Message}";
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanRun));
    }

    private static string FormatPreview(ReportDocument document)
    {
        var builder = new StringBuilder()
            .AppendLine(document.ReportType)
            .AppendLine($"Generated: {document.GeneratedAt.ToLocalTime():G}")
            .AppendLine($"Computer: {document.ComputerName}")
            .AppendLine("Unavailable information and diagnostic states are labelled; no test is implied by missing data.")
            .AppendLine();
        foreach (var row in document.Rows)
        {
            builder.AppendLine($"[{row.Status}] {row.Section} · {row.Name}: {row.Value}");
        }

        return builder.ToString();
    }

    private static string ToHtml(ReportDocument document)
    {
        var builder = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>FixMate report</title><style>body{font-family:Segoe UI,Arial;background:#111318;color:#f1f3f7;margin:32px}h1{color:#4d8dff}table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:9px;border-bottom:1px solid #343a46}th{color:#9ca5b4}</style></head><body>")
            .Append("<h1>").Append(HtmlEncoder.Default.Encode(document.ReportType)).Append("</h1><p>Generated: ")
            .Append(HtmlEncoder.Default.Encode(document.GeneratedAt.ToLocalTime().ToString("G"))).Append(" · Computer: ")
            .Append(HtmlEncoder.Default.Encode(document.ComputerName)).Append("</p><p>Unavailable information and diagnostic states are labelled; no test is implied by missing data.</p><table><thead><tr><th>Section</th><th>Name</th><th>Value</th><th>Status</th></tr></thead><tbody>");
        foreach (var row in document.Rows)
        {
            builder.Append("<tr><td>").Append(HtmlEncoder.Default.Encode(row.Section)).Append("</td><td>")
                .Append(HtmlEncoder.Default.Encode(row.Name)).Append("</td><td>")
                .Append(HtmlEncoder.Default.Encode(row.Value)).Append("</td><td>")
                .Append(HtmlEncoder.Default.Encode(row.Status)).Append("</td></tr>");
        }

        return builder.Append("</tbody></table></body></html>").ToString();
    }

    private static string ToCsv(ReportDocument document)
    {
        var builder = new StringBuilder("GeneratedAt,ComputerName,ReportType,Section,Name,Value,Status\r\n");
        foreach (var row in document.Rows)
        {
            builder.AppendJoin(',', new[]
            {
                Csv(document.GeneratedAt.ToString("O")), Csv(document.ComputerName), Csv(document.ReportType),
                Csv(row.Section), Csv(row.Name), Csv(row.Value), Csv(row.Status)
            }).Append("\r\n");
        }

        return builder.ToString();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string SafeFilePart(string value) => string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    private sealed record ReportDocument(string ReportType, DateTimeOffset GeneratedAt, string ComputerName, IReadOnlyList<ReportRow> Rows);
    private sealed record ReportRow(string Section, string Name, string Value, string Status);
}
