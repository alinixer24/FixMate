using FixMate.App.ViewModels;

namespace FixMate.App.Navigation;

public sealed class NavigationService
{
    public event EventHandler? CurrentViewModelChanged;

    public ViewModelBase? CurrentViewModel { get; private set; }

    public void NavigateTo(ViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
