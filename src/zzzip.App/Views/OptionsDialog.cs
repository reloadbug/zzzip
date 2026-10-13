using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using zzzip.Core;
using zzzip.ShellReg;

namespace zzzip.App.Views;

/// <summary>
/// 【选项】对话框：右键菜单开关（含层叠模式）、默认压缩格式、文件关联。
///
/// 1.0 版这里有两个致命问题，都已修正：
///   · 文件关联列表只是个普通 ListBox，根本没有勾选框，「全选 / 全不选」只改了一行提示文字；
///   · 点「确定」只处理了右键菜单，从头到尾没调用过 Associate / Dissociate，
///     所以「实际上也无法关联」。
/// </summary>
public sealed class OptionsDialog : Window
{
    private readonly CheckBox _installMenu = new() { Content = "集成到资源管理器右键菜单", Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _cascadeMenu = new() { Content = "层叠右键关联菜单（普通文件与压缩包各只保留一个入口项）", Margin = new Thickness(20, 4, 0, 0) };
    private readonly ComboBox _defaultFormat = new() { Width = 140, Margin = new Thickness(8, 4, 8, 4) };
    private readonly StackPanel _formatList = new();
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(16, 4, 16, 8),
        Foreground = Brushes.Gray,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>每个压缩格式一个勾选框；Tag 里放该格式覆盖的全部扩展名。</summary>
    private readonly List<(CheckBox Box, string[] Extensions)> _formatChecks = [];

    private readonly string _exePath = Environment.ProcessPath ?? "zzzip.exe";

    public OptionsDialog()
    {
        Title = "选项";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = Ui.Theme.UiFont;
        FontSize = 12;
        Content = BuildLayout();
        LoadState();
    }

    private UIElement BuildLayout()
    {
        var root = new StackPanel { Margin = new Thickness(16) };

        // ---------------- 右键菜单 ----------------
        root.Children.Add(Header("右键菜单"));
        root.Children.Add(_installMenu);
        root.Children.Add(_cascadeMenu);
        _cascadeMenu.IsEnabled = _installMenu.IsChecked == true;
        _installMenu.Checked += (_, _) => _cascadeMenu.IsEnabled = true;
        _installMenu.Unchecked += (_, _) => _cascadeMenu.IsEnabled = false;
        root.Children.Add(new TextBlock
        {
            Text = "在文件、文件夹上点右键即可压缩 / 解压，选项与 360zip 一致。",
            Foreground = Brushes.Gray,
            Margin = new Thickness(20, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        // ---------------- 默认压缩格式 ----------------
        root.Children.Add(Header("默认压缩格式"));
        var formatRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8) };
        _defaultFormat.ItemsSource = FormatRegistry.CreatableFormats.Select(f => f.DisplayName).ToList();
        _defaultFormat.SelectedIndex = 0;
        formatRow.Children.Add(new TextBlock { Text = "新建压缩包默认格式", VerticalAlignment = VerticalAlignment.Center });
        formatRow.Children.Add(_defaultFormat);
        root.Children.Add(formatRow);

        // ---------------- 文件关联 ----------------
        root.Children.Add(Header("文件关联"));
        var assocRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8) };
        var selectAll = new Button { Content = "全选", Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        var clearAll = new Button { Content = "全不选", Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        selectAll.Click += (_, _) => SetAllChecked(true);
        clearAll.Click += (_, _) => SetAllChecked(false);
        assocRow.Children.Add(selectAll);
        assocRow.Children.Add(clearAll);
        assocRow.Children.Add(new TextBlock
        {
            Text = "勾选后双击该格式压缩包将用 zzzip 打开",
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
        });
        root.Children.Add(assocRow);

        // 每个格式一行复选框，装在可滚动容器里
        foreach (var f in FormatRegistry.AllFormats)
        {
            var exts = f.Extensions.Select(e => e.TrimStart('.')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var box = new CheckBox
            {
                Content = $"{f.DisplayName}   {string.Join(' ', f.Extensions)}",
                Margin = new Thickness(0, 3, 0, 3),
                ToolTip = "关联扩展名：" + string.Join("、", f.Extensions),
            };
            box.Checked += (_, _) => UpdateStatus();
            box.Unchecked += (_, _) => UpdateStatus();
            _formatChecks.Add((box, exts));
        }

        var listPanel = new StackPanel { Margin = new Thickness(20, 0, 8, 0) };
        foreach (var (box, _) in _formatChecks) listPanel.Children.Add(box);

        var listFrame = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE6, 0xE9)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.White,
            Margin = new Thickness(16, 0, 16, 8),
        };
        listFrame.Child = new ScrollViewer
        {
            Height = 210,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8, 6, 8, 6),
            Content = listPanel,
        };
        root.Children.Add(listFrame);

        root.Children.Add(_status);
        root.Children.Add(BuildButtons());

        return root;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 4),
        Foreground = Brushes.Black,
    };

    private UIElement BuildButtons()
    {
        var ok = new Button { Content = "确定", Width = 88, Height = 30, IsDefault = true, Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 76, Height = 30, IsCancel = true, Margin = new Thickness(0, 12, 0, 0) };
        ok.Click += (_, _) => Apply();
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
        };
        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }

    // ---------------------------------------------------------------- 状态

    /// <summary>全选 / 全不选：真正改变每个格式的勾选状态。</summary>
    private void SetAllChecked(bool value)
    {
        foreach (var (box, _) in _formatChecks) box.IsChecked = value;
        UpdateStatus();
    }

    private void LoadState()
    {
        var installed = ContextMenuRegistrar.IsInstalled;
        _installMenu.IsChecked = installed;
        _cascadeMenu.IsEnabled = installed;

        var registrar = new ContextMenuRegistrar(_exePath);
        _cascadeMenu.IsChecked = registrar.LoadOptions();

        // 勾选框回填当前实际关联状态：该格式的全部扩展名都已关联才算勾上
        var checkedCount = 0;
        foreach (var (box, exts) in _formatChecks)
        {
            box.IsChecked = exts.All(ContextMenuRegistrar.IsAssociated);
            if (box.IsChecked == true) checkedCount++;
        }

        UpdateStatus(checkedCount);
    }

    private void UpdateStatus(int? checkedCount = null)
    {
        var n = checkedCount ?? _formatChecks.Count(c => c.Box.IsChecked == true);
        _status.Text = n == 0
            ? "当前未勾选任何格式，确定后不会改变现有关联。"
            : $"已勾选 {n} 个格式，确定后将把对应扩展名关联到 zzzip。";
    }

    // ---------------------------------------------------------------- 应用

    private void Apply()
    {
        string menuResult;
        try
        {
            if (_installMenu.IsChecked == true)
            {
                new ContextMenuRegistrar(_exePath).Register(_cascadeMenu.IsChecked == true);
                menuResult = "右键菜单已更新" + (_cascadeMenu.IsChecked == true ? "（层叠模式）。" : "。");
            }
            else
            {
                // 只摘菜单，不用 Unregister()：文件关联由下面的勾选框独立控制，
                // 不该因为关掉右键菜单就被连带清掉。
                ContextMenuRegistrar.UnregisterMenu();
                menuResult = "已移除右键菜单集成。";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "zzzip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // —— 文件关联：这一块 1.0 版完全没做 ——
        var toAssociate = new List<string>();
        var toDissociate = new List<string>();
        foreach (var (box, exts) in _formatChecks)
        {
            foreach (var ext in exts)
            {
                var want = box.IsChecked == true;
                var has = ContextMenuRegistrar.IsAssociated(ext);
                if (want && !has) toAssociate.Add(ext);
                else if (!want && has) toDissociate.Add(ext);
            }
        }

        var assocResult = "文件关联无变化。";
        var needManual = false;
        if (toAssociate.Count > 0 || toDissociate.Count > 0)
        {
            if (toDissociate.Count > 0) ContextMenuRegistrar.Dissociate(toDissociate);
            var ok = toAssociate.Count == 0 || ContextMenuRegistrar.Associate(toAssociate, _exePath);
            needManual = !ok;

            assocResult = $"已关联 {toAssociate.Count} 个扩展名，已解除 {toDissociate.Count} 个。";
            if (needManual)
                assocResult += "\n部分扩展名由系统「默认应用」锁定，需手动指定。";
        }

        DialogResult = true;
        Close();

        if (needManual)
        {
            var r = MessageBox.Show(
                menuResult + "\n" + assocResult +
                "\n\nWindows 会保护用户手动选过的默认程序（UserChoice），这部分需要你到系统设置里指定。\n现在打开「默认应用」设置页吗？",
                "zzzip", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes) OpenDefaultAppsSettings();
        }
    }

    private static void OpenDefaultAppsSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            MessageBox.Show("请手动打开：设置 → 应用 → 默认应用。", "zzzip");
        }
    }
}
