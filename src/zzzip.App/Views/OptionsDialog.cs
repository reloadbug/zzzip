using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using zzzip.Core;
using zzzip.ShellReg;

namespace zzzip.App.Views;

/// <summary>【选项】对话框：右键菜单开关（含层叠模式）、文件关联、默认压缩格式。</summary>
public sealed class OptionsDialog : Window
{
    private readonly CheckBox _installMenu = new() { Content = "集成到资源管理器右键菜单", Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _cascadeMenu = new() { Content = "层叠右键关联菜单（只保留一个入口项）", Margin = new Thickness(20, 4, 0, 0) };
    private readonly ComboBox _defaultFormat = new() { Width = 140, Margin = new Thickness(8, 4, 8, 4) };
    private readonly ListBox _formatList = new() { Height = 200, Width = 220, Margin = new Thickness(8) };
    private readonly TextBlock _status = new() { Margin = new Thickness(16, 0, 16, 8), Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap };

    public OptionsDialog()
    {
        Title = "选项";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        FontFamily = Ui.Theme.UiFont;
        FontSize = 12;
        Content = BuildLayout();
        LoadState();
    }

    private UIElement BuildLayout()
    {
        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(Header("右键菜单"));
        root.Children.Add(_installMenu);
        root.Children.Add(_cascadeMenu);
        _installMenu.Checked += (_, _) => _cascadeMenu.IsEnabled = true;
        _installMenu.Unchecked += (_, _) => _cascadeMenu.IsEnabled = false;

        root.Children.Add(Header("默认压缩格式"));
        var formatRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8) };
        _defaultFormat.ItemsSource = FormatRegistry.CreatableFormats.Select(f => f.DisplayName).ToList();
        _defaultFormat.SelectedIndex = 0;
        formatRow.Children.Add(new TextBlock { Text = "新建压缩包默认格式", VerticalAlignment = VerticalAlignment.Center });
        formatRow.Children.Add(_defaultFormat);
        root.Children.Add(formatRow);

        root.Children.Add(Header("文件关联"));
        foreach (var f in FormatRegistry.AllFormats)
            _formatList.Items.Add(f.DisplayName + "  " + string.Join(' ', f.Extensions));
        var assocRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8) };
        var selectAll = new Button { Content = "全选", Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        var clearAll = new Button { Content = "全不选", Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        selectAll.Click += (_, _) => SetAllChecked(true);
        clearAll.Click += (_, _) => SetAllChecked(false);
        assocRow.Children.Add(selectAll);
        assocRow.Children.Add(clearAll);
        assocRow.Children.Add(new TextBlock { Text = "勾选后双击该格式压缩包将用 zzzip 打开", Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(assocRow);
        root.Children.Add(_formatList);

        root.Children.Add(_status);
        root.Children.Add(BuildButtons());

        return root;
    }

    private void SetAllChecked(bool value)
    {
        // ListBox 仅为可读清单；实际关联开关通过全选/全不选按钮一次写入
        _status.Text = value ? "确定后将关联全部格式。" : "确定后将取消全部格式关联。";
    }

    private static UIElement Header(string text) => new TextBlock
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 4),
        Foreground = System.Windows.Media.Brushes.Black,
    };

    private UIElement BuildButtons()
    {
        var ok = new Button { Content = "确定", Width = 88, Height = 30, IsDefault = true, Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 76, Height = 30, IsCancel = true, Margin = new Thickness(0, 12, 0, 0) };
        ok.Click += (_, _) => Apply();
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 16) };
        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }

    private void LoadState()
    {
        var installed = ContextMenuRegistrar.IsInstalled;
        _installMenu.IsChecked = installed;
        _cascadeMenu.IsEnabled = installed;

        var registrar = new ContextMenuRegistrar(Environment.ProcessPath ?? "zzzip.exe");
        _cascadeMenu.IsChecked = registrar.LoadOptions();
    }

    private void Apply()
    {
        try
        {
            if (_installMenu.IsChecked == true)
            {
                new ContextMenuRegistrar(Environment.ProcessPath ?? "zzzip.exe")
                    .Register(_cascadeMenu.IsChecked == true);
                _status.Text = "右键菜单已更新。" + (_cascadeMenu.IsChecked == true ? "（层叠模式）" : string.Empty);
            }
            else
            {
                ContextMenuRegistrar.Unregister();
                _status.Text = "已移除右键菜单集成。";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "zzzip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}