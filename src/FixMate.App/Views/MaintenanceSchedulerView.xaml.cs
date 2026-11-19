using System.Windows.Controls;
using FixMate.App.ViewModels;

namespace FixMate.App.Views;

public partial class MaintenanceSchedulerView : UserControl
{
    public MaintenanceSchedulerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MaintenanceSchedulerViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
