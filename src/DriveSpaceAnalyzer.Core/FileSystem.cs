namespace DriveSpaceAnalyzer.Core;

public sealed record FileMetadata(long Length, DateTime ModifiedUtc, FileAttributes Attributes);

/// <summary>Metadata-only surface. No content reads or mutation operations exist here.</summary>
public interface IFileSystem
{
    FileAttributes GetAttributes(string path);
    IEnumerable<string> EnumerateDirectories(string path);
    IEnumerable<string> EnumerateFiles(string path);
    FileMetadata GetFileMetadata(string path);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    private static EnumerationOptions Options => new()
    {
        AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false,
        ReturnSpecialDirectories = false, BufferSize = 64 * 1024
    };
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path, "*", Options);
    public IEnumerable<string> EnumerateFiles(string path) => Directory.EnumerateFiles(path, "*", Options);
    public FileMetadata GetFileMetadata(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("The file disappeared during the scan.", path);
        var attributes = info.Attributes;
        // Never resolve a file link to read metadata from its target.
        return attributes.HasFlag(FileAttributes.ReparsePoint)
            ? new(0, DateTime.MinValue, attributes) : new(info.Length, info.LastWriteTimeUtc, attributes);
    }
}

public interface IDriveProvider { IReadOnlyList<DriveChoice> GetDrives(); }
public sealed class DriveProvider : IDriveProvider
{
    public IReadOnlyList<DriveChoice> GetDrives()
    {
        var result = new List<DriveChoice>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                result.Add(drive.IsReady
                    ? new(drive.Name, drive.VolumeLabel, drive.DriveType.ToString(), true,
                        drive.TotalSize, drive.AvailableFreeSpace, null, drive.TotalFreeSpace)
                    : new(drive.Name, "", drive.DriveType.ToString(), false, null, null, "Drive is not ready or has been disconnected."));
            }
            catch (Exception ex) when (ScanService.IsFileSystemError(ex))
            {
                result.Add(new(drive.Name, "", "Unknown", false, null, null, ex.Message));
            }
        }
        return result;
    }
}
