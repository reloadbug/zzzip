using System.Windows;

namespace zzzip.App.Ui;

/// <summary>
/// WPF 的 <see cref="Window.ShowDialog()"/> 本身不接受 owner 参数，
/// 这里补一个带 owner 的重载，保证对话框在主窗口中央且不脱离父窗口。
/// </summary>
public static class WindowExtensions
{
    public static bool? ShowDialogOwned(this Window child, Window? owner)
    {
        if (owner is not null && !ReferenceEquals(owner, child))
        {
            child.Owner = owner;
            child.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return child.ShowDialog();
    }
}