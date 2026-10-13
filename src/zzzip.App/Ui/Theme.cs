using System.Windows.Media;

namespace zzzip.App.Ui;

/// <summary>zzzip 视觉规范：浅色扁平 + 绿色主色，对标 360 压缩的清新风格。</summary>
public static class Theme
{
    public static Color Accent { get; } = Color.FromRgb(0x19, 0xA5, 0x5A);
    public static Color AccentHover { get; } = Color.FromRgb(0x13, 0x8C, 0x48);
    public static Color WindowBg { get; } = Color.FromRgb(0xF6, 0xF7, 0xF8);
    public static Color PanelBg { get; } = Colors.White;
    public static Color MenuBarBg { get; } = Color.FromRgb(0xFA, 0xFA, 0xFB);
    public static Color BorderSoft { get; } = Color.FromRgb(0xE3, 0xE6, 0xE9);
    public static Color TextPrimary { get; } = Color.FromRgb(0x23, 0x27, 0x2B);
    public static Color TextSecondary { get; } = Color.FromRgb(0x8A, 0x90, 0x99);
    public static Color StatusBg { get; } = Color.FromRgb(0xF1, 0xF3, 0xF4);
    public static Color Hover { get; } = Color.FromArgb(0x1F, 0x19, 0xA5, 0x5A);

    public static Brush AccentBrush { get; } = Frozen(Accent);
    public static Brush AccentHoverBrush { get; } = Frozen(AccentHover);
    public static Brush WindowBgBrush { get; } = Frozen(WindowBg);
    public static Brush PanelBrush { get; } = Frozen(PanelBg);
    public static Brush MenuBarBrush { get; } = Frozen(MenuBarBg);
    public static Brush BorderSoftBrush { get; } = Frozen(BorderSoft);
    public static Brush TextPrimaryBrush { get; } = Frozen(TextPrimary);
    public static Brush TextSecondaryBrush { get; } = Frozen(TextSecondary);
    public static Brush StatusBrush { get; } = Frozen(StatusBg);
    public static Brush HoverBrush { get; } = Frozen(Hover);

    public static FontFamily UiFont { get; } = new("Microsoft YaHei UI, Segoe UI");

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>字节数格式化为中文习惯：1.00 KB / 12.34 MB / 1.05 GB。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? string.Format("{0} {1}", (long)v, units[i])
                     : string.Format("{0:0.00} {1}", v, units[i]);
    }

    public static string FormatDate(DateTimeOffset t) =>
        t == default ? "-" : t.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public static string FormatRatio(double r) =>
        r <= 0 ? "-" : string.Format("{0:0.0}%", r * 100);
}