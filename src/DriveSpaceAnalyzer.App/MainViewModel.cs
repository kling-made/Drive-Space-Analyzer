using System.Collections.ObjectModel;
using System.Windows.Threading;
using DriveSpaceAnalyzer.Core;

namespace DriveSpaceAnalyzer.App;

public sealed class MainViewModel : Observable, IDisposable
{
    private readonly IScanService scanner;
    private readonly IDriveProvider driveProvider;
    private readonly DispatcherTimer elapsedTimer;
    private readonly System.Diagnostics.Stopwatch clock = new();
    private CancellationTokenSource? cancellation;
    private DriveChoice? selectedDrive;
    private string minimum = "1", unit = "GB", status = "Ready — choose a drive to begin.", validation = "";
    private string phase = "Waiting", currentDirectory = "No scan running", remaining = "Estimated remaining: —";
    private string counters = "0 folders processed   ·   0 files examined   ·   0 skipped";
    private string scannedBytes = "Logical bytes accounted for: 0 B", summary = "No completed scan yet.", issues = "";
    private bool scanning, refreshing, indeterminate;
    private double percent;
    private ResultsViewModel results;
    private ScanSnapshot? snapshot;
    private readonly IWindowNavigator navigator;
    public ObservableCollection<DriveChoice> Drives { get; } = [];
    public string[] Units => SizeUnits.InputUnits;
    public AsyncCommand ScanCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand CancelCommand { get; }
    public ResultsViewModel Results { get => results; private set => Set(ref results, value); }
    public ScanSnapshot? Snapshot => snapshot;
    public DriveChoice? SelectedDrive
    {
        get => selectedDrive;
        set { if (Set(ref selectedDrive, value)) { Changed(nameof(DriveDetails)); RefreshCommands(); } }
    }
    public string DriveDetails => SelectedDrive?.CapacityDescription ?? "No drive selected.";
    public string Minimum { get => minimum; set { if (Set(ref minimum, value)) Validate(); } }
    public string Unit { get => unit; set { if (Set(ref unit, value)) Validate(); } }
    public string Validation { get => validation; private set => Set(ref validation, value); }
    public bool IsScanning { get => scanning; private set { if (Set(ref scanning, value)) { Changed(nameof(CanConfigure)); RefreshCommands(); } } }
    public bool CanConfigure => !IsScanning && !refreshing;
    public string Status { get => status; private set => Set(ref status, value); }
    public string Phase { get => phase; private set => Set(ref phase, value); }
    public string CurrentDirectory { get => currentDirectory; private set => Set(ref currentDirectory, value); }
    public string Remaining { get => remaining; private set => Set(ref remaining, value); }
    public string Counters { get => counters; private set => Set(ref counters, value); }
    public string ScannedBytes { get => scannedBytes; private set => Set(ref scannedBytes, value); }
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public string Issues { get => issues; private set => Set(ref issues, value); }
    public bool Indeterminate { get => indeterminate; private set => Set(ref indeterminate, value); }
    public double Percent { get => percent; private set => Set(ref percent, value); }
    public string Percentage => Indeterminate ? "Working…" : $"{Percent:0.#}% of directories";
    public string Elapsed => "Elapsed: " + SizeUnits.Time(clock.Elapsed);
    public MainViewModel() : this(new ScanService(), new DriveProvider(), new WindowNavigator()) { }
    public MainViewModel(IScanService scanner, IDriveProvider driveProvider, IWindowNavigator navigator)
    {
        this.scanner = scanner; this.driveProvider = driveProvider; this.navigator = navigator;
        results = new(navigator);
        ScanCommand = new(ScanAsync, () => CanConfigure && SelectedDrive?.IsReady == true && Validation == "", ShowError);
        RefreshCommand = new(RefreshDrivesAsync, () => CanConfigure, ShowError);
        CancelCommand = new(() => { cancellation?.Cancel(); Status = "Cancel requested — waiting for the current filesystem call…"; }, () => IsScanning);
        elapsedTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        elapsedTimer.Tick += ElapsedTick;
    }
    private void ElapsedTick(object? sender, EventArgs e) => Changed(nameof(Elapsed));
    private void Validate()
    {
        SizeUnits.TryParse(Minimum, Unit, out _, out var error); Validation = error; RefreshCommands();
    }
    private void RefreshCommands() { ScanCommand?.Refresh(); RefreshCommand?.Refresh(); CancelCommand?.Refresh(); }
    private void ShowError(Exception ex) { Status = "Operation failed: " + ex.Message; }
    public async Task RefreshDrivesAsync()
    {
        if (!CanConfigure) return;
        refreshing = true; Changed(nameof(CanConfigure)); RefreshCommands();
        var previous = SelectedDrive?.Root;
        try
        {
            var drives = await Task.Run(driveProvider.GetDrives);
            Drives.Clear(); foreach (var drive in drives) Drives.Add(drive);
            SelectedDrive = Drives.FirstOrDefault(x => x.Root == previous)
                ?? Drives.FirstOrDefault(x => x.IsReady) ?? Drives.FirstOrDefault();
            Status = Drives.Count == 0 ? "No logical drives found. Connect a drive and refresh." : "Ready — select a drive and click Scan Drive.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { refreshing = false; Changed(nameof(CanConfigure)); RefreshCommands(); }
    }
    public async Task ScanAsync()
    {
        if (!CanConfigure || SelectedDrive?.IsReady != true) return;
        if (!SizeUnits.TryParse(Minimum, Unit, out var threshold, out var error)) { Validation = error; return; }
        var drive = SelectedDrive;
        cancellation = new(); IsScanning = true; snapshot = null; Changed(nameof(Snapshot));
        Results.Dispose(); Results = new(navigator);
        Issues = ""; Summary = $"Scanning {drive.Root}   ·   {drive.CapacityDescription}";
        Status = $"Scanning {drive.Root}"; Phase = "Directory discovery"; CurrentDirectory = drive.Root;
        Counters = "0 folders processed   ·   0 files examined   ·   0 skipped";
        ScannedBytes = "Logical bytes accounted for: 0 B"; Percent = 0; Indeterminate = true;
        Remaining = "Estimated remaining: Calculating estimate…";
        clock.Restart(); elapsedTimer.Start(); Changed(nameof(Percentage)); Changed(nameof(Elapsed));
        // Progress<T> captures WPF's dispatcher synchronization context.
        var progress = new Progress<ScanProgress>(UpdateProgress);
        try
        {
            snapshot = await scanner.ScanAsync(drive.Root, threshold, progress, cancellation.Token);
            // A slow UI must not let queued progress overwrite the final snapshot.
            await Dispatcher.Yield(DispatcherPriority.Background);
            clock.Stop(); elapsedTimer.Stop();
            await Results.LoadAsync(snapshot);
            Status = snapshot.Status; Phase = snapshot.Status;
            Percent = snapshot.Outcome == ScanOutcome.Complete ? 100 : Math.Min(99.9, Percent);
            Indeterminate = false; Remaining = "Estimated remaining: —";
            var largestFolder = Results.LargestFolder;
            var largestFile = Results.LargestFile;
            Summary = $"Last scanned: {drive.Root}   ·   {drive.CapacityDescription}\n"
                + $"{snapshot.FilesExamined:N0} files examined · {snapshot.Root?.FileCount ?? 0:N0} accounted files · {snapshot.DirectoriesProcessed:N0} folders processed (root included) · "
                + $"{snapshot.LogicalBytes:N0} logical bytes ({SizeUnits.Format(snapshot.LogicalBytes)}) · {Results.LargeFileCount:N0} qualifying files\n"
                + $"Largest folder: {(largestFolder == null ? "—" : largestFolder.FullPath + " (" + SizeUnits.Format(largestFolder.TotalBytes) + ")")}\n"
                + $"Largest qualifying file: {(largestFile == null ? "—" : largestFile.FullPath + " (" + SizeUnits.Format(largestFile.SizeBytes) + ")")}\n"
                + $"Duration: {SizeUnits.Time(snapshot.Duration)} · Skipped/errors: {snapshot.Skipped:N0} · {snapshot.Status}";
            Issues = snapshot.FailureMessage ?? "";
            if (snapshot.Issues.Count > 0)
                Issues += (Issues == "" ? "" : "\n") + string.Join("\n", snapshot.Issues.Select(x => x.Path + " — " + x.Reason))
                    + (snapshot.Skipped > snapshot.Issues.Count ? $"\nShowing the first {snapshot.Issues.Count} of {snapshot.Skipped:N0} skipped/error events." : "");
            Changed(nameof(Snapshot)); Changed(nameof(Percentage)); Changed(nameof(Elapsed));
        }
        catch (Exception ex) { Status = "Scan Failed: " + ex.Message; Phase = "Scan Failed"; Indeterminate = false; Percent = 0; }
        finally
        {
            clock.Stop(); elapsedTimer.Stop(); cancellation.Dispose(); cancellation = null; IsScanning = false;
            Changed(nameof(Elapsed)); Changed(nameof(Percentage));
        }
    }
    private void UpdateProgress(ScanProgress progress)
    {
        if (!IsScanning) return;
        Phase = progress.Phase switch
        {
            ScanPhase.Discovery => "Phase 1 / 2 — Directory discovery",
            ScanPhase.Analysis => "Phase 2 / 2 — File analysis",
            ScanPhase.Aggregation => "Aggregating recursive folder totals",
            _ => "Preparing results"
        };
        CurrentDirectory = progress.CurrentDirectory;
        Indeterminate = !progress.Percent.HasValue; Percent = progress.Percent ?? 0; Changed(nameof(Percentage));
        Remaining = "Estimated remaining: " + (progress.Phase == ScanPhase.Discovery ? "Calculating estimate…"
            : progress.Remaining is TimeSpan time ? SizeUnits.Time(time) : "Estimating…");
        Counters = $"{progress.DirectoriesProcessed:N0} / {progress.DirectoriesDiscovered:N0} folders processed   ·   {progress.FilesExamined:N0} files examined   ·   {progress.Skipped:N0} skipped/errors";
        ScannedBytes = $"Logical bytes accounted for: {SizeUnits.Format(progress.LogicalBytes)}";
    }
    public void RequestCancel() => cancellation?.Cancel();
    public void Dispose() { RequestCancel(); elapsedTimer.Stop(); elapsedTimer.Tick -= ElapsedTick; Results.Dispose(); }
}
