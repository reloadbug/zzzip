using System.IO;
using System.Windows;
using System.Windows.Controls;
using zzzip.Core;

namespace zzzip.App.Views;

/// <summary>【添加注释】对话框：给已有压缩包写入注释（ZIP 写入 EOCD，7Z 走内核）。</summary>
public sealed class CommentDialog : Window
{
    private readonly TextBox _comment = new() { Margin = new Thickness(8, 4, 8, 4), Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly string _archivePath;

    public CommentDialog(string archivePath)
    {
        _archivePath = archivePath;

        Title = "添加注释";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;
        FontFamily = Ui.Theme.UiFont;
        FontSize = 12;
        Content = BuildLayout();
    }

    private UIElement BuildLayout()
    {
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "为 " + Path.GetFileName(_archivePath) + " 添加注释",
            Margin = new Thickness(0, 0, 0, 8),
        });
        root.Children.Add(_comment);

        var ok = new Button { Content = "确定", Width = 88, Height = 30, IsDefault = true, Margin = new Thickness(0, 12, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 76, Height = 30, IsCancel = true, Margin = new Thickness(0, 12, 0, 0) };
        ok.Click += (_, _) => Apply();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        return root;
    }

    private void Apply()
    {
        if (string.IsNullOrWhiteSpace(_comment.Text))
        {
            MessageBox.Show("注释内容不能为空。", "zzzip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            ArchiveComment.Write(_archivePath, _comment.Text);
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