namespace DriveSpaceAnalyzer.Core;

public enum ScanOutcome { Complete, Warnings, Canceled, Failed }
public enum ScanPhase { Discovery, Analysis, Aggregation, Finished }

public sealed record DriveChoice(string Root, string Label, string Type, bool IsReady,
    long? Capacity, long? Free, string? Error, long? TotalFree = null)
{
    public long? Used => Capacity - (TotalFree ?? Free);
    public string Description => $"{Root}  {Label}  ·  {Type}" + (IsReady ? "" : "  ·  Unavailable");
    public string CapacityDescription => IsReady
        ? $"Capacity {SizeUnits.Format(Capacity ?? 0)}   ·   Used {SizeUnits.Format(Used ?? 0)}   ·   Available free {SizeUnits.Format(Free ?? 0)}"
        : Error ?? "Drive is not ready.";
}

public sealed class FolderNode
{
    private readonly List<int> children = [];
    private readonly List<LargeFile> files = [];
    public int Id { get; }
    public int? ParentId { get; }
    public string Name { get; }
    public string FullPath { get; }
    public long TotalBytes { get; internal set; }
    public long FileCount { get; internal set; }
    public long FolderCount { get; internal set; }
    public bool IsIncomplete { get; internal set; }
    public bool WasAnalyzed { get; internal set; }
    public string Quality => IsIncomplete ? "Partial — inaccessible, skipped or unexamined content" : "Accessible content accounted for";
    public IReadOnlyList<int> Children => children;
    public IReadOnlyList<LargeFile> LargeFiles => files;
    internal FolderNode(int id, int? parentId, string path)
    {
        Id = id; ParentId = parentId; FullPath = path;
        Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (string.IsNullOrEmpty(Name)) Name = path;
    }
    internal void AddChild(int id) => children.Add(id);
    internal void AddFile(LargeFile file) => files.Add(file);
}

public sealed record LargeFile(string Name, string FullPath, long SizeBytes, string Extension,
    DateTime ModifiedUtc, int DirectoryId)
{
    public DateTime ModifiedLocal => ModifiedUtc.ToLocalTime();
}
public sealed record ScanIssue(string Path, string Reason);
public sealed record ScanProgress(ScanPhase Phase, string CurrentDirectory, long DirectoriesDiscovered,
    long DirectoriesProcessed, long FilesExamined, long LogicalBytes, long Skipped,
    TimeSpan Elapsed, double? Percent, TimeSpan? Remaining, ScanOutcome? Outcome = null);

/// <summary>Published only after scanning and aggregation have stopped. No filesystem access.</summary>
public sealed class ScanSnapshot
{
    public string RootPath { get; }
    public long ThresholdBytes { get; }
    public IReadOnlyList<FolderNode> Directories { get; }
    public IReadOnlyList<ScanIssue> Issues { get; }
    public long Skipped { get; }
    public long FilesExamined { get; }
    public long DirectoriesProcessed { get; }
    public TimeSpan Duration { get; }
    public ScanOutcome Outcome { get; }
    public string? FailureMessage { get; }
    public FolderNode? Root => Directories.Count == 0 ? null : Directories[0];
    public long LogicalBytes => Root?.TotalBytes ?? 0;
    public string Status => Outcome switch
    {
        ScanOutcome.Complete => "Scan Complete",
        ScanOutcome.Warnings => "Scan Complete with Warnings — partial results",
        ScanOutcome.Canceled => "Scan Canceled — partial results",
        _ => "Scan Failed — partial results"
    };
    internal ScanSnapshot(string rootPath, long thresholdBytes, List<FolderNode> directories,
        List<ScanIssue> issues, long skipped, long filesExamined, long processed,
        TimeSpan duration, ScanOutcome outcome, string? failure)
    {
        RootPath = rootPath; ThresholdBytes = thresholdBytes; Directories = directories.AsReadOnly();
        Issues = issues.AsReadOnly(); Skipped = skipped; FilesExamined = filesExamined;
        DirectoriesProcessed = processed; Duration = duration; Outcome = outcome; FailureMessage = failure;
    }
    public IEnumerable<FolderNode> GetFolders(int? directoryId = null) => directoryId is int id
        ? Directories[id].Children.Select(child => Directories[child]) : Directories.Skip(1);

    public IEnumerable<LargeFile> GetLargeFiles(int? directoryId = null)
    {
        if (Directories.Count == 0) yield break;
        var pending = new Stack<int>();
        pending.Push(directoryId ?? 0);
        while (pending.TryPop(out var id))
        {
            var node = Directories[id];
            foreach (var file in node.LargeFiles) yield return file;
            foreach (var child in node.Children) pending.Push(child);
        }
    }
}

public static class ResultQuery
{
    public static bool Matches(string name, string path, string? query) => string.IsNullOrWhiteSpace(query)
        || name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
        || path.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
    public static FolderNode[] Folders(ScanSnapshot snapshot, int? id = null) => snapshot.GetFolders(id)
        .OrderByDescending(x => x.TotalBytes).ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).ToArray();
    public static LargeFile[] Files(ScanSnapshot snapshot, int? id = null) => snapshot.GetLargeFiles(id)
        .OrderByDescending(x => x.SizeBytes).ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).ToArray();
}
