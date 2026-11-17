using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using DriveSpaceAnalyzer.Core;

namespace DriveSpaceAnalyzer.App;

public sealed class ResultsViewModel : Observable, IDisposable
{
    private readonly DispatcherTimer filterTimer;
    private readonly IWindowNavigator navigator;
    private string folderSearch = "", fileSearch = "";
    private ScanSnapshot? snapshot;
    private ListCollectionView folderView = new(Array.Empty<FolderNode>());
    private ListCollectionView fileView = new(Array.Empty<LargeFile>());
    private string context = "Select a drive and start a scan to see results.";
    private bool loading;
    public ICollectionView FolderView => folderView;
    public ICollectionView FileView => fileView;
    public string FolderSearch { get => folderSearch; set { if (Set(ref folderSearch, value)) ScheduleFilter(); } }
    public string FileSearch { get => fileSearch; set { if (Set(ref fileSearch, value)) ScheduleFilter(); } }
    public string Context { get => context; private set => Set(ref context, value); }
    public bool Loading { get => loading; private set => Set(ref loading, value); }
    public string FolderHint { get; private set; } = "All scanned folders · double-click to explore";
    public string FileHint { get; private set; } = "Files strictly greater than the scan threshold";
    public ResultsViewModel(IWindowNavigator navigator)
    {
        this.navigator = navigator;
        filterTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        filterTimer.Tick += FilterTick;
    }
    public async Task LoadAsync(ScanSnapshot scan, int? directoryId = null)
    {
        Loading = true;
        try
        {
            var data = await Task.Run(() => (Folders: ResultQuery.Folders(scan, directoryId), Files: ResultQuery.Files(scan, directoryId)));
            snapshot = scan;
            folderView = MakeView(data.Folders, nameof(FolderNode.TotalBytes), item => item is FolderNode folder
                && ResultQuery.Matches(folder.Name, folder.FullPath, FolderSearch));
            fileView = MakeView(data.Files, nameof(LargeFile.SizeBytes), item => item is LargeFile file
                && ResultQuery.Matches(file.Name, file.FullPath, FileSearch));
            FolderHint = directoryId.HasValue ? "Immediate subfolders · sizes include all descendants · double-click to explore"
                : "All scanned folders · sizes include all descendants · double-click to explore";
            FileHint = $"Files > {SizeUnits.Format(scan.ThresholdBytes)} ({scan.ThresholdBytes:N0} bytes) · recursively within this view";
            var node = directoryId.HasValue ? scan.Directories[directoryId.Value] : scan.Root;
            Context = $"{node?.FullPath ?? scan.RootPath}   ·   {scan.Status}   ·   {data.Folders.Length:N0} folders   ·   {data.Files.Length:N0} qualifying files"
                + (node?.IsIncomplete == true ? "   ·   Sizes may exclude inaccessible or unexamined content." : "");
            Changed(nameof(FolderView)); Changed(nameof(FileView)); Changed(nameof(FolderHint)); Changed(nameof(FileHint));
        }
        finally { Loading = false; }
    }
    private static ListCollectionView MakeView(IList items, string sortProperty, Predicate<object> filter)
    {
        var view = new ListCollectionView(items);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Add(new(sortProperty, ListSortDirection.Descending)); view.Filter = filter;
        }
        return view;
    }
    public void OpenFolder(FolderNode folder)
    {
        if (snapshot != null) navigator.Open(snapshot, folder.Id);
    }
    private void ScheduleFilter() { filterTimer.Stop(); filterTimer.Start(); }
    private void FilterTick(object? sender, EventArgs e) => ApplyFilters();
    public void ApplyFilters() { filterTimer.Stop(); folderView.Refresh(); fileView.Refresh(); }
    public void Dispose() { filterTimer.Stop(); filterTimer.Tick -= FilterTick; }
}
