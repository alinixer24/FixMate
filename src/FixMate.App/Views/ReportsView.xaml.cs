using System.Windows.Controls;
using FixMate.App.ViewModels;

namespace FixMate.App.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ReportsViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
