using System.Windows.Controls;
using FixMate.App.ViewModels;

namespace FixMate.App.Views;

public partial class SoftwareCenterView : UserControl
{
    public SoftwareCenterView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SoftwareCenterViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
