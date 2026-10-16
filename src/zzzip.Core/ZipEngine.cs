using System.IO.Compression;

namespace zzzip.Core;

/// <summary>
/// ZIP 内核：基于 .NET 自带 System.IO.Compression，零外部依赖，速度与兼容性足够。
/// 加密 ZIP（AES / ZipCrypto）由 SevenZipEngine 负责，本内核遇到加密包会明确抛错而不是静默失败。
/// </summary>
public sealed class ZipEngine : IArchiveEngine
{
    public string Name => "ZIP(内置)";

    public IReadOnlySet<string> FormatIds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zip" };

    public bool SupportsEncryption => false;
    public bool SupportsSolid => false;          // ZIP 永远是非固实的
    public bool SupportsMultiVolume => false;    // 分卷交给 7z 内核
    public bool SupportsSfx => false;            // SFX 由 7z 内核生成

    public IReadOnlyList<ArchiveEntry> List(string archivePath)
    {
        using var zip = OpenRead(archivePath);
        var list = new List<ArchiveEntry>(zip.Entries.Count);
        foreach (var e in zip.Entries)
        {
            var name = e.FullName;
            var isDir = name.EndsWith('/') || name.EndsWith('\\');
            list.Add(new ArchiveEntry
            {
                Path = name.Replace('/', '\\').TrimEnd('\\'),
                RawName = name,
                IsDirectory = isDir,
                Size = isDir ? 0 : e.Length,
                PackedSize = isDir ? 0 : e.CompressedLength,
                ModifiedUtc = e.LastWriteTime.UtcDateTime,
            });
        }
        return list;
    }

    public void Extract(string archivePath, string destinationDir, ArchiveOptions options,
                        Func<ArchiveEntry, bool>? entryPredicate,
                        IProgress<ArchiveProgress>? progress, CancellationToken ct,
                        IConflictResolver? conflicts = null)
    {
        using var zip = OpenRead(archivePath);
        var root = PathSafety.NormalizeRoot(destinationDir);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

        // 先把同名文件一次性问清楚，再动手解压 —— 与 7z 内核走同一套流程，
        // 免得两个内核在「冲突怎么办」这件事上悄悄跑偏。
        var listings = entries.Select(e => new ArchiveEntry
        {
            Path = e.FullName.Replace('/', '\\').TrimEnd('\\'),
            RawName = e.FullName,
            Size = e.Length,
        }).ToList();

        var hits = ConflictScanner.Find(listings, destinationDir, entryPredicate);
        var gate = new ConflictGate(options, conflicts);
        var decisions = new Dictionary<string, ConflictDecision>(StringComparer.Ordinal);
        foreach (var hit in hits) decisions[hit.Entry.RawName] = gate.Decide(hit);

        long total = entries.Sum(e => e.Length);
        long done = 0;
        int filesDone = 0;

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();

            var probe = new ArchiveEntry
            {
                Path = e.FullName.Replace('/', '\\'),
                RawName = e.FullName,
                Size = e.Length,
            };
            if (entryPredicate is not null && !entryPredicate(probe)) continue;

            var target = PathSafety.ResolveEntryPath(root, e);
            var overwrite = options.OverwriteExisting;

            if (File.Exists(target))
            {
                if (decisions.TryGetValue(e.FullName, out var decision))
                {
                    switch (decision)
                    {
                        case ConflictDecision.Skip:
                            continue;
                        case ConflictDecision.Rename:
                            target = UniqueName.For(target);
                            overwrite = false;
                            break;
                        default:                      // Overwrite
                            overwrite = true;
                            break;
                    }
                }
                else if (!overwrite)
                {
                    continue;                     // 没问到（无询问通道）且不覆盖：保持旧行为
                }
            }
            else
            {
                overwrite = false;                // 目标不存在，不需要覆盖语义
            }

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            e.ExtractToFile(target, overwrite);

            done += e.Length;
            filesDone++;
            progress?.Report(new ArchiveProgress(e.FullName, total, done, entries.Count, filesDone));
        }

        if (!options.KeepArchiveAfterExtract)
        {
            try { File.Delete(archivePath); } catch { }
        }
    }

    public void Create(IEnumerable<string> inputPaths, string outputArchivePath, ArchiveOptions options,
                       IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        var files = Expand(inputPaths, options.EntryRootPrefix).ToList();
        var level = options.Level switch
        {
            CompressionLevelOption.Fast => CompressionLevel.Fastest,
            CompressionLevelOption.Smallest => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputArchivePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        long total = files.Where(f => f.IsFile).Sum(f => new FileInfo(f.LocalPath).Length);
        long done = 0;
        int filesTotal = files.Count(f => f.IsFile);
        int filesDone = 0;

        using (var fs = new FileStream(outputArchivePath, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var item in files)
            {
                ct.ThrowIfCancellationRequested();

                var entry = zip.CreateEntry(item.EntryPath, level);
                entry.LastWriteTime = item.IsFile
                    ? new DateTimeOffset(File.GetLastWriteTime(item.LocalPath))
                    : DateTimeOffset.Now;

                if (item.IsFile)
                {
                    using var src = new FileStream(item.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var dst = entry.Open();
                    CopyWithProgress(src, dst, item.EntryPath, total, filesDone, filesTotal, progress, ct, ref done);
                    filesDone++;
                }
                progress?.Report(new ArchiveProgress(item.EntryPath, total, done, filesTotal, filesDone));
            }
        }

        if (!string.IsNullOrEmpty(options.Comment))
            WriteZipComment(outputArchivePath, options.Comment!);
    }

    private static void CopyWithProgress(Stream src, Stream dst, string name, long total,
                                         int filesDone, int filesTotal,
                                         IProgress<ArchiveProgress>? progress, CancellationToken ct,
                                         ref long done)
    {
        var buffer = new byte[128 * 1024];
        int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            dst.Write(buffer, 0, read);
            done += read;
            progress?.Report(new ArchiveProgress(name, total, done, filesTotal, filesDone));
        }
    }

    /// <summary>把注释写进 EOCD 记录（文件尾部），这是 ZIP 规范存放注释的位置。</summary>
    private static void WriteZipComment(string path, string comment)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(comment);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            long len = fs.Length;
            int tailLen = (int)Math.Min(len, 66_000);
            var tail = new byte[tailLen];
            fs.Seek(-tailLen, SeekOrigin.End);
            fs.ReadExactly(tail);

            long eocd = -1;
            for (int i = tailLen - 22; i >= 0; i--)
            {
                if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
                {
                    eocd = len - tailLen + i;
                    break;
                }
            }
            if (eocd < 0) return;

            fs.Seek(eocd + 20, SeekOrigin.Begin);
            fs.Write(BitConverter.GetBytes((ushort)bytes.Length));
            fs.Write(bytes);
            fs.SetLength(fs.Position);
        }
        catch
        {
            // 注释写入失败不影响压缩包本体，静默忽略
        }
    }

    public bool Test(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(password))
            throw new NotSupportedException("ZIP 内核不支持加密包校验，请改用 7Z 内核。");

        using var zip = OpenRead(archivePath);
        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        long total = files.Sum(e => e.Length);
        long done = 0;
        int i = 0;

        foreach (var e in files)
        {
            ct.ThrowIfCancellationRequested();
            using var s = e.Open();
            s.CopyTo(Stream.Null);
            done += e.Length;
            i++;
            progress?.Report(new ArchiveProgress(e.FullName, total, done, files.Count, i));
        }
        return true;
    }

    private static ZipArchive OpenRead(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("压缩包不存在", path);
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return new ZipArchive(fs, ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            fs.Dispose();
            throw new InvalidDataException($"无法打开压缩包（可能已损坏或为加密包）：{Path.GetFileName(path)}", ex);
        }
    }

    /// <summary>把输入路径展开为待打包文件清单；带 rootPrefix 时把顶层目录名写进包内路径。</summary>
    private static IEnumerable<PackItem> Expand(IEnumerable<string> inputPaths, string? rootPrefix)
    {
        foreach (var p in inputPaths)
        {
            if (Directory.Exists(p))
            {
                var dirName = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var prefix = string.IsNullOrEmpty(rootPrefix) ? dirName : Path.Combine(rootPrefix, dirName);
                foreach (var f in EnumerateSafe(p))
                {
                    var rel = Path.GetRelativePath(p, f);
                    yield return new PackItem(f, true, Path.Combine(prefix, rel).Replace('\\', '/'));
                }
            }
            else if (File.Exists(p))
            {
                yield return new PackItem(p, true, Path.GetFileName(p));
            }
        }
    }

    private static IEnumerable<string> EnumerateSafe(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var f in files) yield return f;
            foreach (var d in dirs) stack.Push(d);
        }
    }

    private sealed record PackItem(string LocalPath, bool IsFile, string EntryPath);
}