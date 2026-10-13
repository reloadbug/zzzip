using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using zzzip.Core;

namespace zzzip.App.Views;

/// <summary>【您将创建一个压缩文件】对话框：格式、等级、固实、分卷、密码、注释、自解压 —— 与 360 设置项一致。</summary>
public sealed class CompressDialog : Window
{
    private readonly TextBox _nameBox = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly TextBox _pathBox = new() { Margin = new Thickness(8, 4, 4, 4) };
    private readonly ComboBox _formatBox = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly ComboBox _levelBox = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly ComboBox _volumeBox = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly CheckBox _solidBox = new() { Content = "固实压缩（仅 7Z）", Margin = new Thickness(8, 4, 8, 4) };
    private readonly CheckBox _sfxBox = new() { Content = "创建自解压文件（EXE）", Margin = new Thickness(8, 4, 8, 4) };
    private readonly PasswordBox _passwordBox = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly PasswordBox _password2Box = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly TextBox _commentBox = new() { Margin = new Thickness(8, 4, 8, 4), AcceptsReturn = true, Height = 52 };
    private readonly TextBlock _summary = new() { Margin = new Thickness(8), Foreground = System.Windows.Media.Brushes.Gray };

    private readonly IReadOnlyList<string> _inputs;

    public CompressDialog(IReadOnlyList<string> inputs)
    {
        _inputs = inputs;

        Title = "您将创建一个压缩文件";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        FontFamily = Ui.Theme.UiFont;
        FontSize = 12;
        Content = BuildLayout();

        _formatBox.ItemsSource = FormatRegistry.CreatableFormats.Select(f => f.DisplayName).ToList();
        _formatBox.SelectedIndex = 0;
        _formatBox.SelectionChanged += (_, _) => OnFormatChanged();

        _levelBox.ItemsSource = new[] { "快速", "标准", "体积最小" };
        _levelBox.SelectedIndex = 1;

        _volumeBox.ItemsSource = new[] { "不分卷", "1 MB", "10 MB", "100 MB", "500 MB", "1000 MB", "2000 MB", "4000 MB" };
        _volumeBox.SelectedIndex = 0;

        _nameBox.Text = DefaultName(inputs);
        var baseDir = inputs.Count > 0 ? Path.GetDirectoryName(Path.GetFullPath(inputs[0])) : string.Empty;
        _pathBox.Text = baseDir ?? string.Empty;
        OnFormatChanged();
    }

    private UIElement BuildLayout()
    {
        var root = new StackPanel { Margin = new Thickness(16) };

        root.Children.Add(new TextBlock
        {
            Text = string.Format("共 {0} 个项目等待压缩", _inputs.Count),
            Margin = new Thickness(0, 0, 0, 10),
        });

        root.Children.Add(Row("文件名", _nameBox));
        root.Children.Add(Row("保存到", BuildPathRow()));
        root.Children.Add(Row("压缩格式", _formatBox));
        root.Children.Add(Row("压缩等级", _levelBox));
        root.Children.Add(Row("压缩分卷大小", _volumeBox));
        root.Children.Add(_solidBox);
        root.Children.Add(_sfxBox);
        root.Children.Add(Row("加密码", _passwordBox));
        root.Children.Add(Row("确认密码", _password2Box));
        root.Children.Add(Row("添加注释", _commentBox));
        root.Children.Add(_summary);
        root.Children.Add(BuildButtons());

        return root;
    }

    private UIElement BuildPathRow()
    {
        var browse = new Button { Content = "浏览…", Width = 76, Margin = new Thickness(0, 4, 8, 4) };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog { Title = "选择保存位置", InitialDirectory = _pathBox.Text };
            if (dlg.ShowDialog(this) == true) _pathBox.Text = dlg.FolderName;
        };
        var panel = new DockPanel();
        panel.Children.Add(browse);
        DockPanel.SetDock(browse, Dock.Right);
        panel.Children.Add(_pathBox);
        return panel;
    }

    private UIElement BuildButtons()
    {
        var ok = new Button { Content = "立即压缩", Width = 96, Height = 30, IsDefault = true, Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 76, Height = 30, IsCancel = true, Margin = new Thickness(0, 12, 0, 0) };
        ok.Click += (_, _) => OnConfirm();
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }

    private static UIElement Row(string label, UIElement editor)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var text = new TextBlock { Text = label, Width = 84, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(text, Dock.Left);
        panel.Children.Add(text);
        panel.Children.Add(editor);
        return panel;
    }

    private void OnFormatChanged()
    {
        var isSevenZip = _formatBox.SelectedIndex == 1;
        _solidBox.IsEnabled = isSevenZip;
        if (!isSevenZip) _solidBox.IsChecked = false;
        _summary.Text = isSevenZip
            ? "7Z 支持固实压缩、AES-256 加密、分卷与自解压；选中 7Z 时默认建议「体积最小」。"
            : "ZIP 通用性最好，任何系统都能打开；不支持固实压缩（ZIP 永远非固实）。";
    }

    private static string DefaultName(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 0) return "新建压缩包";
        if (inputs.Count == 1)
        {
            var p = inputs[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var n = Path.GetFileName(p);
            return string.IsNullOrEmpty(n) ? "新建压缩包" : n;
        }
        var first = Path.GetDirectoryName(Path.GetFullPath(inputs[0]));
        var sameDir = inputs.All(i =>
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(i)), first, StringComparison.OrdinalIgnoreCase));
        return sameDir && !string.IsNullOrEmpty(first) ? Path.GetFileName(first) : "新建压缩包";
    }

    private void OnConfirm()
    {
        if (_inputs.Count == 0)
        {
            MessageBox.Show("没有可压缩的文件。", "zzzip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var format = FormatRegistry.CreatableFormats[Math.Max(0, _formatBox.SelectedIndex)];

        if (_passwordBox.Password.Length > 0 && _passwordBox.Password != _password2Box.Password)
        {
            MessageBox.Show("两次输入的密码不一致。", "zzzip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var volumeMb = _volumeBox.SelectedIndex switch
        {
            1 => 1, 2 => 10, 3 => 100, 4 => 500, 5 => 1000, 6 => 2000, 7 => 4000,
            _ => (int?)null,
        };

        var options = new ArchiveOptions
        {
            Format = format,
            Level = _levelBox.SelectedIndex switch
            {
                0 => CompressionLevelOption.Fast,
                2 => CompressionLevelOption.Smallest,
                _ => CompressionLevelOption.Normal,
            },
            Solid = _solidBox.IsChecked == true,
            VolumeSizeMb = volumeMb,
            Password = string.IsNullOrEmpty(_passwordBox.Password) ? null : _passwordBox.Password,
            Comment = string.IsNullOrWhiteSpace(_commentBox.Text) ? null : _commentBox.Text,
            CreateSfx = _sfxBox.IsChecked == true,
        };

        var dir = string.IsNullOrWhiteSpace(_pathBox.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : _pathBox.Text;
        var name = string.IsNullOrWhiteSpace(_nameBox.Text) ? "新建压缩包" : _nameBox.Text.Trim();
        var output = Path.Combine(dir, name + format.Extension);

        try
        {
            ArchiveEngineRouter.Create(_inputs, output, options, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "zzzip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show("压缩完成：" + output, "zzzip", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }
}