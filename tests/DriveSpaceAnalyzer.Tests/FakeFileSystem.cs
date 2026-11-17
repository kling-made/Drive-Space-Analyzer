using DriveSpaceAnalyzer.Core;

namespace DriveSpaceAnalyzer.Tests;
internal sealed class FakeFileSystem : IFileSystem
{
    public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dsa-fake"));
    public Dictionary<string, List<string>> Directories { get; } = [];
    public Dictionary<string, List<string>> Files { get; } = [];
    public Dictionary<string, FileMetadata> Metadata { get; } = [];
    public Dictionary<string, FileAttributes> Attributes { get; } = [];
    public Dictionary<string, Exception> FailDiscovery { get; } = [];
    public Dictionary<string, Exception> FailAnalysis { get; } = [];
    public Dictionary<string, Exception> FailMetadata { get; } = [];
    public string? FailDiscoveryAfterYield { get; set; }
    public int EnumerationCalls { get; private set; }
    public Action<string>? OnFile { get; set; }
    public FakeFileSystem() { Directories[Root] = []; Files[Root] = []; Attributes[Root] = FileAttributes.Directory; }
    public string AddDirectory(string relative)
    {
        var path = Path.Combine(Root, relative);
        if (Directories.ContainsKey(path)) return path;
        var parent = Path.GetDirectoryName(path)!;
        if (!Directories.ContainsKey(parent)) AddDirectory(Path.GetRelativePath(Root, parent));
        Directories[parent].Add(path); Directories[path] = []; Files[path] = []; Attributes[path] = FileAttributes.Directory;
        return path;
    }
    public string AddFile(string relative, long length, FileAttributes attributes = FileAttributes.Normal)
    {
        var path = Path.Combine(Root, relative); var parent = Path.GetDirectoryName(path)!;
        if (!Directories.ContainsKey(parent)) AddDirectory(Path.GetRelativePath(Root, parent));
        Files[parent].Add(path); Metadata[path] = new(length, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), attributes);
        Attributes[path] = attributes; return path;
    }
    public FileAttributes GetAttributes(string path) => Attributes.TryGetValue(path, out var value)
        ? value : throw new DirectoryNotFoundException(path);
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        EnumerationCalls++;
        if (FailDiscovery.TryGetValue(path, out var ex)) throw ex;
        foreach (var child in Directories[path]) yield return child;
        if (path == FailDiscoveryAfterYield) throw new IOException("Enumeration failed after an entry.");
    }
    public IEnumerable<string> EnumerateFiles(string path)
    {
        EnumerationCalls++;
        if (FailAnalysis.TryGetValue(path, out var ex)) throw ex;
        foreach (var file in Files[path]) { OnFile?.Invoke(file); yield return file; }
    }
    public FileMetadata GetFileMetadata(string path) => FailMetadata.TryGetValue(path, out var ex) ? throw ex : Metadata[path];
}
internal sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
