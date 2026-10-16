using System.Diagnostics;
using System.IO;
using zzzip.App.Views;
using zzzip.Core;
using zzzip.ShellReg;

namespace zzzip.App.Services;

/// <summary>把资源管理器右键菜单的 verb 翻译成核心层动作。每一项对应 360 的同名菜单项。</summary>
public static class ShellVerbRunner
{
    public static void Run(CommandLine cmd)
    {
        var targets = cmd.Targets;
        var files = targets.Where(File.Exists).ToList();
        var archives = targets.Where(FormatRegistry.IsArchive).ToList();

        // --silent 是给自动化与计划任务用的，承诺「绝不弹窗阻塞」。
        // 同名文件询问是新增的阻塞点，所以静默模式下干脆不给询问通道 ——
        // 否则会挂一个对话框在那里，等一个永远不会来的点击。
        // 每个压缩包单独一份询问状态：前一个包选的「全部」不顺延到下一个包。
        IConflictResolver? Prompts() => cmd.Silent ? null : new WpfConflictResolver();

        switch (cmd.Verb)
        {
            case ContextMenuRegistrar.Verbs.AddToArchive:
                // 打开【您将创建一个压缩文件】设置对话框，与 360 行为一致
                new CompressDialog(files).ShowDialog();
                break;

            case ContextMenuRegistrar.Verbs.QuickZip:
                ArchiveEngineRouter.CreateQuickZip(targets, cmd.PresetName, null, CancellationToken.None);
                break;

            case ContextMenuRegistrar.Verbs.HighRatio7z:
                ArchiveEngineRouter.CreateHighRatio7z(targets, cmd.PresetName, null, CancellationToken.None);
                break;

            case ContextMenuRegistrar.Verbs.EachSeparately:
                ArchiveEngineRouter.CreateEachSeparately(
                    files, p => Path.GetDirectoryName(p) ?? string.Empty, null, CancellationToken.None);
                break;

            case ContextMenuRegistrar.Verbs.ZipAndMail:
                ArchiveEngineRouter.CreateQuickZip(targets, cmd.PresetName, null, CancellationToken.None);
                Process.Start(new ProcessStartInfo("mailto:") { UseShellExecute = true });
                break;

            // 右键菜单触发的解压同样要问同名文件：以前这里是静默跳过的，
            // 用户既不知道跳过了什么，也没机会选覆盖。
            case ContextMenuRegistrar.Verbs.ExtractToOwn:
                foreach (var a in archives)
                    ArchiveEngineRouter.ExtractToOwnFolder(a, null, CancellationToken.None, Prompts());
                break;

            case ContextMenuRegistrar.Verbs.ExtractHere:
                foreach (var a in archives)
                    ArchiveEngineRouter.ExtractToHere(a, null, CancellationToken.None, Prompts());
                break;

            case ContextMenuRegistrar.Verbs.ExtractTo:
                new ExtractDialog(archives, cmd.ExtractTo).ShowDialog();
                break;

            default:
                // 静默模式同样不弹窗（与上面同一份「绝不阻塞」的承诺）
                if (cmd.Silent) Console.Error.WriteLine("zzzip: 无法识别的右键菜单命令。");
                else System.Windows.MessageBox.Show("无法识别的右键菜单命令。", "zzzip");
                break;
        }
    }
}