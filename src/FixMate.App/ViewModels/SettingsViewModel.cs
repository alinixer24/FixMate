using System.Windows.Input;
using FixMate.Core.Interfaces;
using FixMate.Core.Models;

namespace FixMate.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private int _selectedRefreshIntervalSeconds;
    private bool _isSaving;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;

    public SettingsViewModel(AppSettings settings, IAppSettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _selectedRefreshIntervalSeconds = settings.MonitoringRefreshIntervalSeconds;
        SaveCommand = new AsyncRelayCommand(SaveAsync, HandleSaveError);
    }

    public IReadOnlyList<int> RefreshIntervalOptions { get; } = [1, 2, 5, 10];
    public ICommand SaveCommand { get; }
    public bool CanSave => !_isSaving;

    public int SelectedRefreshIntervalSeconds
    {
        get => _selectedRefreshIntervalSeconds;
        set
        {
            if (!RefreshIntervalOptions.Contains(value) || _selectedRefreshIntervalSeconds == value)
            {
                return;
            }

            _selectedRefreshIntervalSeconds = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    private async Task SaveAsync()
    {
        _isSaving = true;
        OnPropertyChanged(nameof(CanSave));
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var updatedSettings = new AppSettings
            {
                MonitoringRefreshIntervalSeconds = SelectedRefreshIntervalSeconds
            };
            await _settingsStore.SaveAsync(updatedSettings);
            _settings.MonitoringRefreshIntervalSeconds = updatedSettings.MonitoringRefreshIntervalSeconds;
            StatusMessage = "Settings saved. Monitoring will use the selected interval on its next sample.";
        }
        finally
        {
            _isSaving = false;
            OnPropertyChanged(nameof(CanSave));
        }
    }

    private void HandleSaveError(Exception exception)
    {
        ErrorMessage = $"Settings could not be saved: {exception.Message}";
    }
}
