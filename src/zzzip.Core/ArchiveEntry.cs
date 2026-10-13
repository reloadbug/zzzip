namespace zzzip.Core;

/// <summary>压缩包内的一个条目（文件或目录）。属性名与 UI 列表列绑定保持一致。</summary>
public sealed class ArchiveEntry
{
    /// <summary>包内相对路径，统一使用反斜杠，例如 docs\readme.txt。</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>包内原始条目名（保留 '/'），用于解压时原样还原。</summary>
    public string RawName { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    /// <summary>解压后大小（字节）。</summary>
    public long Size { get; init; }

    /// <summary>压缩后占用（字节）。</summary>
    public long PackedSize { get; init; }

    public DateTimeOffset ModifiedUtc { get; init; }

    /// <summary>是否为加密条目（可读时为 true）。</summary>
    public bool IsEncrypted { get; init; }

    /// <summary>安全评级位：保留给未来的云查杀/本地扫描，本期恒为 Safe。</summary>
    public SafetyRating Safety { get; init; } = SafetyRating.Safe;

    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();

    /// <summary>压缩率，0 表示未压缩或无法计算。</summary>
    public double Ratio => Size <= 0 ? 0 : 1.0 - (double)PackedSize / Size;

    public override string ToString() => Path;
}

public enum SafetyRating
{
    Safe = 0,
    Script = 1,
    Suspicious = 2,
    Malware = 3,
}