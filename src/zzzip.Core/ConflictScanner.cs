using System.IO;

namespace zzzip.Core;

/// <summary>
/// 解压前的同名文件预检。
///
/// 为什么要在解压前先把冲突全找出来，而不是一边解一边问：
/// 7z 是**一次子进程调用解一整包**，中途没法插进去问用户；而且真实的产品体验
/// 也得先知道「一共几处冲突」才能给出「全部覆盖」这种一键决定。
/// 所以流程固定为：先扫全部冲突 → 一次性问清楚 → 再按决策分组执行。
/// </summary>
public static class ConflictScanner
{
    /// <summary>
    /// 找出所有「解压后会被写到已存在文件上」的条目。
    ///
    /// 目录不参与冲突判定（目录本来就是合并语义，不该拿它烦用户）；
    /// 越界条目（Zip Slip）在这里静默跳过，留给解压阶段统一报错，
    /// 免得错误信息变成两套。
    /// </summary>
    public static IReadOnlyList<FileConflict> Find(
        IReadOnlyList<ArchiveEntry> entries, string destinationDir, Func<ArchiveEntry, bool>? predicate)
    {
        if (entries.Count == 0) return [];

        var root = PathSafety.NormalizeRoot(destinationDir);
        var hits = new List<FileConflict>();

        foreach (var e in entries)
        {
            if (e.IsDirectory) continue;
            if (predicate is not null && !predicate(e)) continue;

            string target;
            try
            {
                target = PathSafety.ResolveEntryPath(root, e.Path);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            try
            {
                var info = new FileInfo(target);
                if (!info.Exists) continue;

                hits.Add(new FileConflict
                {
                    Entry = e,
                    TargetPath = target,
                    ExistingSize = info.Length,
                    ExistingModifiedUtc = info.LastWriteTimeUtc,
                    Index = 0,   // 下面统一回填
                    Total = 0,
                });
            }
            catch
            {
                // 探测失败（路径过长、权限不足等）不算冲突：让解压阶段去报真正的错
            }
        }

        // 两个阶段：Index / Total 需要先知道总数才能填
        var total = hits.Count;
        return total == 0 ? [] : hits.Select((c, i) => c with { Index = i + 1, Total = total }).ToList();
    }
}

/// <summary>
/// 冲突决策闸门：把「这一次问什么」和「以后还用不用问」收敛到一处，
/// 供 ZipEngine 与 SevenZipCliEngine 共用，保证两个内核的行为逐字一致。
/// </summary>
internal sealed class ConflictGate
{
    private readonly IConflictResolver? _resolver;
    private readonly bool _overwriteWhenSilent;
    private ConflictDecision? _sticky;

    public ConflictGate(ArchiveOptions options, IConflictResolver? resolver)
    {
        _resolver = resolver;
        _overwriteWhenSilent = options.OverwriteExisting;
    }

    /// <summary>依次询问每处冲突，返回归一化后的动作（只会是 Overwrite / Skip / Rename）。</summary>
    public ConflictDecision Decide(FileConflict conflict)
    {
        // 上一次用户点了「全部xx」：后面一律沿用，不再打扰
        if (_sticky is { } sticky) return sticky;

        // 没有询问通道（右键静默解压、自动化测试）：沿用选项里的策略，与旧版本行为一致
        if (_resolver is null)
            return _overwriteWhenSilent ? ConflictDecision.Overwrite : ConflictDecision.Skip;

        var decision = _resolver.Resolve(conflict);
        switch (decision)
        {
            case ConflictDecision.OverwriteAll:
                _sticky = ConflictDecision.Overwrite;
                return ConflictDecision.Overwrite;
            case ConflictDecision.SkipAll:
                _sticky = ConflictDecision.Skip;
                return ConflictDecision.Skip;
            case ConflictDecision.RenameAll:
                _sticky = ConflictDecision.Rename;
                return ConflictDecision.Rename;
            case ConflictDecision.Cancel:
                throw new ExtractCancelledException();
            default:
                return decision;
        }
    }
}

/// <summary>给同名文件挑一个不冲突的新名字，风格与资源管理器一致：name (1).ext。</summary>
internal static class UniqueName
{
    public static string For(string targetPath)
    {
        if (!File.Exists(targetPath)) return targetPath;

        var dir = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(targetPath);
        var ext = Path.GetExtension(targetPath);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        // 极端情况（同名文件上万个）：退化成不重复的随机名，保证一定成功
        return Path.Combine(dir, $"{name} ({Guid.NewGuid():N}){ext}");
    }
}
