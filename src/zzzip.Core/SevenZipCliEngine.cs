using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace zzzip.Core;

/// <summary>
/// 7-Zip 内核（CLI 方式）：驱动官方 7z.exe 进程，覆盖除纯 ZIP 外的全部格式
/// （7Z/RAR/ISO/TAR/GZ/BZ2/XZ/ZSTD/CAB/ARJ/LZH/UUE/WIM/JAR），并提供
/// AES-256 加密、固实压缩、分卷、SFX 自解压等 ZIP 内核做不到的能力。
///
/// 选型说明：不用手写 7z.dll 的 COM vtable interop —— 槽位序号随版本漂移、
/// PROPVARIANT 跨进程 BSTR 释放极易崩。改用官方 CLI：
///   1) 行为与 7-Zip 完全一致，40+ 格式零维护；
///   2) 崩溃被隔离在子进程内，不会带走主程序；
///   3) ProcessStartInfo.ArgumentList 传参，天然免疫引号/中文/空格路径转义问题。
/// </summary>
public sealed class SevenZipCliEngine : IArchiveEngine
{
    private static readonly Regex PercentRx = new(@"^\s*(\d{1,3})\s*%", RegexOptions.Compiled);

    private static string? _cachedExe;
    private static bool _probed;

    private readonly StringBuilder _stdout = new();
    private readonly string _exe;

    public SevenZipCliEngine(string exePath) => _exe = exePath;

    public string Name => "7-Zip(7z.exe)";

    public IReadOnlySet<string> FormatIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "rar", "tar", "gzip", "bzip2", "xz", "zstd", "iso",
        "cab", "arj", "lzh", "uue", "wim", "jar",
    };

    public bool SupportsEncryption => true;
    public bool SupportsSolid => true;
    public bool SupportsMultiVolume => true;
    public bool SupportsSfx => true;

    public static bool IsAvailable => LocateExecutable() is not null;

    /// <summary>环境变量：显式禁用 7-Zip 引擎，只保留内置 ZIP 内核。</summary>
    /// <remarks>
    /// 两个用途：① 机器上的 7z.exe 损坏、或用户就是想只用内置内核时可以强制降级；
    /// ② 自动化测试用它模拟「7z.exe 缺失」，不必真去删文件（也就不会污染开发机）。
    /// 它优先于探测缓存，设置后立即生效，不需要重启进程。
    /// </remarks>
    private const string DisableEnvVar = "ZZZIP_DISABLE_7Z";

    public static bool IsDisabledByUser
    {
        get
        {
            var v = Environment.GetEnvironmentVariable(DisableEnvVar);
            if (string.IsNullOrWhiteSpace(v)) return false;
            return v == "1"
                || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>按优先级查找 7z.exe：环境变量 → exe 同级 → PATH → 常见安装目录。</summary>
    public static string? LocateExecutable()
    {
        // 禁用开关要优先于缓存判断：否则一旦探测过一次，后面再设开关就不生效了
        if (IsDisabledByUser) return null;

        if (_probed) return _cachedExe;
        _probed = true;

        var candidates = new List<string>();

        var fromEnv = Environment.GetEnvironmentVariable("ZZZIP_7Z");
        if (!string.IsNullOrWhiteSpace(fromEnv)) candidates.Add(fromEnv);

        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "7z.exe"));
        candidates.Add(Path.Combine(baseDir, "7z", "7z.exe"));
        candidates.Add(Path.Combine(baseDir, "native", "7z", "7z.exe"));
        candidates.Add(Path.Combine(baseDir, "..", "native", "7z", "7z.exe"));

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir.Trim(), "7z.exe"));

        foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                   Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "7-Zip", "7z.exe"));
        }

        _cachedExe = candidates
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Select(p => Path.GetFullPath(p))
            .FirstOrDefault();

        return _cachedExe;
    }

    public IReadOnlyList<ArchiveEntry> List(string archivePath) => ListInternal(archivePath, null);

    /// <summary>
    /// 列出条目。加密包（尤其带 -mhe=on 的）必须带密码才能列出文件名。
    /// 密码仅在命令行内部传递，不写入任何日志。
    /// </summary>
    internal IReadOnlyList<ArchiveEntry> ListInternal(string archivePath, string? password)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException("压缩包不存在", archivePath);

        var args = new List<string> { "l", "-slt", "-sccUTF-8", "-y" };
        if (!string.IsNullOrEmpty(password)) args.Add("-p" + password);
        args.Add(archivePath);

        var code = Run(args, null, out var err);
        if (code != 0) throw new InvalidDataException(ErrorMessage(err, archivePath));

        return ParseListing(_stdout.ToString(), archivePath);
    }

    public void Extract(string archivePath, string destinationDir, ArchiveOptions options,
                        Func<ArchiveEntry, bool>? entryPredicate,
                        IProgress<ArchiveProgress>? progress, CancellationToken ct,
                        IConflictResolver? conflicts = null)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException("压缩包不存在", archivePath);
        Directory.CreateDirectory(destinationDir);

        var all = ListInternal(archivePath, options.Password);
        var wanted = entryPredicate is null ? all : all.Where(entryPredicate).ToList();
        var partial = entryPredicate is not null && wanted.Count < all.Count;
        long total = wanted.Where(e => !e.IsDirectory).Sum(e => e.Size);

        // ---- 1. 同名文件预检 ------------------------------------------------
        // 7z 一次调用解一整包，中途没法插进去问用户，所以只能先把冲突全找出来、
        // 一次问清楚，再按「覆盖 / 跳过 / 改名」把条目分组，分几趟执行。
        var hits = ConflictScanner.Find(wanted, destinationDir, null);
        var gate = new ConflictGate(options, conflicts);
        var decisions = new Dictionary<string, ConflictDecision>(StringComparer.Ordinal);
        foreach (var hit in hits) decisions[hit.Entry.RawName] = gate.Decide(hit);

        var toOverwrite = new List<string>();
        var toRename = new List<FileConflict>();
        foreach (var hit in hits)
        {
            switch (decisions[hit.Entry.RawName])
            {
                case ConflictDecision.Overwrite: toOverwrite.Add(hit.Entry.RawName); break;
                case ConflictDecision.Rename: toRename.Add(hit); break;
            }
        }

        // ---- 2. 第一趟：把不冲突的部分解出来 ---------------------------------
        // 有冲突时用 -aos：盘上已存在的文件会被整体跳过，正好把随后要单独处理的那批留下。
        // 没有冲突时沿用选项里的策略，与 1.0.3 以前的行为完全一致。
        var baseMode = hits.Count == 0 && options.OverwriteExisting ? "-aoa" : "-aos";
        var fileCount = wanted.Count(e => !e.IsDirectory);
        var everythingConflicts = hits.Count > 0 && hits.Count == fileCount;

        if (!everythingConflicts)
        {
            var names = partial ? wanted.Select(e => e.RawName).ToList() : null;
            ExtractInBatches(archivePath, destinationDir, options, names, baseMode, progress, total, ct);
        }

        // ---- 3. 第二趟：用户选「覆盖」的那批，单独用 -aoa 覆盖写入 -----------
        if (toOverwrite.Count > 0)
            ExtractInBatches(archivePath, destinationDir, options, toOverwrite, "-aoa", null, 0, ct);

        // ---- 4. 第三趟：用户选「重命名」的那批 -------------------------------
        if (toRename.Count > 0)
            RenameConflicts(archivePath, destinationDir, options, toRename, ct);

        if (!options.KeepArchiveAfterExtract)
        {
            try { File.Delete(archivePath); } catch { /* 删除失败不阻断 */ }
        }
    }

    /// <summary>
    /// 用户选了「重命名」的那批：7z 没法「解压成另一个名字」，所以先原样解到一个临时目录，
    /// 再搬到 <c>name (1).ext</c>。临时目录放在目标目录内部 —— 必须同卷，
    /// 跨卷的 File.Move 会直接失败。
    /// </summary>
    private void RenameConflicts(string archivePath, string destinationDir, ArchiveOptions options,
                                 IReadOnlyList<FileConflict> hits, CancellationToken ct)
    {
        var staging = Path.Combine(destinationDir, ".zzzip-tmp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        try
        {
            ExtractInBatches(archivePath, staging, options,
                             hits.Select(h => h.Entry.RawName).ToList(), "-aoa", null, 0, ct);

            foreach (var hit in hits)
            {
                var source = Path.Combine(staging, hit.Entry.Path);
                if (!File.Exists(source)) continue;

                var target = UniqueName.For(hit.TargetPath);
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Move(source, target);
            }
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { /* 清理失败不影响解压结果 */ }
        }
    }

    /// <summary>按批调 7z。条目名太多时会撞上命令行长度上限，所以切开跑。</summary>
    private void ExtractInBatches(string archivePath, string outDir, ArchiveOptions options,
                                  IReadOnlyList<string>? entryNames, string mode,
                                  IProgress<ArchiveProgress>? progress, long total, CancellationToken ct)
    {
        // names 为 null = 整包解压，此时按铁律不传任何条目名
        if (entryNames is null)
        {
            ExtractPass(archivePath, outDir, options, null, mode, progress, total, ct);
            return;
        }

        var batches = Batch(entryNames).ToList();
        for (var i = 0; i < batches.Count; i++)
        {
            // 只有一批时才把进度接出去：多批时百分比会一次次归零，反而像卡住了
            ExtractPass(archivePath, outDir, options, batches[i], mode,
                        batches.Count == 1 ? progress : null, total, ct);
        }
    }

    /// <summary>调一次 <c>7z x</c>。</summary>
    private void ExtractPass(string archivePath, string outDir, ArchiveOptions options,
                             IReadOnlyList<string>? entryNames, string mode,
                             IProgress<ArchiveProgress>? progress, long total, CancellationToken ct)
    {
        var args = new List<string> { "x", "-y", "-sccUTF-8", "-bsp1", "-o" + outDir };
        if (!string.IsNullOrEmpty(options.Password)) args.Add("-p" + options.Password);
        args.Add(mode);

        // 7z 的语法是：7z x [开关…] <归档名> [条目名…] —— 归档名必须排在条目名之前。
        // 顺序反了的话，7z 会把第一个条目名当作归档名去找，报「系统找不到指定的文件」
        // 并返回 2。包内带目录条目时必现（例如 ~modsdd/BlueprintLoader/x.pak），
        // 表现为「解压到当前文件夹」直接失败。
        args.Add(archivePath);

        // 全部解压时不传任何条目名 —— 7z 默认就是全解，多传只会被包内名字的写法差异坑到。
        // 只有「部分选择」时才逐个指定，避免动到用户没勾的条目。
        if (entryNames is not null) args.AddRange(entryNames);

        var code = Run(args, MakeProgressLineHandler(progress, total), out var err, ct);
        if (code != 0) throw new InvalidDataException(ErrorMessage(err, archivePath));
    }

    /// <summary>把条目名切成若干批，避开命令行长度上限（Windows 是 32767 字符，这里留足余量）。</summary>
    private static IEnumerable<List<string>> Batch(IReadOnlyList<string> names, int maxChars = 8000)
    {
        var current = new List<string>();
        var length = 0;

        foreach (var name in names)
        {
            if (current.Count > 0 && length + name.Length + 1 > maxChars)
            {
                yield return current;
                current = new List<string>();
                length = 0;
            }
            current.Add(name);
            length += name.Length + 1;
        }

        if (current.Count > 0) yield return current;
    }

    public void Create(IEnumerable<string> inputPaths, string outputArchivePath, ArchiveOptions options,
                       IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        var inputs = inputPaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (inputs.Count == 0) throw new InvalidOperationException("没有可压缩的文件。");

        var fmt = options.Format.Id switch
        {
            "7z" => "7z",
            "tar" => "tar",
            "gzip" => "gzip",
            _ => "zip",
        };

        var outDir = Path.GetDirectoryName(Path.GetFullPath(outputArchivePath));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        if (File.Exists(outputArchivePath)) File.Delete(outputArchivePath);

        var args = new List<string> { "a", "-y", "-sccUTF-8", "-bsp1", "-t" + fmt, "-mx" + LevelArg(options.Level) };

        // 固实压缩仅 7Z 支持（ZIP 永远是非固实的）
        if (options.Solid && fmt == "7z") args.Add("-ms=on");
        if (options.VolumeSizeMb is > 0) args.Add("-v" + options.VolumeSizeMb.Value + "m");
        if (!string.IsNullOrEmpty(options.Password))
        {
            args.Add("-p" + options.Password);
            if (fmt == "7z") args.Add("-mhe=on"); // 连文件名一起加密
        }
        if (options.CreateSfx) args.Add("-sfx");

        // 7z 语法：7z a [开关] 压缩包名 [输入文件…]
        // 压缩包名必须在输入文件之前，否则 7z 会把第一个输入当成压缩包名而报错。
        args.Add(outputArchivePath);
        args.AddRange(inputs);

        var total = InputsSize(inputs);
        var code = Run(args, MakeProgressLineHandler(progress, total), out var err, ct);
        if (code != 0) throw new IOException(ErrorMessage(err, outputArchivePath));
    }

    public bool Test(string archivePath, string? password,
                     IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        var args = new List<string> { "t", "-y", "-sccUTF-8", "-bsp1" };
        if (!string.IsNullOrEmpty(password)) args.Add("-p" + password);
        args.Add(archivePath);

        return Run(args, MakeProgressLineHandler(progress, 0), out _, ct) == 0;
    }

    // ---------- 内部实现 ----------

    private static string LevelArg(CompressionLevelOption level) => level switch
    {
        CompressionLevelOption.Fast => "1",
        CompressionLevelOption.Smallest => "9",
        _ => "5",
    };

    private static long InputsSize(IEnumerable<string> inputs)
    {
        long sum = 0;
        foreach (var p in inputs)
        {
            if (File.Exists(p)) { sum += new FileInfo(p).Length; continue; }
            var stack = new Stack<string>();
            stack.Push(p);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                try
                {
                    foreach (var f in Directory.GetFiles(d)) sum += new FileInfo(f).Length;
                    foreach (var sub in Directory.GetDirectories(d)) stack.Push(sub);
                }
                catch (UnauthorizedAccessException) { /* 跳过无权限目录 */ }
            }
        }
        return sum;
    }

    /// <summary>解析 `7z l -slt` 输出：空行分隔条目块；首块是压缩包自身信息，需过滤。</summary>
    internal static IReadOnlyList<ArchiveEntry> ParseListing(string text, string archivePath)
    {
        var result = new List<ArchiveEntry>();

        string? path = null;
        string? size = null;
        string? packed = null;
        string? modified = null;
        bool isDir = false;
        bool encrypted = false;
        bool hasDirKey = false;
        bool hasSizeKey = false;

        void Flush()
        {
            // 只接受含 Folder 或 Size 键的块：压缩包自身的属性块两者都没有
            if (path is not null && (hasDirKey || hasSizeKey))
            {
                result.Add(new ArchiveEntry
                {
                    Path = path.Replace('/', '\\').TrimEnd('\\'),
                    RawName = path,
                    IsDirectory = isDir,
                    Size = isDir ? 0 : ParseLong(size),
                    PackedSize = isDir ? 0 : ParseLong(packed),
                    ModifiedUtc = ParseTime(modified),
                    IsEncrypted = encrypted,
                });
            }
            path = size = packed = modified = null;
            isDir = encrypted = hasDirKey = hasSizeKey = false;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) { Flush(); continue; }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "Path": path = val; break;
                case "Folder": isDir = val == "+"; hasDirKey = true; break;
                case "Size": size = val; hasSizeKey = true; break;
                case "Packed Size": packed = val; break;
                case "Modified": modified = val; break;
                case "Encrypted": encrypted = val is "+" or "AES"; break;
            }
        }
        Flush();

        if (result.Count == 0)
            throw new InvalidDataException($"无法读取压缩包内容：{Path.GetFileName(archivePath)}");
        return result;
    }

    private static long ParseLong(string? s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static DateTimeOffset ParseTime(string? s)
    {
        if (string.IsNullOrEmpty(s)) return default;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : default;
    }

    /// <summary>把 `-bsp1` 的百分比行翻译成 ArchiveProgress 回调。</summary>
    private static Action<string>? MakeProgressLineHandler(IProgress<ArchiveProgress>? progress, long total)
    {
        if (progress is null) return null;
        return line =>
        {
            var m = PercentRx.Match(line);
            if (!m.Success) return;
            if (!int.TryParse(m.Groups[1].Value, out var pct)) return;
            pct = Math.Clamp(pct, 0, 100);
            var doneBytes = total <= 0 ? 0 : (long)(total * pct / 100.0);
            progress.Report(new ArchiveProgress(line.Trim(), total, doneBytes, pct, pct));
        };
    }

    private int Run(List<string> args, Action<string>? onLine, out string errorText, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(_exe)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var err = new StringBuilder();
        _stdout.Clear();

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _stdout.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) err.AppendLine(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            errorText = "无法启动 7z.exe：" + ex.Message;
            return -1;
        }

        // 关键：立即关闭 7z 的标准输入。
        // 否则当密码缺失、需要覆盖确认等情况下，7z 会交互式索要输入并永久阻塞，
        // 表现为「解压加密包时程序卡死」。关闭 stdin 后 7z 读到 EOF 会直接报错返回。
        try { proc.StandardInput.Close(); } catch { /* 已关闭或无法关闭 */ }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        while (!proc.WaitForExit(200))
        {
            if (ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                ct.ThrowIfCancellationRequested();
            }
        }

        proc.WaitForExit();
        errorText = err.ToString();
        return proc.ExitCode;
    }

    /// <summary>
    /// 把 7z 的 stderr 整理成能照着做的提示。
    ///
    /// 7z 的错误文本有两种形态：
    ///   ERROR: Cannot delete output file : 另一个程序正在使用此文件，进程无法访问。 : D:\...\a.pak
    ///   ERROR: D:\...\a.pak
    ///   Cannot create file : Access is denied.
    /// 只取第一行会把真正的原因（第 2 行起）丢掉 —— 用户看到的就只有「处理失败：ERROR: 某文件」，
    /// 既不知道出了什么事，也不知道该怎么办。所以这里保留全部行，并对常见原因给中文说明。
    /// </summary>
    private static string ErrorMessage(string stderr, string archivePath)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim())
                          .Where(s => s.Length > 0
                                      && !s.StartsWith("7-Zip", StringComparison.OrdinalIgnoreCase))
                          .ToList();

        var head = $"{Path.GetFileName(archivePath)} 处理失败";
        if (lines.Count == 0) return head + "：未知错误（7-Zip 没有返回错误详情）";

        var raw = string.Join(" ", lines);
        return Explain(raw) is { } hint ? $"{head}：{hint}" : $"{head}：{raw}";
    }

    /// <summary>
    /// 把 7z 的英文/系统错误翻成可操作的中文建议；认不出来时返回 null，
    /// 让调用方把 7z 的原文完整透出去（宁可啰嗦，也别把线索藏起来）。
    /// </summary>
    private static string? Explain(string raw)
    {
        bool Has(params string[] keys) =>
            keys.Any(k => raw.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (Has("Cannot delete output file", "正在使用此文件", "being used by another process"))
            return "目标文件正被其他程序占用，请关闭正在使用它的程序（例如游戏、编辑器）后重试，"
                 + "或勾选「覆盖已存在的文件」。";
        if (Has("Access is denied", "拒绝访问"))
            return "目标目录没有写入权限。请确认该目录可写，或改用「解压到指定文件夹」换个位置。";
        if (Has("too long", "路径过长"))
            return "路径过长，请把压缩包解压到层级更浅的目录。";
        if (Has("not enough space", "磁盘空间"))
            return "磁盘空间不足。";
        if (Has("CRC", "Data Error", "数据错误"))
            return "数据校验失败，压缩包可能已损坏或没有下载完整。";
        if (Has("Wrong password", "密码错误", "Cannot open encrypted"))
            return "密码错误，或该压缩包已加密。";
        if (Has("Cannot open file as archive"))
            return "这不是有效的压缩包，或文件已经损坏。";
        if (Has("Cannot create file", "Cannot open output file"))
            return "无法创建输出文件，请确认目标目录可写，或该处没有同名的只读文件。";
        return null;
    }
}