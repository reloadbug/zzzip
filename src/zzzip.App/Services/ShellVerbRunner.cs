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

            case ContextMenuRegistrar.Verbs.ExtractToOwn:
                foreach (var a in archives)
                    ArchiveEngineRouter.ExtractToOwnFolder(a, null, CancellationToken.None);
                break;

            case ContextMenuRegistrar.Verbs.ExtractHere:
                foreach (var a in archives)
                    ArchiveEngineRouter.ExtractToHere(a, null, CancellationToken.None);
                break;

            case ContextMenuRegistrar.Verbs.ExtractTo:
                new ExtractDialog(archives, cmd.ExtractTo).ShowDialog();
                break;

            default:
                System.Windows.MessageBox.Show("无法识别的右键菜单命令。", "zzzip");
                break;
        }
    }
}