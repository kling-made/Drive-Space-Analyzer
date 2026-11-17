using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DriveSpaceAnalyzer.App;
using DriveSpaceAnalyzer.Core;
using Xunit;

namespace DriveSpaceAnalyzer.Wpf.Tests;

public sealed class WindowTests
{
    [Fact]
    public async Task ActualWpfControlsScanFilterSortAndOpenIndependentNestedWindowsOnDispatcher()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsa-wpf-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "A", "B")); Directory.CreateDirectory(Path.Combine(root, "C"));
        await File.WriteAllBytesAsync(Path.Combine(root, "A", "B", "large.bin"), new byte[40]);
        await File.WriteAllBytesAsync(Path.Combine(root, "C", "small.bin"), new byte[10]);
        try
        {
            await RunSta(async () =>
            {
                var app = new DriveSpaceAnalyzer.App.App(); app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var fs = new CountingFileSystem(); var scanner = new ScanService(fs);
                var provider = new TestDrives(root);
                var navigator = new WindowNavigator();
                var vm = new MainViewModel(scanner, provider, navigator) { Minimum = "0", Unit = "MB" };
                var uiThread = Environment.CurrentManagedThreadId; var threadViolation = false;
                vm.PropertyChanged += (_, _) => threadViolation |= Environment.CurrentManagedThreadId != uiThread;
                var main = new MainWindow(); ((MainViewModel)main.DataContext).Dispose(); main.DataContext = vm;
                app.MainWindow = main; main.Show();
                try
                {
                    await Until(() => vm.Drives.Count == 1 && vm.CanConfigure);
                    Assert.Null(vm.Snapshot); // Discovery of drives must never start a scan.
                    var scanButton = Descendants<Button>(main).Single(x => Equals(x.Content, "Scan Drive"));
                    var invoker = (IInvokeProvider)new ButtonAutomationPeer(scanButton).GetPattern(PatternInterface.Invoke)!;
                    invoker.Invoke(); await Until(() => vm.Snapshot != null && !vm.IsScanning);
                    Assert.False(threadViolation); Assert.Equal(ScanOutcome.Complete, vm.Snapshot!.Outcome);
                    Assert.Equal(100, vm.Percent);
                    Assert.Equal(new long[] { 40, 40, 10 }, vm.Results.FolderView.Cast<FolderNode>().Select(x => x.TotalBytes));
                    Assert.Equal(new long[] { 40, 10 }, vm.Results.FileView.Cast<LargeFile>().Select(x => x.SizeBytes));
                    SaveWindow(main, "main.png");
                    vm.Results.FileSearch = "LARGE.BIN"; vm.Results.ApplyFilters();
                    Assert.Single(vm.Results.FileView.Cast<LargeFile>()); vm.Results.FileSearch = ""; vm.Results.ApplyFilters();
                    var calls = fs.Calls;
                    var a = vm.Snapshot.Directories.Single(x => x.Name == "A");
                    var panel = Descendants<ResultsPanel>(main).Single();
                    DoubleClick(panel, a);
                    await Until(() => app.Windows.OfType<FolderWindow>().Any());
                    var first = app.Windows.OfType<FolderWindow>().Single();
                    await Until(() => first.LoadTask?.IsCompleted == true);
                    var firstVm = (ResultsViewModel)first.DataContext;
                    var b = Assert.Single(firstVm.FolderView.Cast<FolderNode>());
                    Assert.Equal("B", b.Name); Assert.Equal(40, b.TotalBytes);
                    Assert.Equal("large.bin", Assert.Single(firstVm.FileView.Cast<LargeFile>()).Name);
                    SaveWindow(first, "drill-down.png");
                    DoubleClick(Descendants<ResultsPanel>(first).Single(), b);
                    await Until(() => app.Windows.OfType<FolderWindow>().Count() == 2);
                    var second = app.Windows.OfType<FolderWindow>().Single(x => x != first);
                    await Until(() => second.LoadTask?.IsCompleted == true);
                    Assert.Empty(((ResultsViewModel)second.DataContext).FolderView.Cast<FolderNode>());
                    Assert.Contains(b.FullPath, second.Title);
                    Assert.Equal(calls, fs.Calls);
                    firstVm.FolderSearch = "no match"; firstVm.ApplyFilters();
                    Assert.Equal(3, vm.Results.FolderView.Cast<FolderNode>().Count());
                    first.Close(); Assert.True(main.IsVisible); Assert.True(second.IsVisible);
                    second.Close(); Assert.True(main.IsVisible);
                    Assert.False(threadViolation);

                    using var slow = new MainViewModel(new DelayedScanner(scanner), provider, navigator);
                    await slow.RefreshDrivesAsync();
                    var slowTask = slow.ScanAsync(); var initialElapsed = slow.Elapsed;
                    Assert.True(slow.IsScanning); Assert.False(slow.CanConfigure);
                    await Task.Delay(1100);
                    Assert.NotEqual(initialElapsed, slow.Elapsed);
                    Assert.Contains("Calculating estimate", slow.Remaining);
                    Assert.True(slow.CancelCommand.CanExecute(null)); slow.CancelCommand.Execute(null);
                    await slowTask;
                    Assert.Equal(ScanOutcome.Canceled, slow.Snapshot!.Outcome);
                    Assert.Contains("Canceled", slow.Status); Assert.True(slow.Percent < 100);
                }
                finally
                {
                    foreach (var window in app.Windows.Cast<Window>().ToArray()) window.Close();
                    app.Shutdown();
                }
            });
        }
        finally { Directory.Delete(root, true); }
    }

    private static void DoubleClick(ResultsPanel panel, FolderNode folder)
    {
        var grid = (DataGrid)panel.FindName("FoldersGrid"); grid.SelectedItem = folder;
        grid.ScrollIntoView(folder); grid.UpdateLayout();
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(folder);
        Assert.NotNull(row);
        grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Control.MouseDoubleClickEvent, Source = row });
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (!condition()) { if (DateTime.UtcNow > timeout) throw new TimeoutException("WPF operation did not finish."); await Task.Delay(20); }
    }
    private static void SaveWindow(Window window, string filename)
    {
        var directory = Environment.GetEnvironmentVariable("DSA_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, filename)); encoder.Save(stream);
    }
    private static Task RunSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private sealed class TestDrives(string root) : IDriveProvider
    {
        public IReadOnlyList<DriveChoice> GetDrives() => [new(root, "Test volume", "Fixed", true, 1000, 500, null)];
    }
    private sealed class DelayedScanner(IScanService inner) : IScanService
    {
        public Task<ScanSnapshot> ScanAsync(string root, long threshold, IProgress<ScanProgress>? progress, CancellationToken token) => Task.Run(async () =>
        {
            progress?.Report(new(ScanPhase.Discovery, root, 1, 0, 0, 0, 0, TimeSpan.Zero, null, null));
            try { await Task.Delay(5000, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            return await inner.ScanAsync(root, threshold, progress, token);
        });
    }
    private sealed class CountingFileSystem : IFileSystem
    {
        private readonly PhysicalFileSystem fs = new();
        public int Calls;
        public FileAttributes GetAttributes(string path) => fs.GetAttributes(path);
        public IEnumerable<string> EnumerateDirectories(string path) { Interlocked.Increment(ref Calls); return fs.EnumerateDirectories(path); }
        public IEnumerable<string> EnumerateFiles(string path) { Interlocked.Increment(ref Calls); return fs.EnumerateFiles(path); }
        public FileMetadata GetFileMetadata(string path) => fs.GetFileMetadata(path);
    }
}
