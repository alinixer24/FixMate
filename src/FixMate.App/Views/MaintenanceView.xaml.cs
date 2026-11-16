using System.Windows.Controls;
using FixMate.App.ViewModels;

namespace FixMate.App.Views;

public partial class MaintenanceView : UserControl
{
    public MaintenanceView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MaintenanceViewModel viewModel)
        {
            await viewModel.LoadInitialDataAsync();
        }
    }
}
