using System.ComponentModel;
using System.Windows;

namespace DriveSpaceAnalyzer.App;
public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); DataContext = new MainViewModel(); }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) await vm.RefreshDrivesAsync();
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.RequestCancel();
    }
    private void WindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Dispose();
    }
}
