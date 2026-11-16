using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class DiagnosticsViewModel : ViewModelBase
{
    private readonly IDiagnosticsProvider _provider;
    private bool _isBusy;
    private string _errorMessage = string.Empty;

    public DiagnosticsViewModel(IDiagnosticsProvider provider)
    {
        _provider = provider;
        RunDiagnosticsCommand = new AsyncRelayCommand(RunDiagnosticsAsync, HandleError);
        RepairCommand = new AsyncRelayCommand(RunRepairAsync, HandleRepairError);
        foreach (var area in Enum.GetValues<DiagnosticArea>())
        {
            Results.Add(new DiagnosticResult
            {
                Area = area,
                TestName = GetTestName(area),
                Status = DiagnosticStatus.NotRun,
                Summary = "Not checked yet."
            });
        }
    }

    public ObservableCollection<DiagnosticResult> Results { get; } = [];
    public ICommand RunDiagnosticsCommand { get; }
    public ICommand RepairCommand { get; }

    public bool CanStartOperation => !_isBusy;
    public string OperationStatusText => _isBusy ? "Working..." : string.Empty;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        SetBusy(true);
        ErrorMessage = string.Empty;
        foreach (var result in Results.ToArray())
        {
            ReplaceResult(result.Area, DiagnosticStatus.Running, "Check is running...");
        }

        try
        {
            var results = await _provider.RunDiagnosticsAsync();
            Results.Clear();
            foreach (var result in results)
            {
                Results.Add(result);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunRepairAsync(object? parameter)
    {
        if (parameter is not DiagnosticArea area)
        {
            throw new ArgumentException("A valid diagnostic area is required.", nameof(parameter));
        }

        var (action, warning) = GetRepairPrompt(area);
        var confirmation = MessageBox.Show(
            $"{action}\n\n{warning}\n\nContinue?",
            "Confirm repair",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        ErrorMessage = string.Empty;
        try
        {
            var summary = await _provider.RunRepairAsync(area);
            var status = summary.Contains("Failed", StringComparison.OrdinalIgnoreCase)
                ? DiagnosticStatus.Failed
                : DiagnosticStatus.Passed;
            ReplaceResult(area, status, $"Repair action: {summary}");
        }
        catch (Exception exception)
        {
            ReplaceResult(area, DiagnosticStatus.Failed, $"Repair action could not complete: {exception.Message}");
            throw;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void HandleError(Exception exception)
    {
        ErrorMessage = $"Diagnostics could not complete: {exception.Message}";
        foreach (var result in Results.Where(result => result.Status == DiagnosticStatus.Running).ToArray())
        {
            ReplaceResult(result.Area, DiagnosticStatus.Inconclusive, ErrorMessage);
        }
    }

    private void HandleRepairError(Exception exception)
    {
        ErrorMessage = $"The repair action could not complete: {exception.Message}";
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        OnPropertyChanged(nameof(CanStartOperation));
        OnPropertyChanged(nameof(OperationStatusText));
    }

    private void ReplaceResult(DiagnosticArea area, DiagnosticStatus status, string summary)
    {
        var index = Results.ToList().FindIndex(result => result.Area == area);
        if (index >= 0)
        {
            Results[index] = new DiagnosticResult
            {
                Area = area,
                TestName = GetTestName(area),
                Status = status,
                Summary = summary,
                CompletedAt = status == DiagnosticStatus.Running ? null : DateTimeOffset.Now
            };
        }
    }

    private static (string Action, string Warning) GetRepairPrompt(DiagnosticArea area) => area switch
    {
        DiagnosticArea.Network => (
            "Flush the Windows DNS resolver cache",
            "This clears cached DNS lookups but does not change network settings."),
        DiagnosticArea.Storage => (
            "Run CHKDSK online scans on all ready fixed drives",
            "The scan can take several minutes and may apply online file-system corrections. Administrator approval is required."),
        DiagnosticArea.WindowsIntegrity => (
            "Run DISM RestoreHealth followed by SFC /scannow",
            "These tools can modify protected Windows system files and may take a long time. Administrator approval is required."),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown diagnostic area.")
    };

    private static string GetTestName(DiagnosticArea area) => area switch
    {
        DiagnosticArea.Network => "Network connectivity",
        DiagnosticArea.Storage => "Storage health",
        DiagnosticArea.WindowsIntegrity => "Windows integrity",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown diagnostic area.")
    };
}
