using System.Text;
using zzzip.Core;
using zzzip.SelfTest;

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
    if (!keep)
    {
        try { Directory.Delete(workDir, recursive: true); } catch { }
    }
}

var logPath = Path.Combine(AppContext.BaseDirectory, "selftest-result.txt");
var failures = harness.Report(logPath);
harness.Write($"日志已保存：{logPath}");
return failures;

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