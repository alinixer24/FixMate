using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;
using Microsoft.Win32;

namespace FixMate.App.ViewModels;

public sealed class SoftwareCenterViewModel : ViewModelBase
{
    private readonly IMaintenanceProvider _provider;
    private string _searchText = string.Empty;
    private string _status = "Installed software has not been loaded.";
    private string _errorMessage = string.Empty;

    public SoftwareCenterViewModel(IMaintenanceProvider provider)
    {
        _provider = provider;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, HandleError);
        ExportCommand = new RelayCommand(ExportCsv);
        OpenLocationCommand = new ParameterizedRelayCommand(OpenLocation);
        OpenWebsiteCommand = new ParameterizedRelayCommand(OpenPublisherWebsite);
    }

    public ObservableCollection<InstalledApplication> Applications { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand OpenLocationCommand { get; }
    public ICommand OpenWebsiteCommand { get; }

    public IEnumerable<InstalledApplication> FilteredApplications
    {
        get
        {
            var query = SearchText.Trim();
            return string.IsNullOrEmpty(query)
                ? Applications
                : Applications.Where(application =>
                    application.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    application.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    application.Version.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(FilteredApplications));
            }
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public async Task LoadAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
    }

    private async Task RefreshAsync()
    {
        ErrorMessage = string.Empty;
        var applications = await _provider.GetInstalledApplicationsAsync();
        Applications.Clear();
        foreach (var application in applications)
        {
            Applications.Add(application);
        }

        Status = applications.Count == 0
            ? "No applications were found in the standard Windows uninstall registry locations."
            : $"Found {applications.Count:N0} application record(s). Version/date availability depends on each publisher's Windows registry entry; no update status is inferred.";
        OnPropertyChanged(nameof(FilteredApplications));
    }

    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export installed software inventory",
            FileName = $"FixMate-software-inventory-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            DefaultExt = ".csv",
            Filter = "CSV file (*.csv)|*.csv"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var builder = new StringBuilder().AppendLine("Name,Version,Publisher,InstallDate,VersionAvailable,RegistryView,InstallLocation,PublisherWebsite");
        foreach (var app in FilteredApplications)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(app.Name), Csv(app.Version), Csv(app.Publisher), Csv(app.InstallDate?.ToString("yyyy-MM-dd") ?? string.Empty),
                Csv(app.HasVersionInformation ? "Yes" : "No"), Csv(app.RegistryView), Csv(app.InstallLocation), Csv(app.PublisherWebsite)
            }));
        }

        try
        {
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Status = $"Exported {FilteredApplications.Count():N0} application record(s) to {dialog.FileName}.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            HandleError(exception);
        }
    }

    private void OpenLocation(object? parameter)
    {
        if (parameter is not InstalledApplication app)
        {
            throw new ArgumentException("An installed application record is required.", nameof(parameter));
        }

        var location = GetExistingLocation(app);
        if (location is null)
        {
            ErrorMessage = $"A verified installation folder is not available for {app.Name}.";
            return;
        }

        try
        {
            if (Process.Start(new ProcessStartInfo("explorer.exe", $"\"{location}\"") { UseShellExecute = true }) is null)
            {
                throw new InvalidOperationException("Windows did not open the selected location.");
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            HandleError(exception);
        }
    }

    private void OpenPublisherWebsite(object? parameter)
    {
        if (parameter is not InstalledApplication app ||
            !Uri.TryCreate(app.PublisherWebsite, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            ErrorMessage = "No valid publisher website was provided by this application's Windows record.";
            return;
        }

        try
        {
            if (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }) is null)
            {
                throw new InvalidOperationException("Windows did not open the publisher website.");
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            HandleError(exception);
        }
    }

    private static string? GetExistingLocation(InstalledApplication app)
    {
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
        {
            return app.InstallLocation;
        }

        if (string.IsNullOrWhiteSpace(app.DisplayIcon))
        {
            return null;
        }

        var iconPath = app.DisplayIcon.Trim();
        if (iconPath.StartsWith('"'))
        {
            var endQuote = iconPath.IndexOf('"', 1);
            iconPath = endQuote > 1 ? iconPath[1..endQuote] : string.Empty;
        }
        else
        {
            var comma = iconPath.LastIndexOf(',');
            if (comma >= 0)
            {
                iconPath = iconPath[..comma].Trim();
            }
        }

        return File.Exists(iconPath) ? Path.GetDirectoryName(iconPath) : null;
    }

    private void HandleError(Exception exception)
    {
        ErrorMessage = $"Software Center operation could not complete: {exception.Message}";
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
