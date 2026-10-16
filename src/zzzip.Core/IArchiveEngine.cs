namespace zzzip.Core;

/// <summary>压缩内核抽象。ZIP 由系统库实现，7Z/RAR 等由 7-Zip DLL 实现（见 SevenZipEngine）。</summary>
public interface IArchiveEngine
{
    string Name { get; }

    /// <summary>该内核负责的格式 ID 集合。</summary>
    IReadOnlySet<string> FormatIds { get; }

    bool SupportsEncryption { get; }
    bool SupportsSolid { get; }
    bool SupportsMultiVolume { get; }
    bool SupportsSfx { get; }

    /// <summary>读取条目列表，不解压内容。</summary>
    IReadOnlyList<ArchiveEntry> List(string archivePath);

    /// <summary>把包内条目解压到目标目录。</summary>
    /// <param name="entryPredicate">返回 true 的条目才被解压；null 表示全部。</param>
    /// <param name="conflicts">
    /// 目标目录已存在同名文件时的询问通道。null 表示不询问，按
    /// <see cref="ArchiveOptions.OverwriteExisting"/> 静默处理（右键后台解压与自动化测试走这条路）。
    /// 传了它就一定整包先扫一遍冲突、再动手解压。
    /// </param>
    void Extract(string archivePath, string destinationDir, ArchiveOptions options,
                 Func<ArchiveEntry, bool>? entryPredicate,
                 IProgress<ArchiveProgress>? progress, CancellationToken ct,
                 IConflictResolver? conflicts = null);

    /// <summary>创建压缩包。</summary>
    void Create(IEnumerable<string> inputPaths, string outputArchivePath, ArchiveOptions options,
                IProgress<ArchiveProgress>? progress, CancellationToken ct);

    /// <summary>完整性测试（有密码时传 password）。</summary>
    bool Test(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken ct);
}