namespace FixMate.App.ViewModels;

public sealed class ReportSectionOption(string key, string title, string description) : ViewModelBase
{
    private bool _isIncluded = true;

    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Description { get; } = description;

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }
}
