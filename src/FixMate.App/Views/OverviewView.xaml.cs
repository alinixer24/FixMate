using System.Windows.Controls;
using FixMate.App.ViewModels;

namespace FixMate.App.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is OverviewViewModel viewModel)
        {
            viewModel.Monitoring.Start();
            await viewModel.LoadStorageSummaryAsync();
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is OverviewViewModel viewModel)
        {
            viewModel.Monitoring.Stop();
        }
    }
}
