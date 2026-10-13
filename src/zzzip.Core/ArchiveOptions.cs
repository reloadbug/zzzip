namespace zzzip.Core;

public enum CompressionLevelOption
{
    Fast = 0,
    Normal = 1,
    Smallest = 2,
}

/// <summary>压缩/解压参数，字段与 360 的设置对话框一一对应。</summary>
public sealed record ArchiveOptions
{
    public ArchiveFormat Format { get; init; } = FormatRegistry.Zip;

    public CompressionLevelOption Level { get; init; } = CompressionLevelOption.Normal;

    /// <summary>固实压缩：把所有文件当作连续数据流。仅 7Z 有效。</summary>
    public bool Solid { get; init; }

    /// <summary>分卷大小（MB）。null 或 0 表示不分卷。</summary>
    public int? VolumeSizeMb { get; init; }

    public string? Password { get; init; }

    public bool EncryptFileNames { get; init; }

    /// <summary>把压缩包写进目标文件夹内部（路径前缀），用于“添加到文件夹树”。</summary>
    public string? EntryRootPrefix { get; init; }

    public string? Comment { get; init; }

    /// <summary>生成自解压 exe。</summary>
    public bool CreateSfx { get; init; }

    // 解压相关
    public bool KeepArchiveAfterExtract { get; init; } = true;
    public bool OverwriteExisting { get; init; }
    public bool ExtractToSubFolderPerArchive { get; init; } = true;
}

public readonly record struct ArchiveProgress(
    string CurrentFile,
    long TotalBytes,
    long ProcessedBytes,
    int FilesTotal,
    int FilesDone)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)ProcessedBytes / TotalBytes, 0, 1);
    public double FileFraction => FilesTotal <= 0 ? 0 : Math.Clamp((double)FilesDone / FilesTotal, 0, 1);
}