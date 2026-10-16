using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using zzzip.Core;

namespace zzzip.App.Views;

/// <summary>
/// 【同名文件】逐个对比对话框：把磁盘上已有的文件和压缩包里的同名文件摆在一起，
/// 显示各自的路径、大小、修改时间，让用户看清楚再决定。
///
/// 按钮为「覆盖 / 全部覆盖 / 跳过 / 全部跳过 / 重命名 / 取消」，
/// 与 360 压缩、WinRAR 的同功能一致：「全部xx」= 这一个这么办，后面的都不再问。
/// </summary>
public sealed class FileConflictDialog : Window
{
    /// <summary>用户的最终选择；窗口被直接关掉时保持「取消」。</summary>
    public ConflictDecision Decision { get; private set; } = ConflictDecision.Cancel;

    public FileConflictDialog(FileConflict conflict)
    {
        Title = "发现同名文件";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = Ui.Theme.UiFont;
        FontSize = 12;
        Content = BuildLayout(conflict);
    }

    private UIElement BuildLayout(FileConflict conflict)
    {
        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

        root.Children.Add(new TextBlock
        {
            Text = conflict.Total > 1
                ? string.Format("目标文件夹中已存在同名文件（第 {0} 个，共 {1} 个）", conflict.Index, conflict.Total)
                : "目标文件夹中已存在同名文件",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2),
        });
        root.Children.Add(new TextBlock
        {
            Text = "请选择处理方式：",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 12),
        });

        root.Children.Add(Side("现有文件（磁盘上）", conflict.TargetPath,
                               conflict.ExistingSize, conflict.ExistingModifiedUtc));
        root.Children.Add(Side("压缩包内文件", conflict.EntryPath,
                               conflict.Entry.Size, conflict.Entry.ModifiedUtc));

        if (conflict.SameSize)
        {
            root.Children.Add(new TextBlock
            {
                Text = "两侧大小相同，可能只是同一个文件的旧版本。",
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        root.Children.Add(new TextBlock
        {
            Text = "「重命名」会把解压出来的文件改名为 “文件名 (1).扩展名”，原有文件不动。",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });

        root.Children.Add(BuildButtons());
        return root;
    }

    private static UIElement Side(string caption, string path, long size, DateTimeOffset modifiedUtc)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap });
        inner.Children.Add(new TextBlock
        {
            Text = string.Format("{0}    修改时间：{1:yyyy-MM-dd HH:mm:ss}",
                                 FormatSize(size), modifiedUtc.ToLocalTime()),
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 3, 0, 0),
        });

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock { Text = caption, Foreground = Brushes.Gray });
        panel.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 3, 0, 0),
            Child = inner,
        });
        return panel;
    }

    private UIElement BuildButtons()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0),
        };

        row.Children.Add(Action("覆盖", ConflictDecision.Overwrite, 74));
        row.Children.Add(Action("全部覆盖", ConflictDecision.OverwriteAll, 88));
        row.Children.Add(Action("跳过", ConflictDecision.Skip, 66, isDefault: true));
        row.Children.Add(Action("全部跳过", ConflictDecision.SkipAll, 88));
        row.Children.Add(Action("重命名", ConflictDecision.Rename, 74));
        row.Children.Add(Action("取消", ConflictDecision.Cancel, 66));
        return row;
    }

    private Button Action(string text, ConflictDecision decision, double width, bool isDefault = false)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
        };
        button.Click += (_, _) => Choose(decision);
        return button;
    }

    /// <summary>
    /// 统一出口：把所有按钮（含 ESC）都收敛到「设置 Decision 再关窗」。
    /// 「取消」也走 DialogResult = true —— 它表示「用户做了选择」，
    /// 真正的语义由 Decision 携带；直接关窗口才是「没选」。
    /// </summary>
    private void Choose(ConflictDecision decision)
    {
        Decision = decision;
        DialogResult = true;
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        // ESC 等同于点「取消」：中止整个解压
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Choose(ConflictDecision.Cancel);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private static string FormatSize(long bytes)
    {
        var text = bytes.ToString("N0") + " 字节";
        if (bytes >= 1024L * 1024) return text + string.Format("（{0:N2} MB）", bytes / 1024.0 / 1024.0);
        if (bytes >= 1024) return text + string.Format("（{0:N1} KB）", bytes / 1024.0);
        return text;
    }
}
