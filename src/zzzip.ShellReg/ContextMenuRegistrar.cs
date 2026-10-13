using Microsoft.Win32;
using zzzip.Core;

namespace zzzip.ShellReg;

/// <summary>
/// 右键菜单注册器 —— 复刻 360zip 的资源管理器集成体验。
///
/// 关键设计：
///  1. 只用注册表 verb，不做 COM / IShellExtInit。本机 reg.exe 被安全中心「程序黑名单」拦截，
///     COM 方案还依赖 regsvr32 注册 DLL，风险高、也谈不上绿色；verb 方式纯 .NET API 完成，
///     一个 exe 即可卸载干净，永不出现「菜单丢失」。
///  2. 菜单文字与顺序 1:1 对齐 360，第一项即默认动作。
///  3. 支持层叠模式：勾选后普通文件与压缩包各只保留 1 个入口项，对应 360 的「层叠右键关联菜单」。
///  4. 便携模式：什么都不写注册表，卸载 = 删文件夹。
///
/// 【路径铁律】所有键都相对 HKCU 且必须带上 <c>Software\Classes\</c> 前缀。
///   · 只被资源管理器识别的 verb 位置是 <c>Software\Classes\&lt;类&gt;\shell\&lt;动词名&gt;\command</c>，
///     其中 &lt;类&gt; 取 <c>*</c>（所有文件）/ <c>Directory</c>（文件夹）/ <c>Drive</c>（驱动器）/
///     <c>.ext</c>（具体扩展名）。
///   · <c>shellex\ContextMenuHandlers</c> 是 COM 处理器专用位置，只有默认值填 CLSID 且该 CLSID
///     已注册 InprocServer32 才会生效。我们不是 COM 组件，写它等于无效，反而让资源管理器去
///     加载一个不存在的处理器 —— 这正是 1.0 版右键菜单完全不出现的根因。
/// </summary>
public sealed class ContextMenuRegistrar
{
    /// <summary>命令行动作标识，与 Program.cs 的参数解析一一对应。</summary>
    public static class Verbs
    {
        public const string AddToArchive = "add";       // 添加到压缩文件…
        public const string QuickZip = "quickzip";      // 添加到 ZIP 压缩包
        public const string HighRatio7z = "7z";         // 添加到高压缩比 7Z 压缩包
        public const string EachSeparately = "each";    // 压缩每个文件到单独的压缩包中
        public const string ZipAndMail = "mail";        // 添加到 ZIP 压缩包并邮件
        public const string ExtractToOwn = "own";       // 解压到同名文件夹
        public const string ExtractHere = "here";       // 解压到当前文件夹
        public const string ExtractTo = "to";           // 解压到指定文件夹…
    }

    /// <summary>一个右键菜单项：Verb 是命令行动作，KeyName 是注册表子键名，Label 是菜单文字。</summary>
    public sealed record MenuItem(string Verb, string KeyName, string Label);

    private const string ClassesRoot = @"Software\Classes";
    private const string OptionsKey = @"Software\zzzip";
    private const string FileExtsRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

    /// <summary>级联子菜单的承载键（相对 HKCR，即 HKCU\Software\Classes 下）。</summary>
    private const string MoreMenuClass = "zzzip.menu.more";

    /// <summary>verb 子键统一前缀，便于整体清理且不与别的软件撞名。</summary>
    private const string KeyPrefix = "zzzip.";

    /// <summary>所有 verb 挂载点：所有文件、文件夹、驱动器。</summary>
    private static readonly string[] HostClasses = ["*", "Directory", "Drive"];

    private readonly string _exePath;

    public ContextMenuRegistrar(string exePath)
    {
        _exePath = ResolveExe(path: exePath);
    }

    /// <summary>把 exe 路径规范成绝对路径；传入相对路径时按当前工作目录解析。</summary>
    private static string ResolveExe(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    // ---------------------------------------------------------------- 菜单文案（与 360 一致）

    /// <summary>
    /// 普通文件/文件夹的菜单项（文字与顺序对齐 360 的「其他压缩命令」结构）。
    /// 注：静态 verb 的菜单文字无法插入实际文件名（那需要 COM IContextMenu 动态生成），
    /// 因此这里用等义的固定文案，核心动作与 360 一一对应。
    /// </summary>
    public static IReadOnlyList<MenuItem> FileMenuItems(bool cascade) => cascade
        ? [new(Verbs.AddToArchive, KeyPrefix + Verbs.AddToArchive, "添加到压缩文件…")]
        :
        [
            new(Verbs.AddToArchive, KeyPrefix + Verbs.AddToArchive, "添加到压缩文件…"),
            new(Verbs.QuickZip,     KeyPrefix + Verbs.QuickZip,     "添加到 ZIP 压缩包"),
            new("",                 KeyPrefix + "more",             "其他压缩命令"),
        ];

    /// <summary>「其他压缩命令」级联里的子项（对应 360 的 其他压缩命令 ▸）。</summary>
    public static IReadOnlyList<MenuItem> MoreFileMenuItems() =>
    [
        new(Verbs.HighRatio7z,    KeyPrefix + Verbs.HighRatio7z,    "添加到高压缩比 7Z 压缩包"),
        new(Verbs.EachSeparately, KeyPrefix + Verbs.EachSeparately, "压缩每个文件到单独的压缩包中"),
        new(Verbs.ZipAndMail,     KeyPrefix + Verbs.ZipAndMail,     "添加到 ZIP 压缩包并邮件"),
    ];

    /// <summary>压缩包右键菜单项（顺序与文字对齐 360，第一项为默认项）。</summary>
    public static IReadOnlyList<MenuItem> ArchiveMenuItems(bool cascade) => cascade
        ? [new(Verbs.ExtractToOwn, KeyPrefix + Verbs.ExtractToOwn, "解压到同名文件夹")]
        :
        [
            new(Verbs.ExtractToOwn, KeyPrefix + Verbs.ExtractToOwn, "解压到同名文件夹"),
            new(Verbs.ExtractHere,  KeyPrefix + Verbs.ExtractHere,  "解压到当前文件夹"),
            new(Verbs.ExtractTo,    KeyPrefix + Verbs.ExtractTo,    "解压到指定文件夹…"),
        ];

    // ---------------------------------------------------------------- 注册

    /// <summary>写入右键菜单并刷新资源管理器缓存。返回已注册的处理器名。</summary>
    public IReadOnlyList<string> Register(bool cascade = false)
    {
        // 只清菜单键：注册必须是幂等的，但绝不能顺手把用户已经建立的
        // 文件关联（.ext 默认值 + ProgID）一起重置掉。
        CleanupMenuKeys();
        RegisterFileVerbs(cascade);
        RegisterArchiveVerbs(cascade);
        WriteProgIds();            // 供「打开方式」与关联使用
        SaveOptions(cascade);
        NotifyShell();

        return cascade
            ? ["zzzip 文件菜单（层叠）", "zzzip 压缩包菜单（层叠）"]
            : ["zzzip 文件菜单", "zzzip 压缩包菜单"];
    }

    public void RegisterFileVerbs(bool cascade)
    {
        foreach (var host in HostClasses)
        {
            foreach (var item in FileMenuItems(cascade))
            {
                // Verb 为空的条目即「其他压缩命令」级联入口
                if (string.IsNullOrEmpty(item.Verb))
                    WriteCascadeEntry(host, item);
                else
                    WriteVerb(host, item.KeyName, item.Label, item.Verb);
            }
        }

        if (!cascade)
            WriteCascadeChildren();
    }

    public void RegisterArchiveVerbs(bool cascade)
    {
        foreach (var ext in FormatRegistry.AllExtNames())
        {
            var host = "." + ext;
            foreach (var item in ArchiveMenuItems(cascade))
                WriteVerb(host, item.KeyName, item.Label, item.Verb);
        }
    }

    /// <summary>写一个 verb：<c>Software\Classes\&lt;host&gt;\shell\&lt;key&gt;</c> + 其下 command。</summary>
    private void WriteVerb(string host, string keyName, string label, string verb)
    {
        var basePath = $@"{ClassesRoot}\{host}\shell\{keyName}";
        TryWrite(() =>
        {
            using var k = Registry.CurrentUser.CreateSubKey(basePath);
            if (k is null) return;
            k.SetValue(null, label);
            k.SetValue("Icon", $"\"{_exePath}\",0");
            using var cmd = k.CreateSubKey("command");
            // %1 由资源管理器替换为选中项；配合 MultiSelectModel=Player，
            // 多选时资源管理器只调用一次并把所有选中项都传进来。
            cmd?.SetValue(null, $"\"{_exePath}\" --verb={verb} \"%1\"");
        });
    }

    /// <summary>
    /// 层叠入口（对应 360 的「其他压缩命令 ▸」）。
    /// 需要 <c>SubCommands=""</c> 与 <c>ExtendedSubCommandsKey</c> 成对出现，
    /// 后者指向一个「带 shell 子键」的 HKCR 相对路径。
    /// 单写一个 SubMenu 值是无效的（1.0 版的写法）。
    /// </summary>
    private void WriteCascadeEntry(string host, MenuItem item)
    {
        var basePath = $@"{ClassesRoot}\{host}\shell\{item.KeyName}";
        TryWrite(() =>
        {
            using var k = Registry.CurrentUser.CreateSubKey(basePath);
            if (k is null) return;
            // 默认值与 MUIVerb 都写上：Windows 优先取 MUIVerb，
            // 但部分旧版本在 SubCommands 场景下会回退读默认值，两个都设最稳。
            k.SetValue(null, item.Label);
            k.SetValue("MUIVerb", item.Label);
            k.SetValue("SubCommands", string.Empty);
            k.SetValue("ExtendedSubCommandsKey", MoreMenuClass);
            k.SetValue("Icon", $"\"{_exePath}\",0");
        });
    }

    /// <summary>级联子菜单的条目，集中放在 <c>Software\Classes\zzzip.menu.more\shell</c> 下。</summary>
    private void WriteCascadeChildren()
    {
        foreach (var item in MoreFileMenuItems())
        {
            var basePath = $@"{ClassesRoot}\{MoreMenuClass}\shell\{item.KeyName}";
            TryWrite(() =>
            {
                using var k = Registry.CurrentUser.CreateSubKey(basePath);
                if (k is null) return;
                k.SetValue("MUIVerb", item.Label);
                k.SetValue(null, item.Label);
                k.SetValue("Icon", $"\"{_exePath}\",0");
                using var cmd = k.CreateSubKey("command");
                cmd?.SetValue(null, $"\"{_exePath}\" --verb={item.Verb} \"%1\"");
            });
        }
    }

    /// <summary>为每个压缩扩展名写入自定义 ProgID（含图标与 open 命令）。</summary>
    private void WriteProgIds()
    {
        foreach (var ext in FormatRegistry.AllExtNames())
            WriteProgId(ext, _exePath);
    }

    /// <summary>写一个自定义 ProgID：默认值（显示名）+ DefaultIcon + shell\open\command。</summary>
    private static void WriteProgId(string ext, string exePath)
    {
        var progId = ProgIdFor(ext);
        var display = FormatRegistry.Resolve("." + ext)?.DisplayName is { Length: > 0 } d
            ? d + " 压缩包"
            : "zzzip 压缩包";

        TryWrite(() =>
        {
            using var pid = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{progId}");
            if (pid is null) return;
            pid.SetValue(null, display);
            using var icon = pid.CreateSubKey("DefaultIcon");
            icon?.SetValue(null, $"\"{exePath}\",0");

            // 显式声明 ProgID 的默认动词是「打开」。不写这一行时，一旦该 ProgID 被系统采用，
            // 默认动作会退化成「动词列表的第一个」——也就是某个解压项。
            using var shell = pid.CreateSubKey("shell");
            shell?.SetValue(null, "open");

            using var cmd = pid.CreateSubKey(@"shell\open\command");
            cmd?.SetValue(null, $"\"{exePath}\" \"%1\"");
        });
    }

    // ---------------------------------------------------------------- 卸载（无残留）

    /// <summary>
    /// 卸载：移除右键菜单 + 撤销本程序建立的全部文件关联 + 删除选项。
    /// 对应命令行 <c>--unregister</c> 与「卸载 = 删键」的绿色承诺。
    /// </summary>
    public static void Unregister()
    {
        CleanupMenuKeys();
        ResetOwnedAssociations();
        TryDelete(OptionsKey);
        NotifyShell();
    }

    /// <summary>
    /// 只移除右键菜单，保留文件关联。
    /// 【选项】对话框里取消勾选「集成到资源管理器右键菜单」时用这个 ——
    /// 关联是由下面的勾选框单独控制的，不该被菜单开关连带清掉。
    /// </summary>
    public static void UnregisterMenu()
    {
        CleanupMenuKeys();
        NotifyShell();
    }

    /// <summary>只清理菜单相关的键（含 1.0 版写到错误位置的遗留），不碰文件关联。</summary>
    private static void CleanupMenuKeys()
    {
        // 1) 当前版本的 verb
        foreach (var host in HostClasses)
            DeleteVerbSubKeys($@"{ClassesRoot}\{host}\shell");

        foreach (var ext in FormatRegistry.AllExtNames())
            DeleteVerbSubKeys($@"{ClassesRoot}\.{ext}\shell");

        TryDelete($@"{ClassesRoot}\{MoreMenuClass}");

        // 2) 1.0 版错误的 COM 式路径（我们没有 COM 组件，写它只会让资源管理器
        //    去加载一个不存在的处理器，右键菜单因此完全不出现）
        foreach (var host in HostClasses)
            TryDelete($@"{ClassesRoot}\{host}\shellex\ContextMenuHandlers\zzzip");

        foreach (var ext in FormatRegistry.AllExtNames())
            TryDelete($@"{ClassesRoot}\.{ext}\shellex\ContextMenuHandlers\zzzip");

        // 3) 1.0 版把 verb 根写成了 HKCU\.zzzip.<guid>（漏了 Software\Classes 前缀）
        DeleteHkcuRootJunk();
    }

    /// <summary>
    /// 撤销本程序建立的全部文件关联。
    /// 做法是扫描 Classes 下所有扩展名键，凡是默认值指向 <c>zzzip.assoc.*</c> 的都还原 ——
    /// 比只遍历内置格式列表更彻底，用户在【选项】里勾过的任何扩展名都能覆盖到。
    /// </summary>
    private static void ResetOwnedAssociations()
    {
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(ClassesRoot);
            if (classes?.GetSubKeyNames() is not { } names) return;

            foreach (var n in names)
            {
                if (!n.StartsWith('.') || n.StartsWith(".zzzip.", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var ext = n.TrimStart('.');

                    // 默认动词和图标要先收：它们会残留 —— 用户后来自己改过默认程序时，
                    // 扩展名默认值已经不属于我们，但 zzzip.open 与 .ext\DefaultIcon 还在。
                    ReleaseDefaultVerb(ext);
                    ReleaseExtensionIcon(ext);

                    if (!IsAssociated(ext)) continue;

                    // 还原到关联前记录的默认程序（没有记录才只是清空），
                    // 否则卸载会把用户原来的 WinRAR / 7-Zip 关联一起弄丢。
                    RestoreDefault(ext);
                    ClearUserChoice(ext);
                }
                catch { /* 单个扩展名失败不应中断整体清理 */ }
            }

            // 再删掉所有自定义 ProgID
            foreach (var n in names)
                if (n.StartsWith("zzzip.assoc.", StringComparison.OrdinalIgnoreCase))
                    TryDelete($@"{ClassesRoot}\{n}");
        }
        catch { /* 权限不足时静默 */ }
    }

    /// <summary>删除某个 shell 键下所有以 zzzip. 开头的 verb 子键。</summary>
    private static void DeleteVerbSubKeys(string shellPath)
    {
        try
        {
            using var shell = Registry.CurrentUser.OpenSubKey(shellPath);
            if (shell?.GetSubKeyNames() is not { } names) return;
            // 排除 zzzip.open：它属于「文件关联」而非「右键菜单」。若被菜单重建顺手删掉，
            // 该扩展名的默认动词就指向了一个不存在的 verb，双击会变成没反应。
            foreach (var n in names.Where(n =>
                         n.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)
                         && !n.Equals(OpenVerbKey, StringComparison.OrdinalIgnoreCase)))
                TryDelete($@"{shellPath}\{n}");
        }
        catch { /* 键不存在或无权限 */ }
    }

    /// <summary>清理 1.0 版误建在 HKCU 根下的 <c>.zzzip.*</c> 与 <c>.ext</c> 垃圾键。</summary>
    private static void DeleteHkcuRootJunk()
    {
        try
        {
            using var hkcu = Registry.CurrentUser;
            if (hkcu.GetSubKeyNames() is not { } names) return;

            foreach (var n in names)
            {
                if (n.StartsWith(".zzzip.", StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(n);
                    continue;
                }

                // HKCU\.zip 这类：只有「空壳」或「只指向我们的 ProgID」时才认为是自己造成的
                if (!n.StartsWith('.') || n.Length < 2) continue;
                if (!FormatRegistry.KnownExtensions.Contains(n)) continue;
                if (IsOurLeftoverRootKey(n)) TryDelete(n);
            }
        }
        catch { /* 权限不足时静默 */ }
    }

    private static bool IsOurLeftoverRootKey(string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(name);
            if (k is null) return false;
            if (k.GetSubKeyNames().Length > 0) return false;

            var valueNames = k.GetValueNames();
            if (valueNames.Length == 0) return true;                       // 空壳键，肯定是残留

            return valueNames.All(v => v.Length == 0)
                   && k.GetValue(null) is string s
                   && s.StartsWith("zzzip.assoc.", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch { /* 键不存在或无权限 */ }
    }

    private static void TryWrite(Action action)
    {
        try { action(); }
        catch { /* 写注册表失败不应让 UI 崩掉；调用方可事后用 IsInstalled 复核 */ }
    }

    // ---------------------------------------------------------------- 选项持久化

    public bool IsCascade { get; private set; }

    /// <summary>判断是否已集成：以「*」类下的主菜单项是否存在为准。</summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(
                    $@"{ClassesRoot}\*\shell\{KeyPrefix}{Verbs.AddToArchive}");
                return k?.GetValue(null) is string s && s.Length > 0;
            }
            catch { return false; }
        }
    }

    public void SaveOptions(bool cascade)
    {
        TryWrite(() =>
        {
            using var k = Registry.CurrentUser.CreateSubKey(OptionsKey);
            k?.SetValue("CascadeMenu", cascade ? 1 : 0);
            k?.SetValue("ExePath", _exePath);
        });
        IsCascade = cascade;
    }

    public bool LoadOptions()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(OptionsKey);
            if (k?.GetValue("CascadeMenu") is int v) IsCascade = v == 1;
        }
        catch { /* 读不到就用默认值 */ }
        return IsCascade;
    }

    // ---------------------------------------------------------------- 文件关联

    /// <summary>扩展名对应的自定义 ProgID。</summary>
    public static string ProgIdFor(string extension) =>
        "zzzip.assoc." + extension.TrimStart('.').ToLowerInvariant();

    /// <summary>
    /// 关联前备份用户原有默认 ProgID 的位置。
    /// 【为什么需要】Windows 的「默认打开方式」就是 <c>Classes\.ext</c> 的默认值。
    /// 我们把它改成 zzzip 之后，如果取消关联时只是把默认值删掉，用户原来的
    /// WinRAR / 7-Zip 关联就永久丢了 —— 必须记下原值再还原。
    /// </summary>
    private const string BackupKey = @"Software\zzzip\AssocBackup";

    /// <summary>备份值：该扩展名原本就没有默认 ProgID（不是"没备份"）。</summary>
    private const string NoProgId = "<none>";

    /// <summary>
    /// 「打开」verb 的键名 —— 双击压缩包时进的应该是主界面，不是就地解压。
    ///
    /// 【为什么必须自己造一个 open】Windows 挑默认动词的规则是：
    ///   ① 先看 <c>ProgID\shell</c> 的默认值；② 没人指定时，取合并后动词列表的第一个。
    /// 我们的解压 verb 挂在 <c>.ext\shell</c> 下（挂这里才能在「zzzip 不是默认程序」时
    /// 也保持右键可用），而 ProgID 的 shell 默认值原先没设，于是
    /// <c>zzzip.here</c>（字母序最前）被当成了默认动作 —— 用户双击压缩包会被直接就地解压，
    /// 这在桌面上一堆文件的场景下是有数据风险的。补上这个 open verb 并显式认领默认动词，
    /// 双击才会变成「打开」。
    ///
    /// 它属于「文件关联」而不是「右键菜单」，所以不随菜单开关一起清理
    /// （见 <see cref="DeleteVerbSubKeys"/> 的排除规则），由 Associate / Dissociate 单独管理。
    /// </summary>
    private const string OpenVerbKey = "zzzip.open";

    /// <summary>「打开」verb 的菜单文字。键名带前缀就不在系统内置动词表里，必须自己给文字。</summary>
    private const string OpenVerbLabel = "打开";

    /// <summary>备份值：该扩展名原本没有指定默认动词。</summary>
    private const string NoShellVerb = "<none>";

    /// <summary>该扩展名当前是否已关联到 zzzip。</summary>
    public static bool IsAssociated(string extension)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.{extension.TrimStart('.')}");
            return string.Equals(k?.GetValue(null) as string, ProgIdFor(extension), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string ReadDefaultProgId(string ext)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.{ext}");
            return k?.GetValue(null) as string ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// 关联前记下用户原有的默认 ProgID。
    /// 只在「还没备份过」时写：用户可能反复勾选/取消，如果每次都覆盖，
    /// 第二次就会把我们自己刚写进去的 zzzip.assoc.* 当成"原值"存起来，
    /// 取消关联后反而指向一个不存在的程序。
    /// </summary>
    private static void BackupDefault(string ext)
    {
        var current = ReadDefaultProgId(ext);
        if (current.StartsWith("zzzip.assoc.", StringComparison.OrdinalIgnoreCase)) return;

        TryWrite(() =>
        {
            using var bk = Registry.CurrentUser.CreateSubKey(BackupKey);
            if (bk is null) return;
            if (bk.GetValue(ext) is null)
                bk.SetValue(ext, current.Length == 0 ? NoProgId : current);
        });
    }

    /// <summary>还原成关联前记录的默认 ProgID；没有备份记录时只把默认值清掉。</summary>
    private static void RestoreDefault(string ext)
    {
        string? backup = null;
        try
        {
            using var bk = Registry.CurrentUser.OpenSubKey(BackupKey);
            backup = bk?.GetValue(ext) as string;
        }
        catch { /* 读不到就按"没有备份"处理 */ }

        TryWrite(() =>
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.{ext}", writable: true);
            if (k is null) return;
            if (backup is null || backup == NoProgId)
                k.DeleteValue(string.Empty, throwOnMissingValue: false);
            else
                k.SetValue(null, backup);
        });

        DropBackup(ext);
    }

    /// <summary>丢弃某个扩展名的备份记录（不影响默认值）。</summary>
    private static void DropBackup(string ext)
    {
        TryWrite(() =>
        {
            using var bk = Registry.CurrentUser.OpenSubKey(BackupKey, writable: true);
            bk?.DeleteValue(ext, throwOnMissingValue: false);
        });
    }

    // ---------------------------------------------------------------- 默认动词与图标
    //
    // 这两项都写「扩展名键本身」（.ext\shell 的默认值、.ext\DefaultIcon），
    // 因为 Win10/11 在 FileExts\.ext\UserChoice 缺失时并不总会采用我们写在 Classes 里的
    // ProgID —— 实测双击命令与文件图标都取自扩展名键这一层。两者都按同样的
    // 「先备份、只在没被人占用时才写、卸载可还原」模式处理。

    /// <summary>默认动词 / 图标的备份记录名（与 ProgID 备份共用同一个键，用后缀区分）。</summary>
    private static string ShellVerbBackupName(string ext) => ext + "|verb";
    private static string IconBackupName(string ext) => ext + "|icon";

    private static string? ReadBackup(string name)
    {
        try
        {
            using var bk = Registry.CurrentUser.OpenSubKey(BackupKey);
            return bk?.GetValue(name) as string;
        }
        catch { return null; }
    }

    /// <summary>丢弃备份键下的一条记录（只清记录，不动注册表里的实际现状）。</summary>
    private static void DropValue(string name)
    {
        TryWrite(() =>
        {
            using var bk = Registry.CurrentUser.OpenSubKey(BackupKey, writable: true);
            bk?.DeleteValue(name, throwOnMissingValue: false);
        });
    }

    /// <summary>该扩展名 shell 键上当前的默认动词（空串 = 未指定）。</summary>
    private static string ReadShellDefaultVerb(string ext)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.{ext}\shell");
            return k?.GetValue(null) as string ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// 写「打开」verb。命令刻意不带 <c>--verb=</c>：资源管理器传进来一个裸路径，
    /// 主窗口直接打开并浏览这个压缩包（与 ProgID 的 open 命令完全一致）。
    /// </summary>
    private static void WriteOpenVerb(string ext, string exePath)
    {
        TryWrite(() =>
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{ext}\shell\{OpenVerbKey}");
            if (k is null) return;
            k.SetValue(null, OpenVerbLabel);
            k.SetValue("Icon", $"\"{exePath}\",0");
            using var cmd = k.CreateSubKey("command");
            cmd?.SetValue(null, $"\"{exePath}\" \"%1\"");
        });
    }

    /// <summary>
    /// 把「打开」指定为该扩展名的默认动词 —— 也就是双击动作、右键菜单里的加粗项。
    ///
    /// 只在原本没有指定默认动词时才写入：如果别的压缩软件已经占了这个位置
    /// （比如 7-Zip 把自己设成了默认动词），我们就不越权抢它。
    /// </summary>
    private static void ClaimDefaultVerb(string ext)
    {
        var existing = ReadShellDefaultVerb(ext);

        // 已经被别人指定过默认动词：不抢，也别留下无用备份
        if (existing.Length > 0 && !existing.Equals(OpenVerbKey, StringComparison.OrdinalIgnoreCase))
        {
            DropValue(ShellVerbBackupName(ext));
            return;
        }

        TryWrite(() =>
        {
            // 备份只在「还没备份过」时写：反复勾选关联时，第二次不能把
            // 我们刚写进去的 zzzip.open 当成"用户原来的默认动词"存起来。
            using var bk = Registry.CurrentUser.CreateSubKey(BackupKey);
            if (bk?.GetValue(ShellVerbBackupName(ext)) is null)
                bk?.SetValue(ShellVerbBackupName(ext),
                             existing.Length == 0 ? NoShellVerb : existing);

            using var k = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{ext}\shell");
            k?.SetValue(null, OpenVerbKey);
        });
    }

    /// <summary>还原默认动词，并删掉我们的「打开」verb（幂等，可反复调用）。</summary>
    private static void ReleaseDefaultVerb(string ext)
    {
        var backup = ReadBackup(ShellVerbBackupName(ext));
        TryWrite(() =>
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.{ext}\shell", writable: true);
            if (k is not null
                && string.Equals(k.GetValue(null) as string, OpenVerbKey, StringComparison.OrdinalIgnoreCase))
            {
                if (backup is null || backup == NoShellVerb)
                    k.DeleteValue(string.Empty, throwOnMissingValue: false);
                else
                    k.SetValue(null, backup);
            }
        });

        TryDelete($@"{ClassesRoot}\.{ext}\shell\{OpenVerbKey}");
        DropValue(ShellVerbBackupName(ext));
        RemoveEmptyShellKey(ext);
    }

    /// <summary>
    /// 顺手清掉已经空掉的 <c>.ext\shell</c> 键（绿色承诺：卸载不留残留）。
    /// 只剩空壳键虽然无害，但用户拿注册表编辑器一看会以为没卸干净。
    /// </summary>
    private static void RemoveEmptyShellKey(string ext)
    {
        var path = $@"{ClassesRoot}\.{ext}\shell";
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(path))
            {
                if (k is null) return;
                if (k.GetSubKeyNames().Length > 0 || k.GetValueNames().Length > 0) return;
            }
            Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
        catch { /* 键不存在或被占用，无需处理 */ }
    }

    /// <summary>
    /// 给扩展名键本身写 zzzip 图标。系统取图标的顺序是 <c>.ext\DefaultIcon</c> 先于
    /// ProgID 的 DefaultIcon，而资源管理器不总会采用我们写的 ProgID（见本节开头说明），
    /// 结果就是「关联看着成功了、图标还是系统那张空白文档」。写在这里才稳定生效。
    ///
    /// 同样不抢别人的：已经被别的程序指定过图标就原样不动。
    /// </summary>
    private static void ClaimExtensionIcon(string ext, string exePath)
    {
        var ourIcon = $"\"{exePath}\",0";
        TryWrite(() =>
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{ext}");
            using var icon = k?.CreateSubKey("DefaultIcon");
            if (icon is null) return;

            var existing = icon.GetValue(null) as string ?? string.Empty;
            var weWroteIt = ReadBackup(IconBackupName(ext)) is not null;

            // 不是我们写的、而且已经有值 —— 那是别人的图标，不覆盖
            if (!weWroteIt && existing.Length > 0) return;

            using var bk = Registry.CurrentUser.CreateSubKey(BackupKey);
            if (bk?.GetValue(IconBackupName(ext)) is null)
                bk?.SetValue(IconBackupName(ext), existing.Length == 0 ? NoShellVerb : existing);

            icon.SetValue(null, ourIcon);
        });
    }

    /// <summary>还原扩展名图标（只在我们确实写过时才动，幂等）。</summary>
    private static void ReleaseExtensionIcon(string ext)
    {
        var backup = ReadBackup(IconBackupName(ext));
        if (backup is null) return;      // 没写过，就绝不碰用户/别人的设置

        TryWrite(() =>
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.{ext}", writable: true);
            using var icon = k?.OpenSubKey("DefaultIcon", writable: true);
            if (icon is null) return;
            if (backup == NoShellVerb)
                icon.DeleteValue(string.Empty, throwOnMissingValue: false);
            else
                icon.SetValue(null, backup);
        });

        // 连空壳 DefaultIcon 一起收走
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.{ext}\DefaultIcon"))
            {
                if (k is not null && k.GetValueNames().Length == 0 && k.GetSubKeyNames().Length == 0)
                {
                    k.Close();
                    Registry.CurrentUser.DeleteSubKey($@"{ClassesRoot}\.{ext}\DefaultIcon", throwOnMissingSubKey: false);
                }
            }
        }
        catch { }

        DropValue(IconBackupName(ext));
    }

    /// <summary>
    /// 把指定扩展名的默认打开程序设为 zzzip。
    ///
    /// 注意两点：
    ///  1. 键必须写在 <c>HKCU\Software\Classes\.ext</c>（1.0 版漏了 Software\Classes 前缀，
    ///     写到了 HKCU\.ext，等于什么都没做）；
    ///  2. Win10/11 只要 `FileExts\.ext\UserChoice` 存在就会盖过 Classes 的默认值，
    ///     必须把 UserChoice 删掉才能让新关联生效。删除一般可行（用户是键的所有者），
    ///     写入则会被 ACL 拒绝 —— 失败时返回 false，由调用方提示用户手动改。
    /// </summary>
    public static bool Associate(IEnumerable<string> extensions, string exePath)
    {
        var exe = ResolveExe(exePath);
        var allOk = true;

        foreach (var ext in extensions.Select(e => e.TrimStart('.')))
        {
            WriteProgId(ext, exe);
            WriteOpenVerb(ext, exe);             // 双击的默认动作：打开界面，而不是就地解压
            ClaimDefaultVerb(ext);               // 加粗项 / 双击落在这个「打开」上
            ClaimExtensionIcon(ext, exe);        // 让资源管理器真的显示 zzzip 图标
            BackupDefault(ext);                  // 必须先备份，后覆盖默认值

            TryWrite(() =>
            {
                using var k = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{ext}");
                k?.SetValue(null, ProgIdFor(ext));
            });

            if (!ClearUserChoice(ext)) allOk = false;
        }

        NotifyShell();
        return allOk;
    }

    /// <summary>取消关联：还原成关联前的默认程序，而不是把默认值一删了事。</summary>
    public static void Dissociate(IEnumerable<string> extensions)
    {
        foreach (var ext in extensions.Select(e => e.TrimStart('.')))
        {
            // 这两个都自带幂等保护：不是我们写进去的就不碰，
            // 所以即便用户中途自己改过默认程序，也不会被这里误伤。
            ReleaseDefaultVerb(ext);
            ReleaseExtensionIcon(ext);

            if (IsAssociated(ext))
            {
                RestoreDefault(ext);
                ClearUserChoice(ext);
            }
            else
            {
                // 默认值已经不是我们的（用户自己改过）：不动它，也别留下无用备份
                DropBackup(ext);
            }

            TryDelete($@"{ClassesRoot}\{ProgIdFor(ext)}");
        }

        NotifyShell();
    }

    /// <summary>删除 UserChoice / UserChoiceLatest，让 Classes 里的默认值重新生效。</summary>
    private static bool ClearUserChoice(string ext)
    {
        var ok = true;
        foreach (var sub in new[] { "UserChoice", "UserChoiceLatest" })
        {
            var path = $@"{FileExtsRoot}\.{ext}\{sub}";
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(path);
                if (k is null) continue;              // 本来就没有，无需处理
            }
            catch { continue; }

            try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
            catch { ok = false; }                     // 被 ACL 挡住，需要用户手动改
        }
        return ok;
    }

    // ---------------------------------------------------------------- 资源管理器刷新

    /// <summary>刷新资源管理器缓存，否则要重启 explorer 才能看到菜单。</summary>
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
}
