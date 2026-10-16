using System.Threading;
using System.Windows;
using zzzip.App.Views;
using zzzip.Core;

namespace zzzip.App.Services;

/// <summary>
/// 桌面端的同名文件询问通道。
///
/// 第一次遇到冲突时先把「一共有几处」摆出来，让用户选是一次性决定还是逐个对比；
/// 选「逐个确认」之后，每处冲突弹一个对比框，直到用户点了某个「全部xx」为止。
///
/// 解压在后台线程跑，而弹窗必须在 UI 线程 —— 所以这里统一 Dispatch 过去。
/// 解压线程会在窗口关闭前一直挂着，这正是我们要的「问完再解」。
/// </summary>
public sealed class WpfConflictResolver : IConflictResolver
{
    private readonly Func<Window?>? _ownerProvider;
    private bool _batchAsked;

    /// <param name="ownerProvider">
    /// 返回对话框的宿主窗口（一般传 () =&gt; this）。取不到或不可见时会退回主窗口。
    /// </param>
    public WpfConflictResolver(Func<Window?>? ownerProvider = null) => _ownerProvider = ownerProvider;

    public ConflictDecision Resolve(FileConflict conflict)
    {
        var app = Application.Current;

        // 主窗口模式：解压跑在后台线程，必须 Dispatch 回 UI 线程再弹
        if (app is not null) return app.Dispatcher.Invoke(() => Ask(conflict));

        // 右键菜单模式（--verb=...）：进程里根本没有 Application 实例，
        // 但入口是 [STAThread] 主线程，而且这个调用就发生在它上面 ——
        // ShowDialog 会自己撑起一个消息循环，直接弹即可。
        //
        // 兜底：万一真的落在非 STA 线程上（弹不了窗口），按最安全的方式跳过 ——
        // 绝不因为拿不到界面就悄悄覆盖掉用户的文件。
        return Thread.CurrentThread.GetApartmentState() == ApartmentState.STA
            ? Ask(conflict)
            : ConflictDecision.Skip;
    }

    private ConflictDecision Ask(FileConflict conflict)
    {
        // 第一次遇到冲突：先问「一共几处，要不要逐个看」
        if (!_batchAsked)
        {
            _batchAsked = true;

            if (conflict.Total > 1)
            {
                var batch = new ConflictBatchDialog(conflict.Total);
                Attach(batch);
                batch.ShowDialog();

                // 选了「全部覆盖 / 全部跳过 / 取消」就直接返回；
                // 「全部」的黏性由 ConflictGate 负责，这里不用自己记
                if (!batch.ReviewIndividually) return batch.Choice;
            }
        }

        var dialog = new FileConflictDialog(conflict);
        Attach(dialog);
        dialog.ShowDialog();
        return dialog.Decision;
    }

    private void Attach(Window window)
    {
        var owner = _ownerProvider?.Invoke() ?? Application.Current?.MainWindow;
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, window))
            window.Owner = owner;
    }
}
