using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using zzzip.App.Services;
using zzzip.App.Ui;
using zzzip.App.Views;
using zzzip.Core;

namespace zzzip.App;

/// <summary>列表行视图模型：把 ArchiveEntry 转成可直接绑定的字符串列。</summary>
public sealed class EntryRow
{
    public required ArchiveEntry Entry { get; init; }

    public string Path => Entry.IsDirectory
        ? Entry.Path
        : System.IO.Path.GetFileName(Entry.Path);

    public string FullPath => Entry.Path;
    public bool IsDirectory => Entry.IsDirectory;
    public string SizeText => Entry.IsDirectory ? "-" : Theme.FormatSize(Entry.Size);
    public string PackedSizeText => Entry.IsDirectory ? "-" : Theme.FormatSize(Entry.PackedSize);
    public string RatioText => Entry.IsDirectory ? "-" : Theme.FormatRatio(Entry.Ratio);
    public string ModifiedText => Entry.IsEncrypted ? Theme.FormatDate(Entry.ModifiedUtc) + "  🔒" : Theme.FormatDate(Entry.ModifiedUtc);
}

public partial class MainWindow : Window
{
    private string? _currentArchive;
    private readonly List<string> _pendingInputs = [];
    private readonly CancellationTokenSource _cts = new();

    public MainWindow()
    {
        InitializeComponent();
        StatusRight.Text = ArchiveEngineRouter.EngineSummary;
        StatusLeft.Text = "就绪";
    }

    // ---------------------------------------------------------- 窗口控制

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------- 打开 / 浏览

    /// <summary>命令行传入的路径：压缩包直接浏览，普通文件/文件夹作为待压缩项。</summary>
    public void LoadTargets(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        var archives = list.Where(FormatRegistry.IsArchive).ToList();
        if (archives.Count > 0) { OpenArchive(archives[0]); return; }
        _pendingInputs.AddRange(list);
        RefreshPendingHint();
    }

    public void OpenWithDialog() => OnOpenArchive(this, new RoutedEventArgs());

    private void OnOpenArchive(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "打开压缩文件",
            Filter = "所有压缩包|*.zip;*.7z;*.rar;*.tar;*.gz;*.bz2;*.xz;*.zst;*.iso;*.cab;*.arj;*.lzh;*.wim;*.jar|" +
                     "ZIP (*.zip)|*.zip|7Z (*.7z)|*.7z|RAR (*.rar)|*.rar|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true) OpenArchive(dlg.FileName);
    }

    public void OpenArchive(string path)
    {
        try
        {
            var entries = ArchiveEngineRouter.List(path);
            _currentArchive = path;
            TitleText.Text = System.IO.Path.GetFileName(path) + " - zzzip";
            PathHint.Text = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            EntryList.ItemsSource = entries.Select(e => new EntryRow { Entry = e }).ToList();
            EmptyHint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshStatus();
        }
        catch (Exception ex)
        {
            _currentArchive = null;
            EntryList.ItemsSource = null;
            EmptyHint.Visibility = Visibility.Visible;
            MessageBox.Show(ex.Message, "zzzip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_currentArchive is not null) OpenArchive(_currentArchive);
    }

    private IReadOnlyList<EntryRow> SelectedRows => EntryList.SelectedItems.Cast<EntryRow>().ToList();

    private string? CurrentArchive => _currentArchive;

    private void RefreshStatus()
    {
        var rows = (EntryList.ItemsSource as IEnumerable<EntryRow>)?.ToList() ?? [];
        if (rows.Count == 0)
        {
            StatusLeft.Text = "就绪";
            return;
        }
        var raw = rows.Where(r => !r.IsDirectory).Sum(r => r.Entry.Size);
        var packed = rows.Sum(r => r.Entry.PackedSize);
        StatusLeft.Text = string.Format(
            "{0} 个项目 | 原始大小 {1} | 压缩后 {2}",
            rows.Count, Theme.FormatSize(raw), Theme.FormatSize(packed));
        StatusRight.Text = ArchiveEngineRouter.EngineSummary;
    }

    // ---------------------------------------------------------- 添加

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "选择要压缩的文件", Multiselect = true, CheckFileExists = true };
        if (dlg.ShowDialog(this) != true) return;
        _pendingInputs.AddRange(dlg.FileNames);
        RefreshPendingHint();
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择要压缩的文件夹", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        _pendingInputs.AddRange(dlg.FolderNames);
        RefreshPendingHint();
    }

    private void RefreshPendingHint()
    {
        if (_pendingInputs.Count == 0)
        {
            PathHint.Text = _currentArchive is null ? "未打开压缩包" : PathHint.Text;
            StatusLeft.Text = "就绪";
            return;
        }
        var names = string.Join("、", _pendingInputs.Select(p => System.IO.Path.GetFileName(p)).Take(3));
        PathHint.Text = _pendingInputs.Count > 3
            ? names + " 等 " + _pendingInputs.Count + " 项"
            : names;
        StatusLeft.Text = "待压缩：" + _pendingInputs.Count + " 个项目（点「文件 → 新建压缩文件」开始）";
    }

    private void OnNewArchive(object sender, RoutedEventArgs e)
    {
        if (new CompressDialog(_pendingInputs).ShowDialogOwned(this) == true)
        {
            _pendingInputs.Clear();
            RefreshPendingHint();
        }
    }

    // ---------------------------------------------------------- 解压 / 测试

    private void OnExtractTo(object sender, RoutedEventArgs e)
    {
        if (CurrentArchive is null) { MessageBox.Show("请先打开一个压缩包。", "zzzip"); return; }
        var picked = SelectedRows.Where(r => !r.IsDirectory).Select(r => r.FullPath).ToList();
        new ExtractDialog([CurrentArchive], null, picked).ShowDialogOwned(this);
        if (_currentArchive is not null) OpenArchive(_currentArchive);
    }

    private void OnOneClickExtract(object sender, RoutedEventArgs e)
    {
        if (CurrentArchive is null) { MessageBox.Show("请先打开一个压缩包。", "zzzip"); return; }
        var archive = CurrentArchive;
        RunAsync("一键解压中...", () => ArchiveEngineRouter.ExtractToOwnFolder(
            archive, MakeProgress(), _cts.Token, new WpfConflictResolver(() => this)));
    }

    private void OnTestArchive(object sender, RoutedEventArgs e)
    {
        if (CurrentArchive is null) { MessageBox.Show("请先打开一个压缩包。", "zzzip"); return; }
        var archive = CurrentArchive;
        RunAsync("正在测试压缩包...", () =>
        {
            var ok = ArchiveEngineRouter.Test(archive, null, MakeProgress(), _cts.Token);
            MessageBox.Show(ok ? "测试通过，压缩包完整。" : "测试失败，压缩包可能已损坏。", "zzzip",
                MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        });
    }

    private void OnAddComment(object sender, RoutedEventArgs e)
    {
        if (CurrentArchive is null) { MessageBox.Show("请先打开一个压缩包。", "zzzip"); return; }
        new CommentDialog(CurrentArchive).ShowDialogOwned(this);
    }

    private void OnDeleteEntry(object sender, RoutedEventArgs e)
    {
        if (CurrentArchive is null || SelectedRows.Count == 0) return;
        MessageBox.Show("从压缩包中删除条目将在下一阶段接入 7-Zip 内核更新接口后开放。", "zzzip");
    }

    private void OnEntryOpen(object sender, RoutedEventArgs e)
    {
        var row = SelectedRows.FirstOrDefault();
        if (row is null || row.IsDirectory) return;
        MessageBox.Show("双击直接打开压缩包内的 " + System.IO.Path.GetFileName(row.FullPath) +
                        " 需要先释放到临时目录，将在下一阶段接入。", "zzzip");
    }

    private void OnEntryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedRows.FirstOrDefault() is { IsDirectory: true })
            MessageBox.Show("压缩包内目录导航将在下一阶段接入。", "zzzip");
        else
            OnEntryOpen(sender, e);
    }

    // ---------------------------------------------------------- 编辑 / 查看

    private void OnSelectAll(object sender, RoutedEventArgs e) => EntryList.SelectAll();
    private void OnUnselectAll(object sender, RoutedEventArgs e) => EntryList.SelectedItems.Clear();
    private void OnViewLarge(object sender, RoutedEventArgs e) => MessageBox.Show("大图标视图将在打磨阶段提供。", "zzzip");
    private void OnViewDetails(object sender, RoutedEventArgs e) { }

    // ---------------------------------------------------------- 拖放

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;
        var archives = dropped.Where(FormatRegistry.IsArchive).ToList();
        if (archives.Count > 0) { OpenArchive(archives[0]); return; }
        _pendingInputs.AddRange(dropped);
        RefreshPendingHint();
        if (new CompressDialog(_pendingInputs).ShowDialogOwned(this) == true)
        {
            _pendingInputs.Clear();
            RefreshPendingHint();
        }
    }

    // ---------------------------------------------------------- 工具

    private void OnOptions(object sender, RoutedEventArgs e) => new OptionsDialog().ShowDialogOwned(this);

    private void OnAbout(object sender, RoutedEventArgs e) => MessageBox.Show(
        "zzzip 1.0.0\n极简 · 绿色 · 无广告的 Windows 压缩工具\n\n" +
        "ZIP 由内置引擎处理，其余格式由 7-Zip 引擎驱动。",
        "关于 zzzip", MessageBoxButton.OK, MessageBoxImage.Information);

    // ---------------------------------------------------------- 后台任务封装

    private IProgress<ArchiveProgress> MakeProgress() => new Progress<ArchiveProgress>(p =>
    {
        var pct = (int)Math.Round(p.FileFraction * 100);
        StatusProgress.Text = pct > 0 ? string.Format("{0}%  {1}", pct, p.CurrentFile) : StatusProgress.Text;
    });

    /// <summary>后台线程跑压缩/解压，进度与异常都回到 UI 线程呈现。</summary>
    private void RunAsync(string statusText, Action action)
    {
        StatusProgress.Text = statusText;
        EntryList.IsEnabled = false;

        Task.Run(() =>
        {
            try { action(); }
            catch (Exception ex) when (ex is not ExtractCancelledException)
            {
                // 用户在冲突对话框里点了「取消」不算失败，静默收场即可
                Dispatcher.Invoke(() =>
                    MessageBox.Show(ex.Message, "zzzip", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    StatusProgress.Text = string.Empty;
                    EntryList.IsEnabled = true;
                    if (_currentArchive is not null) OpenArchive(_currentArchive);
                });
            }
        });
    }
}