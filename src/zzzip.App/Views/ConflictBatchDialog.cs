using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using zzzip.Core;

namespace zzzip.App.Views;

/// <summary>
/// 【同名文件】前置提示：只告诉用户「一共有几处冲突」，让他先选一次
/// 是要一键决定，还是进去逐个对比。
///
/// 冲突文件动辄几十上百个，如果一上来就逐个弹窗，用户只能靠一路点「全部覆盖」
/// 杀出重围 —— 那还不如一开始就把这个选择摆出来（360 压缩也是这个做法）。
/// </summary>
public sealed class ConflictBatchDialog : Window
{
    /// <summary>true 表示用户选了「逐个确认」，由调用方接着弹逐个对比对话框。</summary>
    public bool ReviewIndividually { get; private set; }

    /// <summary>用户直接选定的一键策略；选了「逐个确认」时无意义。</summary>
    public ConflictDecision Choice { get; private set; } = ConflictDecision.Cancel;

    public ConflictBatchDialog(int total)
    {
        Title = "发现同名文件";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = Ui.Theme.UiFont;
        FontSize = 12;
        Content = BuildLayout(total);
    }

    private UIElement BuildLayout(int total)
    {
        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };

        root.Children.Add(new TextBlock
        {
            Text = string.Format("目标文件夹中已存在 {0} 个同名文件", total),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        root.Children.Add(new TextBlock
        {
            Text = "可以一次性决定，也可以进去逐个对比后再决定。\n"
                 + "「全部」只对本次解压有效，不会记住。",
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        row.Children.Add(Button("全部覆盖", ConflictDecision.OverwriteAll, 88));
        row.Children.Add(Button("全部跳过", ConflictDecision.SkipAll, 88));
        row.Children.Add(Review("逐个确认…", 96));
        row.Children.Add(Cancel("取消", 68));
        root.Children.Add(row);

        return root;
    }

    private Button Button(string text, ConflictDecision decision, double width)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
        };
        button.Click += (_, _) =>
        {
            Choice = decision;
            ReviewIndividually = false;
            DialogResult = true;
        };
        return button;
    }

    private Button Review(string text, double width)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,   // 默认走「看一眼再决定」，最不容易误操作
        };
        button.Click += (_, _) =>
        {
            ReviewIndividually = true;
            Choice = ConflictDecision.Cancel;
            DialogResult = true;
        };
        return button;
    }

    private Button Cancel(string text, double width)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
        };
        button.Click += (_, _) =>
        {
            Choice = ConflictDecision.Cancel;
            ReviewIndividually = false;
            DialogResult = false;
        };
        return button;
    }
}
