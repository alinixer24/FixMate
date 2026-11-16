using System.Windows.Controls;
using FixMate.App.ViewModels;

namespace FixMate.App.Views;

public partial class ErrorCenterView : UserControl
{
    public ErrorCenterView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ErrorCenterViewModel viewModel)
        {
            await viewModel.LoadInitialDataAsync();
        }
    }
}
