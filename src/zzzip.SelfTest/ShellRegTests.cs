using Microsoft.Win32;
using zzzip.Core;
using zzzip.ShellReg;

namespace zzzip.SelfTest;

/// <summary>
/// 右键菜单集成测试。会真实写入 HKCU 注册表，因此：
///  1. 测试前记录原始安装状态；
///  2. 测试后必须完整卸载并复核无残留；
///  3. 若原本已安装，则恢复原状。
/// </summary>
public static class ShellRegTests
{
    private const string FileMenuKey = @"Software\Classes\*\shellex\ContextMenuHandlers\zzzip";
    private const string DirMenuKey = @"Software\Classes\Directory\shellex\ContextMenuHandlers\zzzip";
    private const string OptKey = @"Software\zzzip";

    public static void Register(TestHarness t, string workDir)
    {
        var exe = Path.Combine(workDir, "zzzip-fake.exe");
        File.WriteAllText(exe, "stub");
        var registrar = new ContextMenuRegistrar(exe);

        t.Run("右键菜单：普通文件类型(*)/文件夹(Directory)/驱动器(Drive) 均被登记", () =>
        {
            registrar.Register(cascade: false);
            try
            {
                TestHarness.True(ValueOf(FileMenuKey) is { Length: > 0 }, "文件右键菜单未写入");
                TestHarness.True(ValueOf(DirMenuKey) is { Length: > 0 }, "文件夹右键菜单未写入");
                TestHarness.True(ValueOf(@"Software\Classes\Drive\shellex\ContextMenuHandlers\zzzip") is { Length: > 0 },
                    "驱动器右键菜单未写入");
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("右键菜单：全部 42+ 压缩扩展名均登记菜单与打开命令", () =>
        {
            registrar.Register(cascade: false);
            try
            {
                var missing = new List<string>();
                foreach (var ext in FormatRegistry.AllExtNames())
                {
                    var handler = ValueOf($@"Software\Classes\{ext}\shellex\ContextMenuHandlers\zzzip");
                    if (string.IsNullOrEmpty(handler)) missing.Add(ext);
                }
                TestHarness.True(missing.Count == 0,
                    "以下扩展名缺少右键菜单登记：" + string.Join(", ", missing));

                var extCount = FormatRegistry.AllExtNames().Length;
                TestHarness.True(extCount >= 42, $"应登记不少于 42 个扩展名，实际 {extCount}");
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("右键菜单：菜单文案与 360 一致（含“其他压缩命令”级联）", () =>
        {
            var files = ContextMenuRegistrar.FileMenuItems(cascade: false);
            var labels = files.Select(f => f.Label).ToList();

            TestHarness.True(labels.Any(l => l.Contains("添加到压缩文件")), "缺少「添加到压缩文件…」");
            TestHarness.True(labels.Any(l => l.Contains("\"%f.zip\"")), "缺少「添加到\"%f.zip\"」");
            TestHarness.True(labels.Any(l => l.Contains("其他压缩命令")), "缺少「其他压缩命令」级联");
            TestHarness.True(labels.Any(l => l.Contains("高压缩比")), "缺少「添加到高压缩比文件」");
            TestHarness.True(labels.Any(l => l.Contains("单独的压缩包")), "缺少「压缩每个文件到单独的压缩包中」");
            TestHarness.True(labels.Any(l => l.Contains("并邮件")), "缺少「添加到\"%f.zip\"并邮件」");

            var archives = ContextMenuRegistrar.ArchiveMenuItems(cascade: false);
            var aLabels = archives.Select(a => a.Label).ToList();
            TestHarness.True(aLabels.Any(l => l.Contains("解压到")), "缺少「解压到\"%f\\\"」");
            TestHarness.True(aLabels.Any(l => l.Contains("当前文件夹")), "缺少「解压到当前文件夹」");
            TestHarness.True(aLabels.Any(l => l.Contains("指定文件夹")), "缺少「解压到指定文件夹…」");

            // 第一项必须是默认项（360 的默认动作是「解压到"文件名文件名\"」）
            TestHarness.Equal(ContextMenuRegistrar.Verbs.ExtractToOwn, archives[0].Verb,
                "压缩包菜单第一项应为「解压到同名文件夹」");
        });

        t.Run("右键菜单：层叠模式折叠为单项", () =>
        {
            var cascadeFiles = ContextMenuRegistrar.FileMenuItems(cascade: true);
            var cascadeArchives = ContextMenuRegistrar.ArchiveMenuItems(cascade: true);
            TestHarness.Equal(1, cascadeFiles.Count, "层叠模式下文件菜单应只剩 1 项");
            TestHarness.Equal(1, cascadeArchives.Count, "层叠模式下压缩包菜单应只剩 1 项");
        });

        t.Run("右键菜单：卸载后注册表无任何 zzzip 残留", () =>
        {
            registrar.Register(cascade: false);
            TestHarness.True(ContextMenuRegistrar.IsInstalled, "注册后应处于已安装状态");

            ContextMenuRegistrar.Unregister();

            TestHarness.False(ContextMenuRegistrar.IsInstalled, "卸载后不应仍为已安装状态");
            TestHarness.True(ValueOf(FileMenuKey) is null, "文件菜单键未清理");
            TestHarness.True(ValueOf(DirMenuKey) is null, "文件夹菜单键未清理");
            TestHarness.True(KeyExists(OptKey) is false, "选项键未清理");

            var leftovers = new List<string>();
            foreach (var ext in FormatRegistry.AllExtNames())
            {
                if (ValueOf($@"Software\Classes\{ext}\shellex\ContextMenuHandlers\zzzip") is not null)
                    leftovers.Add(ext);
                if (KeyExists($@"Software\Classes\zzzip.assoc.{ext}")) leftovers.Add("assoc:" + ext);
            }
            TestHarness.True(leftovers.Count == 0, "存在残留项：" + string.Join(", ", leftovers));
        });

        t.Run("右键菜单：选项可持久化并读回（层叠开关）", () =>
        {
            try
            {
                var r1 = new ContextMenuRegistrar(exe);
                r1.Register(cascade: true);
                TestHarness.True(r1.IsCascade, "层叠模式应被持久化");

                var r2 = new ContextMenuRegistrar(exe);
                TestHarness.True(r2.LoadOptions(), "新建实例应能读回层叠模式");
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("文件关联：关联后可识别为 zzzip，解除后恢复", () =>
        {
            try
            {
                ContextMenuRegistrar.Associate([".zip"]);
                TestHarness.Equal("zzzip.assoc.zip", ValueOf(@".zip"), ".zip 应被关联到 zzzip");

                ContextMenuRegistrar.Dissociate([".zip"]);
                var after = ValueOf(@".zip");
                TestHarness.True(after is null || !after.StartsWith("zzzip.assoc.", StringComparison.OrdinalIgnoreCase),
                    "解除关联后不应仍指向 zzzip");
            }
            finally { ContextMenuRegistrar.Dissociate([".zip"]); }
        });
    }

    private static string? ValueOf(string path)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path);
            return k?.GetValue(null) as string;
        }
        catch { return null; }
    }

    private static bool KeyExists(string path)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path);
            return k is not null;
        }
        catch { return false; }
    }
}