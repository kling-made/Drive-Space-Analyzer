using System.Diagnostics;
using System.Security;

namespace DriveSpaceAnalyzer.Core;

public interface IScanService
{
    Task<ScanSnapshot> ScanAsync(string root, long thresholdBytes, IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class ScanService(IFileSystem? fileSystem = null) : IScanService
{
    private readonly IFileSystem fs = fileSystem ?? new PhysicalFileSystem();
    public Task<ScanSnapshot> ScanAsync(string root, long thresholdBytes, IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken) => Task.Run(() => Scan(root, thresholdBytes, progress, cancellationToken));

    public static bool IsFileSystemError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or SecurityException or ArgumentException or NotSupportedException;

    private ScanSnapshot Scan(string rootPath, long threshold, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        rootPath = Path.GetFullPath(rootPath);
        var clock = Stopwatch.StartNew();
        var analysisClock = new Stopwatch();
        var estimator = new RateEstimator();
        var directories = new List<FolderNode>();
        var issues = new List<ScanIssue>();
        long skipped = 0, examined = 0, processed = 0, logicalBytes = 0;
        var phase = ScanPhase.Discovery;
        var currentPath = rootPath;
        TimeSpan lastReport = TimeSpan.MinValue;
        ScanOutcome? forcedOutcome = null;
        string? failure = null;

        void Report(bool force = false, ScanOutcome? outcome = null)
        {
            if (!force && clock.Elapsed - lastReport < TimeSpan.FromMilliseconds(200)) return;
            lastReport = clock.Elapsed;
            double? percent = phase == ScanPhase.Analysis
                ? Math.Min(99.9, 100.0 * processed / Math.Max(1, directories.Count)) : null;
            if (phase == ScanPhase.Finished) percent = outcome == ScanOutcome.Complete ? 100 :
                Math.Min(99.9, 100.0 * processed / Math.Max(1, directories.Count));
            var remaining = phase == ScanPhase.Analysis
                ? estimator.Update(processed, directories.Count, analysisClock.Elapsed) : null;
            progress?.Report(new(phase, currentPath, directories.Count, processed, examined,
                logicalBytes, skipped, clock.Elapsed, percent, remaining, outcome));
        }
        void Skip(FolderNode node, string path, string reason)
        {
            skipped++; node.IsIncomplete = true;
            if (issues.Count < 200) issues.Add(new(path, reason));
        }
        void Enumerate(FolderNode node, bool discover)
        {
            try
            {
                // Recheck before each phase in case a directory has changed to a link.
                var attributes = fs.GetAttributes(node.FullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                { Skip(node, node.FullPath, "Reparse point skipped; link targets are never followed."); return; }
                if ((attributes & FileAttributes.Directory) == 0)
                    throw new DirectoryNotFoundException("Directory disappeared or was replaced.");
                var entries = discover ? fs.EnumerateDirectories(node.FullPath) : fs.EnumerateFiles(node.FullPath);
                foreach (var path in entries)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (discover)
                        {
                            var childAttributes = fs.GetAttributes(path);
                            if ((childAttributes & FileAttributes.ReparsePoint) != 0)
                            { Skip(node, path, "Directory reparse point skipped (junction or symbolic link)."); continue; }
                            if ((childAttributes & FileAttributes.Directory) == 0) continue;
                            var child = new FolderNode(directories.Count, node.Id, path);
                            directories.Add(child); node.AddChild(child.Id);
                        }
                        else
                        {
                            examined++;
                            var metadata = fs.GetFileMetadata(path);
                            if ((metadata.Attributes & FileAttributes.ReparsePoint) != 0)
                            { Skip(node, path, "File reparse point skipped; target was not read."); continue; }
                            ArgumentOutOfRangeException.ThrowIfNegative(metadata.Length);
                            var newNodeSize = checked(node.TotalBytes + metadata.Length);
                            var newTotal = checked(logicalBytes + metadata.Length);
                            node.TotalBytes = newNodeSize; logicalBytes = newTotal;
                            node.FileCount = checked(node.FileCount + 1);
                            if (metadata.Length > threshold)
                                node.AddFile(new(Path.GetFileName(path), path, metadata.Length,
                                    Path.GetExtension(path), metadata.ModifiedUtc, node.Id));
                        }
                    }
                    catch (Exception ex) when (IsFileSystemError(ex)) { Skip(node, path, ex.Message); }
                    Report();
                }
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                Skip(node, node.FullPath, ex.Message);
                if (node.Id == 0) { forcedOutcome = ScanOutcome.Failed; failure = ex.Message; }
            }
        }

        try
        {
            token.ThrowIfCancellationRequested();
            var attributes = fs.GetAttributes(rootPath);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The scan root must be an accessible directory, not a reparse point.");
            directories.Add(new(0, null, rootPath));
            Report(true);
            // Parent identifiers always precede child identifiers. No recursive calls.
            for (var i = 0; i < directories.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                currentPath = directories[i].FullPath;
                Enumerate(directories[i], true); Report();
            }
            phase = ScanPhase.Analysis; analysisClock.Start(); Report(true);
            foreach (var node in directories)
            {
                token.ThrowIfCancellationRequested();
                currentPath = node.FullPath;
                Enumerate(node, false); node.WasAnalyzed = true; processed++; Report();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { forcedOutcome = ScanOutcome.Canceled; }
        catch (Exception ex)
        {
            forcedOutcome = ScanOutcome.Failed; failure = ex.Message;
            if (directories.Count > 0) Skip(directories[0], currentPath, ex.Message);
            else { skipped++; issues.Add(new(rootPath, ex.Message)); }
        }

        phase = ScanPhase.Aggregation; Report(true);
        // Also aggregate partial scans. This is memory-only work and does not touch the disk.
        for (var i = directories.Count - 1; i >= 0; i--)
        {
            var node = directories[i];
            node.IsIncomplete |= !node.WasAnalyzed || forcedOutcome is ScanOutcome.Canceled or ScanOutcome.Failed;
            if (node.ParentId is not int parentId) continue;
            var parent = directories[parentId];
            try
            {
                parent.TotalBytes = checked(parent.TotalBytes + node.TotalBytes);
                parent.FileCount = checked(parent.FileCount + node.FileCount);
                parent.FolderCount = checked(parent.FolderCount + node.FolderCount + 1);
            }
            catch (OverflowException)
            {
                forcedOutcome = ScanOutcome.Failed; failure = "The scan exceeds the supported 64-bit size or count range.";
                Skip(parent, parent.FullPath, failure);
            }
            parent.IsIncomplete |= node.IsIncomplete;
            if (i % 4096 == 0) Report();
        }
        if (token.IsCancellationRequested) forcedOutcome = ScanOutcome.Canceled;
        var outcome = forcedOutcome ?? (skipped > 0 ? ScanOutcome.Warnings : ScanOutcome.Complete);
        if (outcome != ScanOutcome.Complete && directories.Count > 0) directories[0].IsIncomplete = true;
        clock.Stop(); phase = ScanPhase.Finished; Report(true, outcome);
        return new(rootPath, threshold, directories, issues, skipped, examined, processed, clock.Elapsed, outcome, failure);
    }
}
