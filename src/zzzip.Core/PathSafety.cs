using System.IO.Compression;

namespace zzzip.Core;

/// <summary>解压路径安全：阻断 Zip Slip（../）与绝对路径写入。</summary>
public static class PathSafety
{
    public static string NormalizeRoot(string root)
    {
        var full = Path.GetFullPath(root);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }

    /// <summary>把包内条目名映射为安全的本地绝对路径；越界则抛异常。</summary>
    public static string ResolveEntryPath(string rootDir, ZipArchiveEntry entry)
    {
        var normalizedRoot = NormalizeRoot(rootDir);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, entry.FullName));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"压缩包条目越界，已阻止解压：{entry.FullName}");
        return candidate;
    }

    public static string ResolveEntryPath(string rootDir, string entryName)
    {
        var normalizedRoot = NormalizeRoot(rootDir);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, entryName));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"压缩包条目越界，已阻止解压：{entryName}");
        return candidate;
    }

    /// <summary>校验解压目标磁盘剩余空间是否足够。</summary>
    public static bool HasEnoughFreeSpace(string targetDir, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(targetDir));
            if (string.IsNullOrEmpty(root)) return true;
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace >= requiredBytes;
        }
        catch
        {
            return true; // 取不到就放行，不因为探测失败阻断用户
        }
    }
}