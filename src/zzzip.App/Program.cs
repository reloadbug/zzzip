using System.Windows;
using zzzip.App.Services;
using zzzip.Core;
using zzzip.ShellReg;

namespace zzzip.App;

/// <summary>
/// 命令行入口。三种启动形态：
///  1. --register / --unregister：只改注册表，不进 UI；
///  2. --verb=xxx：资源管理器右键菜单调用，执行完即退出，不弹主界面；
///  3. 无参数或带路径：启动主窗口（带压缩包路径则直接浏览）。
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var cmd = CommandLine.Parse(args);
        ArchiveEngineRouter.EnsureConfigured();

        if (cmd.IsUnregisterOnly)
        {
            ContextMenuRegistrar.Unregister();
            return 0;
        }

        if (cmd.IsRegisterOnly)
        {
            new ContextMenuRegistrar(Environment.ProcessPath ?? "zzzip.exe").Register(cmd.CascadeMenu);
            return 0;
        }

        // --associate / --dissociate：只改文件关联，不进 UI（供无人值守与自动测试使用）
        if (cmd.AssociateExtensions.Count > 0 || cmd.DissociateExtensions.Count > 0)
        {
            if (cmd.DissociateExtensions.Count > 0)
                ContextMenuRegistrar.Dissociate(cmd.DissociateExtensions);

            if (cmd.AssociateExtensions.Count > 0)
                ContextMenuRegistrar.Associate(cmd.AssociateExtensions, Environment.ProcessPath ?? "zzzip.exe");

            return 0;
        }

        if (cmd.Verb is not null)
        {
            try
            {
                ShellVerbRunner.Run(cmd);
                return 0;
            }
            catch (ExtractCancelledException)
            {
                // 用户在「同名文件」提示里点了取消：这是主动中止，不是失败，安静退出即可
                return 0;
            }
            catch (Exception ex)
            {
                // 静默模式（自动化/计划任务）下绝不弹窗阻塞，只写 stderr 并返回非零退出码
                if (cmd.Silent)
                    Console.Error.WriteLine("zzzip: " + ex.Message);
                else
                    MessageBox.Show(ex.Message, "zzzip", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 1;
            }
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var win = new MainWindow();

        if (cmd.Targets.Count > 0)
            win.LoadTargets(cmd.Targets);
        else if (cmd.PickArchiveOnStart)
            win.OpenWithDialog();

        return app.Run(win);
    }
}