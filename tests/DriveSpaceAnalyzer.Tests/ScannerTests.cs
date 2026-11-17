using System.Globalization;
using DriveSpaceAnalyzer.Core;
using Xunit;

namespace DriveSpaceAnalyzer.Tests;
public sealed class ScannerTests
{
    private static Task<ScanSnapshot> Scan(FakeFileSystem fs, long threshold = 0, CancellationToken token = default,
        IProgress<ScanProgress>? progress = null) => new ScanService(fs).ScanAsync(fs.Root, threshold, progress, token);

    [Fact]
    public async Task AggregatesCountsAndRootFilesWithoutRankingRoot()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("root.bin", 10); fs.AddFile(Path.Combine("a", "one.bin"), 20);
        fs.AddFile(Path.Combine("a", "b", "two.bin"), 30); fs.AddDirectory("empty");
        var scan = await Scan(fs);
        Assert.Equal(60, scan.LogicalBytes); Assert.Equal(3, scan.Root!.FileCount); Assert.Equal(3, scan.Root.FolderCount);
        var a = Assert.Single(scan.GetFolders(), x => x.Name == "a");
        Assert.Equal(50, a.TotalBytes); Assert.Equal(2, a.FileCount); Assert.Equal(1, a.FolderCount);
        Assert.DoesNotContain(scan.Root, scan.GetFolders());
        Assert.Contains(scan.GetLargeFiles(), x => x.Name == "root.bin");
        Assert.Equal(2 * scan.Directories.Count, fs.EnumerationCalls);
        Assert.Equal(ScanOutcome.Complete, scan.Outcome);
    }

    [Fact]
    public async Task ThresholdIsStrictAndNonQualifyingFilesStillCount()
    {
        var fs = new FakeFileSystem(); fs.AddFile("below", 999_999_999); fs.AddFile("equal", 1_000_000_000); fs.AddFile("above", 1_000_000_001);
        var scan = await Scan(fs, 1_000_000_000);
        Assert.Equal("above", Assert.Single(scan.GetLargeFiles()).Name); Assert.Equal(3, scan.Root!.FileCount);
        Assert.Equal(3_000_000_000, scan.LogicalBytes);
    }

    [Theory]
    [InlineData("1", "GB", 1_000_000_000L)]
    [InlineData("0.5", "GB", 500_000_000L)]
    [InlineData("1.5", "TB", 1_500_000_000_000L)]
    [InlineData("1.25", "MB", 1_250_000L)]
    [InlineData("0", "MB", 0L)]
    public void ConvertsDecimalUnits(string input, string unit, long expected)
    {
        Assert.True(SizeUnits.TryParse(input, unit, out var bytes, out _, CultureInfo.InvariantCulture)); Assert.Equal(expected, bytes);
    }
    [Theory]
    [InlineData("-1")][InlineData("NaN")][InlineData("Infinity")][InlineData("1e999")][InlineData("99999999999999999999")][InlineData("")]
    public void RejectsInvalidAndOverflowingThresholds(string input) =>
        Assert.False(SizeUnits.TryParse(input, "TB", out _, out _, CultureInfo.InvariantCulture));

    [Fact]
    public async Task SortsNumericSizesAndFiltersNameOrFullPathWithoutEnumeration()
    {
        var fs = new FakeFileSystem(); fs.AddFile(Path.Combine("small", "s"), 700_000_000);
        fs.AddFile(Path.Combine("Big", "movie.ISO"), 120_000_000_000); fs.AddFile(Path.Combine("mid", "m"), 9_000_000_000);
        var scan = await Scan(fs); var calls = fs.EnumerationCalls;
        Assert.Equal(new long[] { 120_000_000_000, 9_000_000_000, 700_000_000 }, ResultQuery.Folders(scan).Select(x => x.TotalBytes));
        Assert.Equal(new long[] { 120_000_000_000, 9_000_000_000, 700_000_000 }, ResultQuery.Files(scan).Select(x => x.SizeBytes));
        Assert.True(ResultQuery.Matches("movie.ISO", Path.Combine(fs.Root, "Big", "movie.ISO"), "big"));
        Assert.True(ResultQuery.Matches("movie.ISO", "x", ".iso")); Assert.False(ResultQuery.Matches("x", "y", "z"));
        Assert.Equal(calls, fs.EnumerationCalls);
    }

    [Fact]
    public async Task EmptyDirectoriesAndZeroByteFilesRemainAccounted()
    {
        var fs = new FakeFileSystem(); fs.AddDirectory("empty"); fs.AddFile("zero", 0);
        var scan = await Scan(fs); Assert.Equal(0, Assert.Single(scan.GetFolders()).TotalBytes);
        Assert.Equal(1, scan.Root!.FileCount); Assert.Empty(scan.GetLargeFiles()); Assert.Equal(ScanOutcome.Complete, scan.Outcome);
    }

    [Fact]
    public async Task InaccessibleDescendantsPropagateIncompleteFlagsAndKeepOtherResults()
    {
        var fs = new FakeFileSystem(); var denied = fs.AddDirectory(Path.Combine("a", "denied"));
        fs.AddFile(Path.Combine("a", "good"), 12); fs.FailDiscovery[denied] = new UnauthorizedAccessException("Denied");
        fs.FailAnalysis[denied] = new UnauthorizedAccessException("Denied");
        var scan = await Scan(fs);
        Assert.Equal(12, scan.LogicalBytes); Assert.Equal(ScanOutcome.Warnings, scan.Outcome);
        Assert.True(scan.Root!.IsIncomplete); Assert.True(scan.Directories.Single(x => x.Name == "a").IsIncomplete);
        Assert.True(scan.Skipped > 0); Assert.Contains(scan.Issues, x => x.Reason == "Denied");
    }

    [Fact]
    public async Task LazyEnumerationFailureKeepsAlreadyDiscoveredChildren()
    {
        var fs = new FakeFileSystem(); fs.AddFile(Path.Combine("child", "good"), 33); fs.FailDiscoveryAfterYield = fs.Root;
        var scan = await Scan(fs); Assert.Equal(33, scan.LogicalBytes);
        Assert.Equal(ScanOutcome.Failed, scan.Outcome); Assert.Single(scan.GetFolders()); Assert.True(scan.Root!.IsIncomplete);
    }

    [Fact]
    public async Task FileDisappearingDoesNotAbortRemainingFiles()
    {
        var fs = new FakeFileSystem(); var missing = fs.AddFile("missing", 25); fs.AddFile("good", 50);
        fs.FailMetadata[missing] = new FileNotFoundException("Gone");
        var scan = await Scan(fs); Assert.Equal(50, scan.LogicalBytes); Assert.Equal(2, scan.FilesExamined);
        Assert.Equal(ScanOutcome.Warnings, scan.Outcome);
    }

    [Fact]
    public async Task DirectoryAndFileReparsePointsAreNeverFollowed()
    {
        var fs = new FakeFileSystem(); var link = fs.AddDirectory("junction");
        fs.Attributes[link] |= FileAttributes.ReparsePoint; fs.AddFile(Path.Combine("junction", "external"), 500);
        fs.AddFile("symlink", 1000, FileAttributes.ReparsePoint); fs.AddFile("real", 7);
        var scan = await Scan(fs);
        Assert.Empty(scan.GetFolders()); Assert.Equal(7, scan.LogicalBytes); Assert.Equal(2, scan.Skipped);
        Assert.Equal(2, fs.EnumerationCalls); Assert.Single(scan.GetLargeFiles());
    }

    [Theory][InlineData(true)][InlineData(false)]
    public async Task CancelsCooperativelyDuringEitherPhase(bool discovery)
    {
        var fs = new FakeFileSystem(); fs.AddFile(Path.Combine("child", "data"), 10);
        using var cts = new CancellationTokenSource();
        var progress = new List<ScanProgress>();
        var scan = await Scan(fs, token: cts.Token, progress: new InlineProgress<ScanProgress>(p =>
        { progress.Add(p); if (p.Phase == (discovery ? ScanPhase.Discovery : ScanPhase.Analysis)) cts.Cancel(); }));
        Assert.Equal(ScanOutcome.Canceled, scan.Outcome); Assert.True(scan.Root!.IsIncomplete);
        Assert.DoesNotContain(progress, x => x.Percent == 100); Assert.Equal(ScanOutcome.Canceled, progress.Last().Outcome);
    }

    [Fact]
    public async Task CancellationAfterSomeFilesPreservesTheirAggregates()
    {
        var fs = new FakeFileSystem(); fs.AddFile(Path.Combine("child", "one"), 40); fs.AddFile(Path.Combine("child", "two"), 20);
        using var cts = new CancellationTokenSource();
        fs.OnFile = path => { if (Path.GetFileName(path) == "two") cts.Cancel(); };
        var scan = await Scan(fs, token: cts.Token);
        Assert.Equal(40, scan.LogicalBytes); Assert.Equal(ScanOutcome.Canceled, scan.Outcome);
        Assert.Equal(40, Assert.Single(scan.GetFolders()).TotalBytes);
    }

    [Fact]
    public async Task SupportsLarge64BitMetadataAndDeepTreesIteratively()
    {
        var fs = new FakeFileSystem(); var path = "d";
        for (var i = 0; i < 1500; i++) { fs.AddDirectory(path); path = Path.Combine(path, "d"); }
        fs.AddFile(Path.Combine(Path.GetDirectoryName(path)!, "huge"), 8_000_000_000_000_000_000L);
        var scan = await Scan(fs); Assert.Equal(8_000_000_000_000_000_000L, scan.LogicalBytes);
        Assert.Equal(1500, scan.Root!.FolderCount); Assert.Equal(ScanOutcome.Complete, scan.Outcome);
    }

    [Fact]
    public async Task OverflowFailsClearlyInsteadOfWrappingToNegativeBytes()
    {
        var fs = new FakeFileSystem(); fs.AddFile(Path.Combine("a", "x"), long.MaxValue); fs.AddFile(Path.Combine("b", "y"), 1);
        var scan = await Scan(fs); Assert.Equal(ScanOutcome.Failed, scan.Outcome); Assert.True(scan.LogicalBytes >= 0); Assert.NotNull(scan.FailureMessage);
    }

    [Fact]
    public async Task DrillDownUsesOnlyChildrenAndRecursiveFilesWithoutAnyDiskAccess()
    {
        var fs = new FakeFileSystem(); var a = fs.AddDirectory("a"); var b = fs.AddDirectory(Path.Combine("a", "b"));
        fs.AddFile(Path.Combine("a", "b", "deep"), 9); fs.AddFile(Path.Combine("a", "direct"), 4); fs.AddFile(Path.Combine("other", "outside"), 30);
        var scan = await Scan(fs); var calls = fs.EnumerationCalls;
        var node = scan.Directories.Single(x => x.FullPath == a);
        Assert.Equal(b, Assert.Single(ResultQuery.Folders(scan, node.Id)).FullPath);
        Assert.Equal(new long[] { 9, 4 }, ResultQuery.Files(scan, node.Id).Select(x => x.SizeBytes));
        var nested = scan.Directories.Single(x => x.FullPath == b);
        Assert.Empty(ResultQuery.Folders(scan, nested.Id)); Assert.Equal("deep", Assert.Single(ResultQuery.Files(scan, nested.Id)).Name);
        Assert.Equal(calls, fs.EnumerationCalls);
    }

    [Fact]
    public async Task ProgressIsOrderedAndOnlyUnwarnedCompletionReaches100()
    {
        foreach (var warnings in new[] { false, true })
        {
            var fs = new FakeFileSystem(); fs.AddFile("good", 5);
            if (warnings) fs.FailMetadata[fs.AddFile("bad", 1)] = new IOException("Unavailable");
            var updates = new List<ScanProgress>();
            var scan = await Scan(fs, progress: new InlineProgress<ScanProgress>(updates.Add));
            Assert.Null(updates.First().Percent); Assert.Null(updates.First().Remaining);
            Assert.Equal(warnings ? ScanOutcome.Warnings : ScanOutcome.Complete, scan.Outcome);
            Assert.Equal(!warnings, updates.Last().Percent == 100);
            Assert.True(updates.Zip(updates.Skip(1)).All(x => x.First.Elapsed <= x.Second.Elapsed));
        }
    }

    [Fact]
    public void EtaRequiresEnoughMeasuredSamplesAndSmoothesThroughput()
    {
        var rate = new RateEstimator();
        Assert.Null(rate.Update(1, 20, TimeSpan.FromMilliseconds(200)));
        Assert.Null(rate.Update(2, 20, TimeSpan.FromMilliseconds(400)));
        Assert.Null(rate.Update(3, 20, TimeSpan.FromMilliseconds(600)));
        Assert.Null(rate.Update(0, 20, TimeSpan.Zero));
        Assert.NotNull(rate.Update(10, 20, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task PhysicalScanPreservesControlledFilesAndIncludesHiddenContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsa-tests-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, ".hidden")).FullName;
            var file = Path.Combine(folder, "bytes.bin"); await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
            var before = File.GetLastWriteTimeUtc(file);
            var scan = await new ScanService().ScanAsync(root, 0, null, CancellationToken.None);
            Assert.Equal(4, scan.LogicalBytes); Assert.Equal(before, File.GetLastWriteTimeUtc(file));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(file));
            Assert.Equal(ScanOutcome.Complete, scan.Outcome);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PhysicalDirectoryLinkBackToRootDoesNotLoop()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsa-links-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var link = Path.Combine(root, "loop");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "real"), [1, 2, 3]);
            if (OperatingSystem.IsWindows())
            {
                using var command = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
                {
                    ArgumentList = { "/c", "mklink", "/J", link, root },
                    RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                })!;
                await command.WaitForExitAsync(); Assert.Equal(0, command.ExitCode);
            }
            else Directory.CreateSymbolicLink(link, root);
            var scan = await new ScanService().ScanAsync(root, 0, null, CancellationToken.None);
            Assert.Equal(3, scan.LogicalBytes); Assert.Empty(scan.GetFolders());
            Assert.Equal(1, scan.Skipped); Assert.Equal(ScanOutcome.Warnings, scan.Outcome);
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SlowScanReportsElapsedTimeAndThrottlesEntryUpdates()
    {
        var fs = new FakeFileSystem();
        for (var i = 0; i < 50; i++) fs.AddFile(i.ToString(CultureInfo.InvariantCulture), 2);
        fs.OnFile = _ => Thread.Sleep(10);
        var updates = new List<ScanProgress>();
        var scan = await Scan(fs, progress: new InlineProgress<ScanProgress>(updates.Add));
        Assert.True(scan.Duration.TotalMilliseconds >= 400);
        var samples = updates.Where(x => x.Phase == ScanPhase.Analysis).ToArray();
        Assert.InRange(samples.Length, 2, 8); Assert.True(samples.Last().Elapsed > samples.First().Elapsed);
    }
}
