using System.Windows.Controls;
using FixMate.App.ViewModels;

namespace FixMate.App.Views;

public partial class NetworkCenterView : UserControl
{
    public NetworkCenterView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is NetworkCenterViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
