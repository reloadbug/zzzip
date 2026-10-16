namespace zzzip.Core;

/// <summary>
/// 内核路由：按格式挑选可用内核，并屏蔽 7z.exe 缺失的情况。
///
/// 策略：
///  1. 7z.exe 可用 → 全部格式走 SevenZipCliEngine（ZIP 也走它，可写 AES-256 加密 zip）；
///  2. 7z.exe 缺失 → 纯 ZIP 走内置 ZipEngine 兜底，其余格式给出明确中文提示而不是崩溃；
///  3. 需要加密但只剩内置内核时直接抛可读异常，说明原因。
/// </summary>
public static class ArchiveEngineRouter
{
    private static SevenZipCliEngine? _sevenZip;

    /// <summary>7-Zip 内核是否可用（决定 UI 是否显示加密/固实/分卷/SFX 等选项）。</summary>
    public static bool HasSevenZip => _sevenZip is not null;

    /// <summary>引擎状态文案，用于主界面右下角（对应 360 的“双核引擎”提示位）。</summary>
    public static string EngineSummary => _sevenZip is null
        ? "ZIP(内置) · 未检测到 7-Zip 引擎"
        : "7-Zip(7z.exe) + ZIP(内置) 双核";

    public static void Configure(string? sevenZipExePath)
    {
        var path = sevenZipExePath ?? SevenZipCliEngine.LocateExecutable();
        _sevenZip = path is null ? null : new SevenZipCliEngine(path);
    }

    /// <summary>首次使用时自动探测一次。</summary>
    public static void EnsureConfigured() => _sevenZip ??= Probe();

    private static SevenZipCliEngine? Probe()
    {
        var path = SevenZipCliEngine.LocateExecutable();
        return path is null ? null : new SevenZipCliEngine(path);
    }

    public static IArchiveEngine ForExtract(string archivePath)
    {
        EnsureConfigured();
        if (_sevenZip is not null) return _sevenZip;

        if (FormatRegistry.Resolve(archivePath)?.Id == "zip") return new ZipEngine();
        throw new NotSupportedException(
            "该格式需要 7-Zip 引擎支持。请把 7z.exe 放到 zzzip.exe 同目录后重试。");
    }

    public static IArchiveEngine ForCreate(ArchiveOptions options)
    {
        EnsureConfigured();
        if (_sevenZip is not null) return _sevenZip;

        if (options.Format.Id == "zip" && string.IsNullOrEmpty(options.Password))
            return new ZipEngine();

        throw new NotSupportedException(
            "该压缩选项需要 7-Zip 引擎支持。请把 7z.exe 放到 zzzip.exe 同目录后重试。");
    }

    // ---- 便捷门面：UI 与右键菜单直接调用，无需自己判断用哪个内核 ----

    public static IReadOnlyList<ArchiveEntry> List(string archivePath) =>
        ForExtract(archivePath).List(archivePath);

    public static void Extract(string archivePath, string destinationDir, ArchiveOptions options,
                               Func<ArchiveEntry, bool>? entryPredicate,
                               IProgress<ArchiveProgress>? progress, CancellationToken ct,
                               IConflictResolver? conflicts = null)
    {
        var engine = ForExtract(archivePath);
        if (!string.IsNullOrEmpty(options.Password) && !engine.SupportsEncryption)
            throw new NotSupportedException("当前仅内置 ZIP 内核，无法解压加密包。");
        engine.Extract(archivePath, destinationDir, options, entryPredicate, progress, ct, conflicts);
    }

    public static void Create(IEnumerable<string> inputPaths, string outputPath, ArchiveOptions options,
                              IProgress<ArchiveProgress>? progress, CancellationToken ct) =>
        ForCreate(options).Create(inputPaths, outputPath, options, progress, ct);

    public static bool Test(string archivePath, string? password,
                            IProgress<ArchiveProgress>? progress, CancellationToken ct) =>
        ForExtract(archivePath).Test(archivePath, password, progress, ct);

    // ---- 右键菜单语义化动作（与 360 菜单项一一对应） ----

    /// <summary>解压到“压缩包名\”同名文件夹（360 默认右键项）。</summary>
    /// <param name="conflicts">同名文件询问通道；传了就逐个问，null 则按选项静默处理。</param>
    public static void ExtractToOwnFolder(string archivePath, IProgress<ArchiveProgress>? progress, CancellationToken ct,
                                          IConflictResolver? conflicts = null)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? "";
        var target = Path.Combine(parent, Path.GetFileNameWithoutExtension(archivePath));
        Directory.CreateDirectory(target);
        Extract(archivePath, target, new ArchiveOptions(), null, progress, ct, conflicts);
    }

    /// <summary>解压到压缩包所在目录（360 的“解压到当前文件夹”）。</summary>
    /// <param name="conflicts">同名文件询问通道；传了就逐个问，null 则按选项静默处理。</param>
    public static void ExtractToHere(string archivePath, IProgress<ArchiveProgress>? progress, CancellationToken ct,
                                     IConflictResolver? conflicts = null)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? "";
        Extract(archivePath, parent, new ArchiveOptions { ExtractToSubFolderPerArchive = false },
                null, progress, ct, conflicts);
    }

    /// <summary>解压到指定目录（360 的“解压到指定文件夹…”）。</summary>
    /// <param name="conflicts">同名文件询问通道；传了就逐个问，null 则按选项静默处理。</param>
    public static void ExtractToDirectory(string archivePath, string targetDir,
                                          IProgress<ArchiveProgress>? progress, CancellationToken ct,
                                          IConflictResolver? conflicts = null)
    {
        Directory.CreateDirectory(targetDir);
        Extract(archivePath, targetDir, new ArchiveOptions(), null, progress, ct, conflicts);
    }

    /// <summary>压缩到所在目录输出 xxx.zip（360 的“添加到&quot;文件名.zip&quot;”）。</summary>
    public static void CreateQuickZip(IEnumerable<string> inputs, string? presetName,
                                      IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        var list = inputs.ToList();
        var dir = list.Count > 0 ? Path.GetDirectoryName(Path.GetFullPath(list[0])) ?? "" : "";
        var output = Path.Combine(dir, (presetName ?? QuickName(list)) + ".zip");
        Create(list, output, new ArchiveOptions { Format = FormatRegistry.Zip }, progress, ct);
    }

    /// <summary>高压缩比：输出 xxx.7z 且默认固实（360 的“添加到高压缩比文件xx.7z”）。</summary>
    public static void CreateHighRatio7z(IEnumerable<string> inputs, string? presetName,
                                         IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        var list = inputs.ToList();
        var dir = list.Count > 0 ? Path.GetDirectoryName(Path.GetFullPath(list[0])) ?? "" : "";
        var output = Path.Combine(dir, (presetName ?? QuickName(list)) + ".7z");
        Create(list, output, new ArchiveOptions
        {
            Format = FormatRegistry.SevenZ,
            Level = CompressionLevelOption.Smallest,
            Solid = true,
        }, progress, ct);
    }

    /// <summary>压缩每个文件到单独的压缩包中（360 的同名右键项）。</summary>
    public static IReadOnlyList<string> CreateEachSeparately(IEnumerable<string> inputs,
        Func<string, string> resolveDir, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        var outputs = new List<string>();
        foreach (var item in inputs)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(item)) continue; // 只逐个处理文件；文件夹按整目录压一个包
            var outPath = Path.Combine(resolveDir(item), Path.GetFileName(item) + ".zip");
            Create([item], outPath, new ArchiveOptions { Format = FormatRegistry.Zip }, progress, ct);
            outputs.Add(outPath);
        }
        return outputs;
    }

    private static string QuickName(List<string> inputs)
    {
        if (inputs.Count == 0) return "新建压缩包";
        if (inputs.Count == 1)
        {
            var p = inputs[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var n = Path.GetFileName(p);
            return string.IsNullOrEmpty(n) ? "新建压缩包" : n;
        }
        // 多个文件：全部同属一个目录时用目录名，否则用通用名
        var common = Path.GetDirectoryName(Path.GetFullPath(inputs[0]));
        var allSame = inputs.All(i =>
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(i)), common, StringComparison.OrdinalIgnoreCase));
        return allSame && !string.IsNullOrEmpty(common) ? Path.GetFileName(common) : "新建压缩包";
    }
}