using System.Windows.Input;

namespace FixMate.App.ViewModels;

public sealed class NavigationItemViewModel(string title, string icon, ViewModelBase target, ICommand navigateCommand) : ViewModelBase
{
    private bool _isSelected;
    private bool _reduceAnimations;
    private bool _showTitle = true;

    public string Title { get; } = title;
    public string Icon { get; } = icon;
    public ViewModelBase Target { get; } = target;
    public ICommand NavigateCommand { get; } = navigateCommand;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool ReduceAnimations
    {
        get => _reduceAnimations;
        set => SetProperty(ref _reduceAnimations, value);
    }

    public bool ShowTitle
    {
        get => _showTitle;
        set => SetProperty(ref _showTitle, value);
    }
}
