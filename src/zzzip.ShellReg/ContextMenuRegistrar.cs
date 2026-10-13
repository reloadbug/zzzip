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
/// </summary>
public sealed class ContextMenuRegistrar
{
    /// <summary>命令行动作标识，与 Program.cs 的参数解析一一对应。</summary>
    public static class Verbs
    {
        public const string AddToArchive = "add";       // 添加到压缩文件…
        public const string QuickZip = "quickzip";      // 添加到"文件名.zip"
        public const string HighRatio7z = "7z";         // 添加到高压缩比文件"文件名.7z"
        public const string EachSeparately = "each";    // 压缩每个文件到单独的压缩包中
        public const string ZipAndMail = "mail";        // 添加到"文件名.zip"并邮件
        public const string ExtractToOwn = "own";       // 解压到"文件名\"
        public const string ExtractHere = "here";       // 解压到当前文件夹
        public const string ExtractTo = "to";           // 解压到指定文件夹…
    }

    private const string RegPath = @"Software\zzzip";
    private const string MenuKey = @"Software\Classes\*\shellex\ContextMenuHandlers\zzzip";
    private const string MenuKeyDir = @"Software\Classes\Directory\shellex\ContextMenuHandlers\zzzip";
    private const string DriveMenuKey = @"Software\Classes\Drive\shellex\ContextMenuHandlers\zzzip";

    private readonly string _exePath;
    private readonly string _verbRoot;   // HKCU\Software\Classes\.zzzip.<guid>\shell

    public ContextMenuRegistrar(string exePath)
    {
        _exePath = Path.GetFullPath(exePath);
        var id = Guid.NewGuid().ToString("N");
        _verbRoot = $@".zzzip.{id}\shell";
    }

    // ---------------------------------------------------------------- 菜单文案（与 360 一致）

    /// <summary>普通文件/文件夹的菜单项（文字与顺序对齐 360）。</summary>
    public static IReadOnlyList<(string Verb, string Label, bool IsSub)> FileMenuItems(bool cascade) => cascade
        ? new[] { (Verbs.AddToArchive, "添加到压缩文件…", false) }
        : new[]
        {
            (Verbs.AddToArchive,   "添加到压缩文件…",                           false),
            (Verbs.QuickZip,       "添加到\"%f.zip\"",                          false),
            ("__sub__",            "其他压缩命令",                               true ),
            (Verbs.HighRatio7z,    "    添加到高压缩比文件\"%f.7z\"",             false),
            (Verbs.EachSeparately, "    压缩每个文件到单独的压缩包中",           false),
            (Verbs.ZipAndMail,     "    添加到\"%f.zip\"并邮件",               false),
        };

    /// <summary>压缩包右键菜单项（顺序与文字对齐 360，第一项为默认项）。</summary>
    public static IReadOnlyList<(string Verb, string Label, bool IsSub)> ArchiveMenuItems(bool cascade) => cascade
        ? new[] { (Verbs.ExtractToOwn, "解压到\"%f\"", false) }
        : new[]
        {
            (Verbs.ExtractToOwn, "解压到\"%f\\\"",       false),
            (Verbs.ExtractHere,  "解压到当前文件夹",        false),
            (Verbs.ExtractTo,    "解压到指定文件夹…",       false),
        };

    // ---------------------------------------------------------------- 注册

    /// <summary>写入右键菜单并刷新资源管理器缓存。返回已注册的处理器名。</summary>
    public IReadOnlyList<string> Register(bool cascade = false)
    {
        RegisterFileVerbs(cascade);
        RegisterArchiveVerbs(cascade);

        SetHandler(MenuKey, "ZZZIP.FileMenu");
        SetHandler(MenuKeyDir, "ZZZIP.FileMenu");
        SetHandler(DriveMenuKey, "ZZZIP.FileMenu");

        foreach (var ext in FormatRegistry.AllExtNames())
        {
            SetHandler($@"Software\Classes\{ext}\shellex\ContextMenuHandlers\zzzip", "ZZZIP.ArchiveMenu");

            // 在自定义类里登记 open 命令：供「打开方式」与双击兜底，不动系统已有关联
            using var cmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\zzzip.assoc.{ext}\shell\open\command");
            cmd?.SetValue("", $"\"{_exePath}\" \"%1\"");
        }

        SaveOptions(cascade);
        NotifyShell();
        return cascade
            ? new[] { "ZZZIP.FileMenu(层叠)", "ZZZIP.ArchiveMenu(层叠)" }
            : new[] { "ZZZIP.FileMenu", "ZZZIP.ArchiveMenu" };
    }

    public void RegisterFileVerbs(bool cascade)
    {
        foreach (var (verb, label, isSub) in FileMenuItems(cascade))
        {
            if (isSub) { WriteSubMenuShell(); continue; }
            WriteVerb(verb, label.Replace("%f", "%1"), isDefault: verb == Verbs.AddToArchive);
        }
    }

    public void RegisterArchiveVerbs(bool cascade)
    {
        foreach (var (verb, label, _) in ArchiveMenuItems(cascade))
            WriteVerb(verb, label.Replace("%f", "%1"), isDefault: verb == Verbs.ExtractToOwn);
    }

    private void WriteVerb(string verb, string label, bool isDefault)
    {
        using var k = Registry.CurrentUser.CreateSubKey($@"{_verbRoot}\{verb}");
        if (k is null) return;
        k.SetValue("", label);
        k.SetValue("Icon", $"\"{_exePath}\",0");
        if (isDefault) k.SetValue("Extended", 0);
    }

    /// <summary>「其他压缩命令」级联子菜单（对应 360 的 其他压缩命令 ▸）。</summary>
    private void WriteSubMenuShell()
    {
        using var k = Registry.CurrentUser.CreateSubKey($@"{_verbRoot}\__sub__");
        if (k is null) return;
        k.SetValue("", "其他压缩命令");
        k.SetValue("SubMenu", "");
    }

    private void SetHandler(string path, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(path);
        k?.SetValue("", value);
    }

    // ---------------------------------------------------------------- 卸载（无残留）

    public static void Unregister()
    {
        TryDelete(MenuKey);
        TryDelete(MenuKeyDir);
        TryDelete(DriveMenuKey);

        foreach (var ext in FormatRegistry.AllExtNames())
        {
            TryDelete($@"Software\Classes\{ext}\shellex\ContextMenuHandlers\zzzip");
            TryDelete($@"Software\Classes\zzzip.assoc.{ext}");
        }

        // 清理本程序创建的 .zzzip.* 类键（按前缀扫描）
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes");
            if (classes?.GetSubKeyNames() is { } names)
                foreach (var n in names.Where(n => n.StartsWith(".zzzip.", StringComparison.OrdinalIgnoreCase)))
                    TryDelete($@"Software\Classes\{n}");
        }
        catch { /* 权限不足时静默 */ }

        TryDelete(RegPath);
        NotifyShell();
    }

    private static void TryDelete(string path)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch { /* 键不存在或无权限 */ }
    }

    // ---------------------------------------------------------------- 选项持久化

    public bool IsCascade { get; private set; }

    public static bool IsInstalled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(MenuKey);
            return k?.GetValue(null) is string s && s.Length > 0;
        }
    }

    public void SaveOptions(bool cascade)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RegPath);
        k?.SetValue("CascadeMenu", cascade ? 1 : 0);
        k?.SetValue("ExePath", _exePath);
        IsCascade = cascade;
    }

    public bool LoadOptions()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RegPath);
        if (k?.GetValue("CascadeMenu") is int v) IsCascade = v == 1;
        return IsCascade;
    }

    // ---------------------------------------------------------------- 文件关联

    /// <summary>把指定扩展名的默认打开程序设为 zzzip（工具 → 选项 → 文件关联）。</summary>
    public static void Associate(IEnumerable<string> extensions)
    {
        foreach (var ext in extensions.Select(e => e.TrimStart('.')))
        {
            using var k = Registry.CurrentUser.CreateSubKey($".{ext}");
            k?.SetValue("", $"zzzip.assoc.{ext}");
        }
        NotifyShell();
    }

    public static void Dissociate(IEnumerable<string> extensions)
    {
        foreach (var ext in extensions.Select(e => e.TrimStart('.')))
        {
            using var k = Registry.CurrentUser.OpenSubKey($".{ext}", writable: true);
            if (k?.GetValue(null) is string s && s.StartsWith("zzzip.assoc.", StringComparison.OrdinalIgnoreCase))
                k.DeleteValue(string.Empty, throwOnMissingValue: false);
        }
        NotifyShell();
    }

    /// <summary>刷新资源管理器缓存，否则要重启 explorer 才能看到菜单。</summary>
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
}