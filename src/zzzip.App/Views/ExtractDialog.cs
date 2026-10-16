using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using zzzip.App.Services;
using zzzip.Core;

namespace zzzip.App.Views;

/// <summary>【解压文件】对话框：目标目录、是否解压全部/仅选中项、覆盖策略、密码。</summary>
public sealed class ExtractDialog : Window
{
    private readonly TextBox _pathBox = new() { Margin = new Thickness(8, 4, 4, 4) };
    // 同名文件的处理策略：默认逐个询问，避免以前那种「勾了就静默覆盖」的默认值把用户文件闷掉
    private readonly RadioButton _askOnConflict = new() { Content = "逐个询问我", Margin = new Thickness(8, 4, 14, 4), IsChecked = true };
    private readonly RadioButton _overwriteAll = new() { Content = "全部覆盖", Margin = new Thickness(0, 4, 14, 4) };
    private readonly RadioButton _skipAll = new() { Content = "全部跳过", Margin = new Thickness(0, 4, 8, 4) };
    private readonly CheckBox _keepArchive = new() { Content = "解压后保留压缩包", Margin = new Thickness(8, 4, 8, 4), IsChecked = true };
    private readonly PasswordBox _passwordBox = new() { Margin = new Thickness(8, 4, 8, 4) };
    private readonly TextBlock _summary = new() { Margin = new Thickness(8), Foreground = System.Windows.Media.Brushes.Gray };

    private readonly IReadOnlyList<string> _archives;
    private readonly IReadOnlyList<string> _selectedEntries;
    private readonly ProgressBar _progress = new() { Height = 6, Margin = new Thickness(8, 8, 8, 0), IsIndeterminate = false };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 4, 8, 0), Foreground = System.Windows.Media.Brushes.Gray };

    public ExtractDialog(IReadOnlyList<string> archives, string? presetDir = null, IReadOnlyList<string>? selectedEntries = null)
    {
        _archives = archives;
        _selectedEntries = selectedEntries ?? [];

        Title = "解压文件";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        FontFamily = Ui.Theme.UiFont;
        FontSize = 12;
        Content = BuildLayout(presetDir);
        UpdateSummary();
    }

    private UIElement BuildLayout(string? presetDir)
    {
        var root = new StackPanel { Margin = new Thickness(16) };

        _pathBox.Text = presetDir ?? DefaultDir();

        root.Children.Add(new TextBlock
        {
            Text = string.Format("待解压压缩包：{0} 个", _archives.Count),
            Margin = new Thickness(0, 0, 0, 10),
        });
        root.Children.Add(Row("解压到", BuildPathRow()));
        root.Children.Add(Row("密码", _passwordBox));
        root.Children.Add(Row("同名文件", BuildConflictRow()));
        root.Children.Add(_keepArchive);
        root.Children.Add(_summary);
        root.Children.Add(_progress);
        root.Children.Add(_status);
        root.Children.Add(BuildButtons());

        return root;
    }

    private string DefaultDir()
    {
        if (_archives.Count == 0) return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var first = _archives[0];
        var parent = Path.GetDirectoryName(Path.GetFullPath(first)) ?? string.Empty;
        return Path.Combine(parent, Path.GetFileNameWithoutExtension(first));
    }

    private UIElement BuildPathRow()
    {
        var browse = new Button { Content = "浏览…", Width = 76, Margin = new Thickness(0, 4, 8, 4) };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog { Title = "选择解压位置", InitialDirectory = _pathBox.Text };
            if (dlg.ShowDialog(this) == true) _pathBox.Text = dlg.FolderName;
        };
        var panel = new DockPanel();
        panel.Children.Add(browse);
        DockPanel.SetDock(browse, Dock.Right);
        panel.Children.Add(_pathBox);
        return panel;
    }

    private UIElement BuildConflictRow()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(_askOnConflict);
        panel.Children.Add(_overwriteAll);
        panel.Children.Add(_skipAll);
        return panel;
    }

    private UIElement BuildButtons()
    {
        var ok = new Button { Content = "确定", Width = 88, Height = 30, IsDefault = true, Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 76, Height = 30, IsCancel = true, Margin = new Thickness(0, 12, 0, 0) };
        ok.Click += (_, _) => RunExtract(ok, cancel);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(ok);
        panel.Children.Add(cancel);
        return panel;
    }

    private static UIElement Row(string label, UIElement editor)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var text = new TextBlock { Text = label, Width = 72, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(text, Dock.Left);
        panel.Children.Add(text);
        panel.Children.Add(editor);
        return panel;
    }

    private void UpdateSummary()
    {
        _summary.Text = _selectedEntries.Count > 0
            ? string.Format("将只解压选中的 {0} 个条目", _selectedEntries.Count)
            : "将解压压缩包内的全部内容";
    }

    private void RunExtract(Button ok, Button cancel)
    {
        if (_archives.Count == 0)
        {
            MessageBox.Show("没有可解压的压缩包。", "zzzip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dest = string.IsNullOrWhiteSpace(_pathBox.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : _pathBox.Text;

        // 三种策略：询问 / 全部覆盖 / 全部跳过。
        // 「全部覆盖」与「全部跳过」都只是不给询问通道，由 options 决定静默行为；
        // 「逐个询问」时不设覆盖，让内核先扫冲突再来问用户。
        var askFirst = _askOnConflict.IsChecked == true;
        var overwriteAll = _overwriteAll.IsChecked == true;

        var options = new ArchiveOptions
        {
            OverwriteExisting = overwriteAll,
            KeepArchiveAfterExtract = _keepArchive.IsChecked == true,
            Password = string.IsNullOrEmpty(_passwordBox.Password) ? null : _passwordBox.Password,
        };

        var selected = _selectedEntries;
        var progress = new Progress<ArchiveProgress>(p =>
        {
            var pct = (int)Math.Round(p.FileFraction * 100);
            Dispatcher.Invoke(() =>
            {
                _progress.Value = pct;
                _status.Text = pct > 0 ? string.Format("{0}%  {1}", pct, p.CurrentFile) : string.Empty;
            });
        });

        ok.IsEnabled = false;
        cancel.IsEnabled = false;
        _status.Text = "正在解压...";

        var archives = _archives;
        Task.Run(() =>
        {
            foreach (var a in archives)
            {
                Directory.CreateDirectory(dest);
                var sub = archives.Count > 1
                    ? Path.Combine(dest, Path.GetFileNameWithoutExtension(a))
                    : dest;
                Directory.CreateDirectory(sub);

                var predicate = selected.Count > 0
                    ? new Func<ArchiveEntry, bool>(e => selected.Contains(e.Path))
                    : null;

                // 每个压缩包单独一份询问状态：多包解压时，前一个包选的「全部」不该顺延到下一个包
                var resolver = askFirst ? new WpfConflictResolver(() => this) : null;

                ArchiveEngineRouter.Extract(a, sub, options, predicate, progress, CancellationToken.None, resolver);
            }
        })
        .ContinueWith(t =>
        {
            Dispatcher.Invoke(() =>
            {
                ok.IsEnabled = true;
                cancel.IsEnabled = true;
                if (t.IsFaulted)
                {
                    var failure = t.Exception?.GetBaseException();

                    // 用户在冲突对话框里点了「取消」：这不是错误，不该弹报错框
                    if (failure is ExtractCancelledException)
                    {
                        _status.Text = "已取消";
                        DialogResult = false;
                        Close();
                        return;
                    }

                    MessageBox.Show(failure?.Message ?? "解压失败。", "zzzip",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    _status.Text = "解压失败";
                    return;
                }
                _status.Text = "解压完成";
                MessageBox.Show("解压完成：" + dest, "zzzip", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            });
        });
    }
}