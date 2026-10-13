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

    /// <summary>按优先级查找 7z.exe：环境变量 → exe 同级 → PATH → 常见安装目录。</summary>
    public static string? LocateExecutable()
    {
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
                        IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        if (!File.Exists(archivePath)) throw new FileNotFoundException("压缩包不存在", archivePath);
        Directory.CreateDirectory(destinationDir);

        var all = ListInternal(archivePath, options.Password);
        var wanted = entryPredicate is null ? all : all.Where(entryPredicate).ToList();
        long total = wanted.Where(e => !e.IsDirectory).Sum(e => e.Size);

        var args = new List<string> { "x", "-y", "-sccUTF-8", "-bsp1", "-o" + destinationDir };
        if (!string.IsNullOrEmpty(options.Password)) args.Add("-p" + options.Password);
        args.Add(options.OverwriteExisting ? "-aoa" : "-aos");

        // 只解压被选中的条目：目录整体追加；文件仅在“部分选择”时按包内路径逐个追加
        foreach (var e in wanted)
        {
            if (e.IsDirectory) args.Add(e.RawName);
            else if (wanted.Count < all.Count) args.Add(e.RawName);
        }
        args.Add(archivePath);

        var code = Run(args, MakeProgressLineHandler(progress, total), out var err, ct);
        if (code != 0) throw new InvalidDataException(ErrorMessage(err, archivePath));

        if (!options.KeepArchiveAfterExtract)
        {
            try { File.Delete(archivePath); } catch { /* 删除失败不阻断 */ }
        }
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

    private static string ErrorMessage(string stderr, string archivePath)
    {
        var detail = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .FirstOrDefault(s => !s.StartsWith("7-Zip", StringComparison.OrdinalIgnoreCase));
        var reason = detail ?? "未知错误";
        return $"{Path.GetFileName(archivePath)} 处理失败：{reason}";
    }
}