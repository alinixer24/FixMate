using System.Windows.Controls;
using FixMate.App.ViewModels;

namespace FixMate.App.Views;

public partial class MonitoringView : UserControl
{
    public MonitoringView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MonitoringViewModel viewModel)
        {
            viewModel.Start();
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MonitoringViewModel viewModel)
        {
            viewModel.Stop();
        }
    }
}
