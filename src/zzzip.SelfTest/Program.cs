using System.Text;
using zzzip.Core;
using zzzip.SelfTest;
using zzzip.ShellReg;

// zzzip 自动测试入口。
// 用法： zzzip.SelfTest.exe [--exe=<zzzip.exe 路径>] [--work=<临时工作目录>] [--keep]
// 退出码 = 失败用例数（0 表示全部通过）。
// 说明：本自测不依赖任何测试框架，可被 CI 或本地脚本直接判定。

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("zzzip 自动测试");
Console.WriteLine("=" + new string('=', 57));

string? exePath = null;
string? workDir = null;
var keep = false;
foreach (var a in args)
{
    if (a.StartsWith("--exe=", StringComparison.OrdinalIgnoreCase)) exePath = a[6..];
    else if (a.StartsWith("--work=", StringComparison.OrdinalIgnoreCase)) workDir = a[7..];
    else if (string.Equals(a, "--keep", StringComparison.OrdinalIgnoreCase)) keep = true;
    else if (a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(a)) exePath = a;
}

// 未显式指定时，按常见构建输出位置自动探测 zzzip.exe
exePath ??= AutoLocateApp();

workDir ??= Path.Combine(Path.GetTempPath(), "zzzip-selftest-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(workDir);

var harness = new TestHarness();
harness.Write($"工作目录：{workDir}");
harness.Write($"zzzip.exe：{exePath ?? "(未找到，端到端测试将跳过)"}");
ArchiveEngineRouter.EnsureConfigured();
harness.Write($"压缩引擎：{ArchiveEngineRouter.EngineSummary}");

// 这组测试会真实地反复装卸资源管理器右键菜单（【二】里直接写注册表，
// 【三】里还会调用 exe 的 --unregister）。先记下用户当前装没装、装的是哪个 exe，
// 整个测试跑完再原样恢复 —— 否则「跑一次自测」就会把用户已经装好的菜单弄没。
// 注意必须在最外层恢复：【三】的 --unregister 会在【二】之后再把菜单删一次。
var preInstalled = ContextMenuRegistrar.IsInstalled;
var preExe = preInstalled ? InstalledExePath() : null;
var preCascade = ReadCascadeOption();
harness.Write(preInstalled
    ? $"右键菜单现场：已安装（{preExe}），测试结束后将恢复"
    : "右键菜单现场：未安装，测试结束后保持未安装");

// 文件关联同样会被【三】的 --unregister 连带清掉：卸载的定义就是把所有默认值
// 指向 zzzip.assoc.* 的扩展名还原成原程序。所以也要记下现场 —— 而且要记下
// 用户当初用的那个 exe：自测的 exe 躺在临时目录里，跑完就被删了，
// 拿它去恢复等于给用户留一堆指向不存在文件的关联。
var preAssociated = AssociatedExtensions();
var preAssocExe = preAssociated.Count > 0 ? AssociationExePath(preAssociated[0]) : null;
harness.Write(preAssociated.Count > 0
    ? $"文件关联现场：{preAssociated.Count} 个扩展名（{string.Join(' ', preAssociated)}），测试结束后将恢复"
    : "文件关联现场：无，测试结束后保持无关联");
harness.Write(string.Empty);

try
{
    harness.Write("【一】压缩内核与格式注册表");
    CoreTests.Register(harness, workDir);

    harness.Write(string.Empty);
    harness.Write("【二】资源管理器右键菜单集成");
    ShellRegTests.Register(harness, workDir);

    harness.Write(string.Empty);
    harness.Write("【三】端到端命令行链路");
    E2ETests.Register(harness, workDir, exePath);
}
finally
{
    // 先恢复关联，再恢复菜单：Register 只重建菜单键，不会碰文件关联。
    if (preAssociated.Count > 0 && preAssocExe is not null)
    {
        try
        {
            ContextMenuRegistrar.Associate(preAssociated, preAssocExe);
            harness.Write($"已恢复测试前的文件关联（{preAssociated.Count} 个扩展名）。");
        }
        catch (Exception ex)
        {
            harness.Write($"!! 恢复文件关联失败：{ex.Message}");
        }
    }

    if (preInstalled && preExe is not null)
    {
        try
        {
            new ContextMenuRegistrar(preExe).Register(preCascade);
            harness.Write("已恢复测试前的右键菜单安装状态。");
        }
        catch (Exception ex)
        {
            harness.Write($"!! 恢复右键菜单失败：{ex.Message}");
        }
    }

    if (!keep)
    {
        try { Directory.Delete(workDir, recursive: true); } catch { }
    }
}

var logPath = Path.Combine(AppContext.BaseDirectory, "selftest-result.txt");
var failures = harness.Report(logPath);
harness.Write($"日志已保存：{logPath}");
return failures;

/// <summary>从已写入的 command 值里解析出当前注册用的 exe 路径（供测试后恢复现场）。</summary>
static string? InstalledExePath()
{
    try
    {
        using var k = Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(@"Software\Classes\*\shell\zzzip.add\command");
        return ParseExeFromCommand(k?.GetValue(null) as string);
    }
    catch { return null; }
}

/// <summary>命令行字符串 → exe 路径。形如 "C:\...\zzzip.exe" --verb=add "%1"。</summary>
static string? ParseExeFromCommand(string? s)
{
    s = s?.Trim();
    if (string.IsNullOrEmpty(s)) return null;

    if (s[0] == '"')
    {
        var end = s.IndexOf('"', 1);
        if (end > 1) return s[1..end];
    }
    var sp = s.IndexOf(' ');
    return sp > 0 ? s[..sp] : s;
}

/// <summary>列出当前所有默认值指向 zzzip 的扩展名（供测试后恢复现场）。</summary>
static List<string> AssociatedExtensions()
{
    var found = new List<string>();
    try
    {
        using var classes = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes");
        if (classes?.GetSubKeyNames() is not { } names) return found;

        foreach (var n in names)
        {
            if (!n.StartsWith('.') || n.StartsWith(".zzzip.", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var ext = n.TrimStart('.');
                if (ContextMenuRegistrar.IsAssociated(ext)) found.Add(ext);
            }
            catch { /* 单个扩展名读失败不影响其他 */ }
        }
    }
    catch { /* 读不到就按「没有关联」处理 */ }
    return found;
}

/// <summary>从 ProgID 的 open 命令里解析出关联时所用的 exe 路径。</summary>
static string? AssociationExePath(string ext)
{
    try
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            $@"Software\Classes\zzzip.assoc.{ext}\shell\open\command");
        return ParseExeFromCommand(k?.GetValue(null) as string);
    }
    catch { return null; }
}

/// <summary>读回菜单的层叠模式开关，恢复时保持用户原来的形态。</summary>
static bool ReadCascadeOption()
{
    try
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\zzzip");
        var v = k?.GetValue("CascadeMenu");
        return v is not null && v.ToString() == "1";
    }
    catch { return false; }
}

static string? AutoLocateApp()
{
    var baseDir = AppContext.BaseDirectory;
    var candidates = new[]
    {
        Path.Combine(baseDir, "zzzip.exe"),
        Path.Combine(baseDir, "..", "..", "..", "..", "zzzip.App", "bin", "Debug", "net10.0-windows", "zzzip.exe"),
        Path.Combine(baseDir, "..", "..", "..", "..", "zzzip.App", "bin", "Release", "net10.0-windows", "zzzip.exe"),
        Path.Combine(baseDir, "..", "..", "..", "..", "..", "artifacts", "zzzip.exe"),
    };
    return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
}