using System.Runtime.InteropServices;
using System.Windows;
using DriveSpaceAnalyzer.Core;

namespace DriveSpaceAnalyzer.App;
public partial class FolderWindow : Window
{
    private readonly ScanSnapshot snapshot;
    private readonly int directoryId;
    private readonly ResultsViewModel viewModel;
    public Task? LoadTask { get; private set; }
    public FolderWindow(ScanSnapshot snapshot, int directoryId, IWindowNavigator navigator)
    {
        InitializeComponent(); this.snapshot = snapshot; this.directoryId = directoryId;
        var path = snapshot.Directories[directoryId].FullPath;
        Title = "Drive Space Analyzer — " + path; PathLabel.Text = path;
        DataContext = viewModel = new(navigator);
    }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try { LoadTask = viewModel.LoadAsync(snapshot, directoryId); await LoadTask; }
        catch (Exception ex) { Footer.Text = "Could not display this snapshot: " + ex.Message; }
    }
    private void CopyPathClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(snapshot.Directories[directoryId].FullPath); Footer.Text = "Directory path copied to the clipboard."; }
        catch (ExternalException) { Footer.Text = "Clipboard is busy. Please try copying again."; }
    }
    private void WindowClosed(object? sender, EventArgs e) => viewModel.Dispose();
}
