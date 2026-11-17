using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DriveSpaceAnalyzer.Core;

namespace DriveSpaceAnalyzer.App;
public partial class ResultsPanel : UserControl
{
    public ResultsPanel() { InitializeComponent(); }
    private void FolderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not DataGridRow) source = VisualTreeHelper.GetParent(source);
        if (source is DataGridRow { Item: FolderNode folder } && DataContext is ResultsViewModel viewModel)
        { viewModel.OpenFolder(folder); e.Handled = true; }
    }
}
