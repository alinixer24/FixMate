namespace FixMate.Core.Models;

public sealed record StorageVolume(
    string Name,
    string RootPath,
    string FileSystem,
    long TotalBytes,
    long AvailableBytes);

public sealed record StorageFile(
    string Name,
    string FullPath,
    long SizeBytes,
    DateTime LastModified);

public sealed record StorageAnalysisResult(
    string FolderPath,
    int FilesScanned,
    long TotalBytes,
    int SkippedItems,
    IReadOnlyList<StorageFile> LargestFiles);
