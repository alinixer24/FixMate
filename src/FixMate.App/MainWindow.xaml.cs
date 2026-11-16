using System.Windows;
using FixMate.App.ViewModels;

namespace FixMate.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
