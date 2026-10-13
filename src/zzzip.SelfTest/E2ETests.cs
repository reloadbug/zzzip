using System.Diagnostics;
using System.Text;
using zzzip.Core;

namespace zzzip.SelfTest;

/// <summary>
/// 端到端测试：真实拉起编译好的 zzzip.exe，验证右键菜单的完整调用链路
/// （命令行契约 → 引擎 → 产出文件），而不是只在进程内调用库。
/// </summary>
public static class E2ETests
{
    public static void Register(TestHarness t, string workDir, string? exePath)
    {
        if (exePath is null || !File.Exists(exePath))
        {
            t.Run("端到端：zzzip.exe 右键菜单调用链", () =>
                TestHarness.Skip("未提供 zzzip.exe 路径（用 --exe= 指定），跳过端到端测试"));
            return;
        }

        var exe = Path.GetFullPath(exePath);

        t.Run("端到端：--unregister 静默执行且退出码为 0", () =>
        {
            var (code, _, err) = Run(exe, ["--unregister"], workDir);
            TestHarness.Equal(0, code, "退出码应为 0，stderr：" + err);
        });

        t.Run("端到端：--verb=quickzip 产出 zip 且内容正确", () =>
        {
            var dir = Path.Combine(workDir, "e2e-quickzip");
            Directory.CreateDirectory(dir);
            var f1 = Path.Combine(dir, "one.txt");
            var f2 = Path.Combine(dir, "two.txt");
            File.WriteAllText(f1, "端到端一号", Encoding.UTF8);
            File.WriteAllText(f2, "端到端二号", Encoding.UTF8);

            var (code, _, err) = Run(exe, ["--verb=quickzip", f1, f2], dir);
            TestHarness.Equal(0, code, "退出码应为 0，stderr：" + err);

            var produced = Directory.GetFiles(dir, "*.zip");
            TestHarness.True(produced.Length >= 1, "应产出至少一个 zip 文件");

            var entries = ArchiveEngineRouter.List(produced[0]);
            TestHarness.Equal(2, entries.Count, "zip 内应含 2 个文件");
        });

        t.Run("端到端：--verb=7z 产出高压缩比 7z 包", () =>
        {
            var dir = Path.Combine(workDir, "e2e-7z");
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "payload.txt");
            File.WriteAllText(f, string.Concat(Enumerable.Repeat("高压缩比测试 zzzip ", 500)), Encoding.UTF8);

            var (code, _, err) = Run(exe, ["--verb=7z", f], dir);
            TestHarness.Equal(0, code, "退出码应为 0，stderr：" + err);

            var produced = Directory.GetFiles(dir, "*.7z");
            TestHarness.True(produced.Length >= 1, "应产出 7z 文件");
            TestHarness.True(new FileInfo(produced[0]).Length > 0, "7z 文件不应为空");
        });

        t.Run("端到端：--verb=own 解压到同名文件夹", () =>
        {
            var dir = Path.Combine(workDir, "e2e-extract");
            Directory.CreateDirectory(dir);
            var src = Path.Combine(dir, "hello.txt");
            File.WriteAllText(src, "解压目标内容", Encoding.UTF8);

            // 先造一个包
            var zip = Path.Combine(dir, "pack.zip");
            ArchiveEngineRouter.Create([src], zip, new ArchiveOptions { Format = FormatRegistry.Zip },
                null, CancellationToken.None);
            File.Delete(src);

            var (code, _, err) = Run(exe, ["--verb=own", zip], dir);
            TestHarness.Equal(0, code, "退出码应为 0，stderr：" + err);

            var expected = Path.Combine(dir, "pack", "hello.txt");
            TestHarness.True(File.Exists(expected), "应解压到 pack\\hello.txt，实际目录：" +
                string.Join(",", Directory.GetFileSystemEntries(dir).Select(Path.GetFileName)));
            TestHarness.Equal("解压目标内容", File.ReadAllText(expected));
        });

        t.Run("端到端：--verb=here 解压到当前文件夹", () =>
        {
            var dir = Path.Combine(workDir, "e2e-here", "sub");
            Directory.CreateDirectory(dir);
            var src = Path.Combine(dir, "here.txt");
            File.WriteAllText(src, "here content", Encoding.UTF8);
            var zip = Path.Combine(dir, "h.zip");
            ArchiveEngineRouter.Create([src], zip, new ArchiveOptions { Format = FormatRegistry.Zip },
                null, CancellationToken.None);
            File.Delete(src);

            var (code, _, err) = Run(exe, ["--verb=here", zip], dir);
            TestHarness.Equal(0, code, "退出码应为 0，stderr：" + err);
            TestHarness.True(File.Exists(Path.Combine(dir, "here.txt")), "应解压到当前文件夹");
        });

        t.Run("端到端：--verb=each 每个文件压成单独压缩包", () =>
        {
            var dir = Path.Combine(workDir, "e2e-each");
            Directory.CreateDirectory(dir);
            var a = Path.Combine(dir, "a.txt");
            var b = Path.Combine(dir, "b.txt");
            File.WriteAllText(a, "aa");
            File.WriteAllText(b, "bb");

            var (code, _, err) = Run(exe, ["--verb=each", a, b], dir);
            TestHarness.Equal(0, code, "退出码应为 0，stderr：" + err);

            TestHarness.True(File.Exists(Path.Combine(dir, "a.txt.zip")), "应产出 a.txt.zip");
            TestHarness.True(File.Exists(Path.Combine(dir, "b.txt.zip")), "应产出 b.txt.zip");
        });

        t.Run("端到端：--associate / --dissociate 往返（用测试专用扩展名，不动真实关联）", () =>
        {
            const string ext = "zzziptest";
            using var _ = new AssocRestore(ext);

            var (ca, _, ea) = Run(exe, ["--associate=." + ext], workDir);
            TestHarness.Equal(0, ca, "--associate 退出码应为 0，stderr：" + ea);
            TestHarness.True(zzzip.ShellReg.ContextMenuRegistrar.IsAssociated(ext),
                "关联后应指向 zzzip。注意：1.0 版写到了 HKCU\\.ext（漏了 Software\\Classes），这里会直接暴露出来");

            var (cd, _, ed) = Run(exe, ["--dissociate=." + ext], workDir);
            TestHarness.Equal(0, cd, "--dissociate 退出码应为 0，stderr：" + ed);
            TestHarness.False(zzzip.ShellReg.ContextMenuRegistrar.IsAssociated(ext), "解除后不应再指向 zzzip");
        });

        t.Run("端到端：--register / --unregister 往返不留残留", () =>
        {
            using var _ = new RegistryRestore();

            var (c1, _, e1) = Run(exe, ["--register"], workDir);
            TestHarness.Equal(0, c1, "注册退出码应为 0，stderr：" + e1);
            TestHarness.True(zzzip.ShellReg.ContextMenuRegistrar.IsInstalled, "--register 后应处于已安装状态");

            var (c2, _, e2) = Run(exe, ["--unregister"], workDir);
            TestHarness.Equal(0, c2, "卸载退出码应为 0，stderr：" + e2);
            TestHarness.False(zzzip.ShellReg.ContextMenuRegistrar.IsInstalled, "--unregister 后应无残留");
        });
    }

    /// <summary>测试结束后确保注册表回到未安装状态，避免污染用户机器。</summary>
    private sealed class RegistryRestore : IDisposable
    {
        public void Dispose()
        {
            try { zzzip.ShellReg.ContextMenuRegistrar.Unregister(); } catch { }
        }
    }

    /// <summary>测试结束后解除测试用扩展名的关联，绝不动用户真实的 .zip / .7z。</summary>
    private sealed class AssocRestore(string ext) : IDisposable
    {
        public void Dispose()
        {
            try { zzzip.ShellReg.ContextMenuRegistrar.Dissociate(["." + ext]); } catch { }
        }
    }

    private static (int Code, string StdOut, string StdErr) Run(string exe, string[] args, string cwd)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // 一律静默：GUI 弹窗在无人值守环境会永久阻塞。
        psi.ArgumentList.Add("--silent");
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(120_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new AssertionException($"进程超时未退出：{exe} {string.Join(' ', args)}");
        }
        return (p.ExitCode, stdout, stderr);
    }
}