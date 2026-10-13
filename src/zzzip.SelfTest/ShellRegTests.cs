using Microsoft.Win32;
using zzzip.Core;
using zzzip.ShellReg;

namespace zzzip.SelfTest;

/// <summary>
/// 右键菜单与文件关联测试。会真实写入 HKCU 注册表，因此：
///  1. 测试前记录原始安装状态；
///  2. 测试后必须完整卸载并复核无残留；
///  3. 若原本已安装，则恢复原状。
///
/// 【教训】1.0 版的这组测试断言的是 <c>shellex\ContextMenuHandlers</c> —— 那是 COM 处理器的位置，
/// 我们没有 COM 组件，写进去完全不生效。测试与实现犯了同一个错，于是功能全坏而测试全绿。
/// 现在所有断言都改到资源管理器真正识别的位置上（<c>Software\Classes\&lt;类&gt;\shell\</c>）。
/// </summary>
public static class ShellRegTests
{
    private const string ClassesRoot = @"Software\Classes";
    private const string FileHost = @"Software\Classes\*\shell";
    private const string DirHost = @"Software\Classes\Directory\shell";
    private const string DriveHost = @"Software\Classes\Drive\shell";
    private const string OptKey = @"Software\zzzip";
    private const string MoreMenuKey = @"Software\Classes\zzzip.menu.more\shell";

    /// <summary>关联测试专用扩展名，避免污染用户真实的 .zip / .7z 关联。</summary>
    private const string TestExt = "zzziptest";

    /// <summary>HKCU 根下遗留键清理测试用的真实压缩扩展名（几乎不可能被别的软件占用）。</summary>
    private const string JunkExt = "udf";

    public static void Register(TestHarness t, string workDir)
    {
        var exe = Path.Combine(workDir, "zzzip-fake.exe");
        File.WriteAllText(exe, "stub");
        var registrar = new ContextMenuRegistrar(exe);

        t.Run("右键菜单：verb 写在资源管理器真正识别的 *\\shell / Directory\\shell / Drive\\shell", () =>
        {
            registrar.Register(cascade: false);
            try
            {
                foreach (var host in new[] { FileHost, DirHost, DriveHost })
                {
                    var key = $@"{host}\zzzip.add";
                    TestHarness.True(ValueOf(key) is { Length: > 0 }, $@"{host} 未写入「添加到压缩文件…」");
                    TestHarness.True(ValueOf($@"{key}\command") is { Length: > 0 }, $@"{host}\zzzip.add 缺少 command 子键");
                }

                TestHarness.True(ValueOf($@"{FileHost}\zzzip.quickzip") is { Length: > 0 }, "缺少「添加到 ZIP 压缩包」");
                TestHarness.True(ValueOf($@"{FileHost}\zzzip.more") is { Length: > 0 }, "缺少「其他压缩命令」级联入口");
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("右键菜单：命令行为 --verb=xxx \"%1\"，且带图标", () =>
        {
            registrar.Register(cascade: false);
            try
            {
                var cmd = ValueOf($@"{FileHost}\zzzip.add\command");
                TestHarness.True(cmd is not null && cmd.Contains("--verb=add"), "命令缺少 --verb=add：" + cmd);
                TestHarness.True(cmd is not null && cmd.Contains("\"%1\""), "命令缺少 %1 占位符：" + cmd);
                TestHarness.True(cmd is not null && cmd.Contains("zzzip-fake.exe"), "命令未指向本程序：" + cmd);

                var icon = ValueOf($@"{FileHost}\zzzip.add", "Icon");
                TestHarness.True(icon is not null && icon.Contains("zzzip-fake.exe"), "菜单项缺少 Icon：" + icon);
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("右键菜单：不再写 shellex\\ContextMenuHandlers（我们没有 COM 组件）", () =>
        {
            registrar.Register(cascade: true);
            try
            {
                var bogus = new List<string>();
                foreach (var host in new[] { "*", "Directory", "Drive" })
                    if (KeyExists($@"{ClassesRoot}\{host}\shellex\ContextMenuHandlers\zzzip")) bogus.Add(host);
                foreach (var ext in FormatRegistry.AllExtNames())
                    if (KeyExists($@"{ClassesRoot}\.{ext}\shellex\ContextMenuHandlers\zzzip")) bogus.Add(ext);

                TestHarness.True(bogus.Count == 0,
                    "又写入了无效的 COM 路径（会去加载不存在的处理器）：" + string.Join(", ", bogus));
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("右键菜单：全部 42+ 压缩扩展名均登记解压 verb", () =>
        {
            registrar.Register(cascade: false);
            try
            {
                var missing = new List<string>();
                foreach (var ext in FormatRegistry.AllExtNames())
                    if (ValueOf($@"{ClassesRoot}\.{ext}\shell\zzzip.own") is not { Length: > 0 }) missing.Add(ext);

                TestHarness.True(missing.Count == 0, "以下扩展名缺少解压菜单：" + string.Join(", ", missing));

                var extCount = FormatRegistry.AllExtNames().Length;
                TestHarness.True(extCount >= 42, $"应登记不少于 42 个扩展名，实际 {extCount}");
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("右键菜单：「其他压缩命令」用 SubCommands + ExtendedSubCommandsKey 实现级联", () =>
        {
            registrar.Register(cascade: false);
            try
            {
                // 单写一个 SubMenu 值（1.0 版的写法）是无效的，必须成对出现这两个值
                TestHarness.True(ValueOf($@"{FileHost}\zzzip.more", "MUIVerb") is { Length: > 0 },
                    "级联入口缺少 MUIVerb，菜单会显示成空文字");
                TestHarness.True(ValueOf($@"{FileHost}\zzzip.more", "SubCommands") is not null,
                    "缺少 SubCommands 值，级联不会展开");
                TestHarness.Equal("zzzip.menu.more", ValueOf($@"{FileHost}\zzzip.more", "ExtendedSubCommandsKey"),
                    "ExtendedSubCommandsKey 应指向承载键");

                foreach (var item in ContextMenuRegistrar.MoreFileMenuItems())
                {
                    TestHarness.True(ValueOf($@"{MoreMenuKey}\{item.KeyName}") is { Length: > 0 },
                        $"级联子项 {item.Label} 未写入");
                    TestHarness.True(ValueOf($@"{MoreMenuKey}\{item.KeyName}\command") is { Length: > 0 },
                        $"级联子项 {item.Label} 缺少 command");
                }
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("右键菜单：菜单文案与 360 对齐", () =>
        {
            var files = ContextMenuRegistrar.FileMenuItems(cascade: false).Select(f => f.Label).ToList();
            TestHarness.True(files.Any(l => l.Contains("添加到压缩文件")), "缺少「添加到压缩文件…」");
            TestHarness.True(files.Any(l => l.Contains("添加到 ZIP 压缩包")), "缺少「添加到 ZIP 压缩包」");
            TestHarness.True(files.Any(l => l.Contains("其他压缩命令")), "缺少「其他压缩命令」级联");

            var more = ContextMenuRegistrar.MoreFileMenuItems().Select(f => f.Label).ToList();
            TestHarness.True(more.Any(l => l.Contains("高压缩比")), "缺少「添加到高压缩比」");
            TestHarness.True(more.Any(l => l.Contains("单独的压缩包")), "缺少「压缩每个文件到单独的压缩包中」");
            TestHarness.True(more.Any(l => l.Contains("并邮件")), "缺少「添加到 ZIP 压缩包并邮件」");

            var archives = ContextMenuRegistrar.ArchiveMenuItems(cascade: false);
            var aLabels = archives.Select(a => a.Label).ToList();
            TestHarness.True(aLabels.Any(l => l.Contains("同名文件夹")), "缺少「解压到同名文件夹」");
            TestHarness.True(aLabels.Any(l => l.Contains("当前文件夹")), "缺少「解压到当前文件夹」");
            TestHarness.True(aLabels.Any(l => l.Contains("指定文件夹")), "缺少「解压到指定文件夹…」");

            TestHarness.Equal(ContextMenuRegistrar.Verbs.ExtractToOwn, archives[0].Verb,
                "压缩包菜单第一项应为「解压到同名文件夹」");
        });

        t.Run("右键菜单：层叠模式折叠为单项并撤下多余 verb", () =>
        {
            TestHarness.Equal(1, ContextMenuRegistrar.FileMenuItems(cascade: true).Count, "层叠下文件菜单应只剩 1 项");
            TestHarness.Equal(1, ContextMenuRegistrar.ArchiveMenuItems(cascade: true).Count, "层叠下压缩包菜单应只剩 1 项");

            registrar.Register(cascade: true);
            try
            {
                TestHarness.True(ValueOf($@"{FileHost}\zzzip.add") is { Length: > 0 }, "层叠下主入口应存在");
                TestHarness.True(ValueOf($@"{FileHost}\zzzip.quickzip") is null, "层叠下不应保留「添加到 ZIP 压缩包」");
                TestHarness.True(ValueOf($@"{ClassesRoot}\.zip\shell\zzzip.here") is null, "层叠下不应保留「解压到当前文件夹」");
            }
            finally { ContextMenuRegistrar.Unregister(); }
        });

        t.Run("右键菜单：卸载后无任何残留（含 1.0 版遗留的错误位置）", () =>
        {
            registrar.Register(cascade: false);
            TestHarness.True(ContextMenuRegistrar.IsInstalled, "注册后应处于已安装状态");

            ContextMenuRegistrar.Unregister();

            TestHarness.False(ContextMenuRegistrar.IsInstalled, "卸载后不应仍为已安装状态");
            TestHarness.True(KeyExists(OptKey) is false, "选项键未清理");
            TestHarness.True(ValueOf($@"{FileHost}\zzzip.add") is null, "文件菜单键未清理");
            TestHarness.True(KeyExists($@"{ClassesRoot}\zzzip.menu.more") is false, "级联承载键未清理");

            var leftovers = new List<string>();
            foreach (var host in new[] { "*", "Directory", "Drive" })
                foreach (var n in SubKeysOf($@"{ClassesRoot}\{host}\shell"))
                    if (n.StartsWith("zzzip.", StringComparison.OrdinalIgnoreCase)) leftovers.Add($"{host}:{n}");

            foreach (var ext in FormatRegistry.AllExtNames())
            {
                foreach (var n in SubKeysOf($@"{ClassesRoot}\.{ext}\shell"))
                    if (n.StartsWith("zzzip.", StringComparison.OrdinalIgnoreCase)) leftovers.Add($"shell:{ext}:{n}");
                if (KeyExists($@"{ClassesRoot}\zzzip.assoc.{ext}")) leftovers.Add("assoc:" + ext);
            }

            TestHarness.True(leftovers.Count == 0, "存在残留项：" + string.Join(", ", leftovers));
        });

        t.Run("右键菜单：卸载能清掉 1.0 版写错位置的键", () =>
        {
            // 复刻 1.0 版的两处错误写法
            using (Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shellex\ContextMenuHandlers\zzzip")) { }
            using (Registry.CurrentUser.CreateSubKey(@"Software\Classes\.zip\shellex\ContextMenuHandlers\zzzip")) { }
            using (var k = Registry.CurrentUser.CreateSubKey(@".zzzip.deadbeef\shell\add")) k.SetValue(null, "旧版误建");
            using (var k = Registry.CurrentUser.CreateSubKey($@".{JunkExt}")) { }

            ContextMenuRegistrar.Unregister();

            TestHarness.True(KeyExists(@"Software\Classes\*\shellex\ContextMenuHandlers\zzzip") is false,
                "旧版 shellex 残留未清除");
            TestHarness.True(KeyExists(@"Software\Classes\.zip\shellex\ContextMenuHandlers\zzzip") is false,
                "旧版扩展名 shellex 残留未清除");
            TestHarness.True(KeyExists(@".zzzip.deadbeef") is false, "HKCU 根下的 .zzzip.* 垃圾键未清除");
            TestHarness.True(KeyExists($@".{JunkExt}") is false, "HKCU 根下的 .ext 垃圾键未清除");
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

        RegisterAssociationTests(t);
    }

    /// <summary>
    /// 文件关联测试。用专用扩展名 <c>.zzziptest</c>，绝不动用户真实的 .zip 关联。
    /// 1.0 版的关联写到了 HKCU\.zip（漏了 Software\Classes 前缀），等于什么都没做。
    /// </summary>
    private static void RegisterAssociationTests(TestHarness t)
    {
        var exe = Path.Combine(Path.GetTempPath(), "zzzip-assoc-fake.exe");

        t.Run("文件关联：写在 Software\\Classes\\.ext 下并指向自定义 ProgID", () =>
        {
            try
            {
                TestHarness.False(ContextMenuRegistrar.IsAssociated(TestExt), "前置条件：测试扩展名不应已关联");

                ContextMenuRegistrar.Associate(["." + TestExt], exe);

                TestHarness.True(ContextMenuRegistrar.IsAssociated(TestExt), "关联后 IsAssociated 应为真");
                TestHarness.Equal("zzzip.assoc." + TestExt,
                    ValueOf($@"{ClassesRoot}\.{TestExt}"), ".ext 默认值应指向我们的 ProgID");

                // 绝不能写到 HKCU 根下（1.0 版的 bug）
                TestHarness.True(KeyExists($".{TestExt}") is false,
                    "关联键写到了 HKCU 根下，缺少 Software\\Classes 前缀");

                // ProgID 必须完整，否则「打开方式」里没有图标、双击也起不来
                TestHarness.True(ValueOf($@"{ClassesRoot}\zzzip.assoc.{TestExt}") is { Length: > 0 },
                    "ProgID 缺少显示名");
                TestHarness.True(ValueOf($@"{ClassesRoot}\zzzip.assoc.{TestExt}\DefaultIcon") is { Length: > 0 },
                    "ProgID 缺少 DefaultIcon");
                var openCmd = ValueOf($@"{ClassesRoot}\zzzip.assoc.{TestExt}\shell\open\command");
                TestHarness.True(openCmd is not null && openCmd.Contains("\"%1\""), "ProgID 缺少 open 命令：" + openCmd);
            }
            finally { ContextMenuRegistrar.Dissociate(["." + TestExt]); }
        });

        t.Run("文件关联：解除后默认值清空且 ProgID 被删除", () =>
        {
            ContextMenuRegistrar.Associate(["." + TestExt], exe);
            ContextMenuRegistrar.Dissociate(["." + TestExt]);

            TestHarness.False(ContextMenuRegistrar.IsAssociated(TestExt), "解除后不应仍指向 zzzip");
            var after = ValueOf($@"{ClassesRoot}\.{TestExt}");
            TestHarness.True(after is null || !after.StartsWith("zzzip.assoc.", StringComparison.OrdinalIgnoreCase),
                "解除后默认值未清空：" + after);
            TestHarness.True(KeyExists($@"{ClassesRoot}\zzzip.assoc.{TestExt}") is false, "ProgID 未删除");
        });

        t.Run("文件关联：取消关联会还原用户原有的默认程序", () =>
        {
            // 模拟「用户原本把该扩展名关联到别的压缩软件」
            const string original = "ZZZipTestOther.zzziptest";
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{TestExt}"))
                    k!.SetValue(null, original);

                ContextMenuRegistrar.Associate(["." + TestExt], exe);
                TestHarness.Equal("zzzip.assoc." + TestExt, ValueOf($@"{ClassesRoot}\.{TestExt}"),
                    "关联后默认值应指向 zzzip");

                ContextMenuRegistrar.Dissociate(["." + TestExt]);
                TestHarness.Equal(original, ValueOf($@"{ClassesRoot}\.{TestExt}"),
                    "取消关联后必须还原成用户原来的默认程序，而不是把默认值一删了事");
            }
            finally
            {
                TryDeleteKey($@"{ClassesRoot}\.{TestExt}");
            }
        });

        t.Run("文件关联：反复勾选再取消，仍还原成最初的原默认程序", () =>
        {
            const string original = "AnotherApp.zzziptest";
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{TestExt}"))
                    k!.SetValue(null, original);

                // 勾选两次：第二次若把备份覆盖成自己的 ProgID，取消关联后会指向不存在的程序
                ContextMenuRegistrar.Associate(["." + TestExt], exe);
                ContextMenuRegistrar.Associate(["." + TestExt], exe);
                ContextMenuRegistrar.Dissociate(["." + TestExt]);

                TestHarness.Equal(original, ValueOf($@"{ClassesRoot}\.{TestExt}"),
                    "重复关联后取消，应还原成最初的原默认程序（备份不可被自我覆盖）");
            }
            finally
            {
                TryDeleteKey($@"{ClassesRoot}\.{TestExt}");
            }
        });

        t.Run("文件关联：卸载程序会重置仍指向 zzzip 的扩展名（不留下坏关联）", () =>
        {
            ContextMenuRegistrar.Associate(["." + TestExt], exe);
            TestHarness.True(ContextMenuRegistrar.IsAssociated(TestExt), "前置条件：应先完成关联");

            // 关联状态下执行卸载（选项对话框里「取消勾选集成」的场景）
            ContextMenuRegistrar.Unregister();

            TestHarness.False(ContextMenuRegistrar.IsAssociated(TestExt), "卸载后不应仍指向已删除的 ProgID");
            TestHarness.True(KeyExists($@"{ClassesRoot}\zzzip.assoc.{TestExt}") is false, "卸载后 ProgID 未清理");
        });
    }

    // ---------------------------------------------------------------- 注册表读取辅助

    private static string? ValueOf(string path, string? name = null)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path);
            return k?.GetValue(name) as string;
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

    /// <summary>删除测试期间临时造出来的键，避免污染用户注册表。</summary>
    private static void TryDeleteKey(string path)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch { /* 删不掉也不该让测试失败 */ }
    }

    private static string[] SubKeysOf(string path)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path);
            return k?.GetSubKeyNames() ?? [];
        }
        catch { return []; }
    }
}
