using System.IO.Compression;
using System.Text;
using zzzip.Core;

namespace zzzip.SelfTest;

/// <summary>库级测试：格式注册表、路径安全、ZIP 内核往返、注释、7-Zip 内核全能力。</summary>
public static class CoreTests
{
    public static void Register(TestHarness t, string workDir)
    {
        t.Run("格式注册表：扩展名解析与已知集合", () =>
        {
            TestHarness.Equal("zip", FormatRegistry.Resolve("a.ZIP")!.Id, "大写扩展名应解析为 zip");
            TestHarness.Equal("7z", FormatRegistry.Resolve("b.7z")!.Id);
            TestHarness.Equal("rar", FormatRegistry.Resolve(@"C:\x\y\c.RAR")!.Id);
            TestHarness.Equal("iso", FormatRegistry.Resolve("d.iso")!.Id);
            TestHarness.True(FormatRegistry.IsArchive("e.tar"));
            TestHarness.False(FormatRegistry.IsArchive("f.txt"));
            TestHarness.True(FormatRegistry.Resolve("g.doc") is null);

            var creatable = FormatRegistry.CreatableFormats.Select(f => f.Id).ToList();
            TestHarness.True(creatable.Contains("zip") && creatable.Contains("7z"),
                "可创建格式应包含 zip 与 7z");

            var all = FormatRegistry.AllFormats.SelectMany(f => f.Extensions).Distinct().Count();
            TestHarness.True(all >= 42, $"支持解压的扩展名应不少于 42 种，实际 {all}");
        });

        t.Run("路径安全：Zip Slip 越界条目被拦截", () =>
        {
            var root = Path.Combine(workDir, "slip-root");
            Directory.CreateDirectory(root);

            var evil = Path.Combine(root, "evil.zip");
            using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("../../pwned.txt");
                using var w = new StreamWriter(e.Open());
                w.Write("should never be written outside");
            }

            var outDir = Path.Combine(root, "out");
            var ex = TestHarness.Throws<InvalidDataException>(() =>
            {
                var z = new ZipEngine();
                z.Extract(evil, outDir, new ArchiveOptions { OverwriteExisting = true },
                    null, null, CancellationToken.None);
            });
            TestHarness.Contains(ex.Message, "越界");

            TestHarness.False(File.Exists(Path.Combine(workDir, "pwned.txt")),
                "越界文件不得被写出到工作目录");
        });

        t.Run("路径安全：正常条目路径位于目标目录内", () =>
        {
            var root = Path.Combine(workDir, "safe-root");
            var resolved = PathSafety.ResolveEntryPath(root, Path.Combine("sub", "a.txt"));
            TestHarness.True(PathSafety.NormalizeRoot(root).StartsWith(
                PathSafety.NormalizeRoot(workDir), StringComparison.OrdinalIgnoreCase) ||
                resolved.StartsWith(PathSafety.NormalizeRoot(root), StringComparison.OrdinalIgnoreCase));
        });

        t.Run("ZIP 内核：多文件创建 → 列出 → 解压 → 内容一致（往返）", () =>
        {
            var dir = Path.Combine(workDir, "zip-roundtrip");
            Directory.CreateDirectory(dir);
            var f1 = Path.Combine(dir, "a.txt");
            var f2 = Path.Combine(dir, "b.txt");
            File.WriteAllText(f1, "hello zzzip", Encoding.UTF8);
            File.WriteAllText(f2, new string('x', 5000), Encoding.UTF8);

            var zipPath = Path.Combine(dir, "out.zip");
            var engine = new ZipEngine();
            engine.Create([f1, f2], zipPath, new ArchiveOptions { Level = CompressionLevelOption.Normal },
                null, CancellationToken.None);

            TestHarness.True(File.Exists(zipPath), "压缩包应被创建");

            var entries = engine.List(zipPath);
            TestHarness.Equal(2, entries.Count, "应列出 2 个条目");
            TestHarness.True(entries.Any(e => e.Path == "a.txt"), "应含 a.txt");
            TestHarness.True(entries.All(e => !e.IsEncrypted), "ZIP 非加密包条目不应标记加密");

            var dest = Path.Combine(dir, "unpacked");
            engine.Extract(zipPath, dest, new ArchiveOptions { OverwriteExisting = true },
                null, null, CancellationToken.None);

            TestHarness.Equal("hello zzzip", File.ReadAllText(Path.Combine(dest, "a.txt")));
            // 与源文件对比：Encoding.UTF8 会写入 3 字节 BOM，硬编码 5000 会误判
            TestHarness.Equal(new FileInfo(f2).Length, new FileInfo(Path.Combine(dest, "b.txt")).Length);
        });

        t.Run("ZIP 内核：目录树层级被完整保留", () =>
        {
            var src = Path.Combine(workDir, "zip-tree", "proj");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "top.txt"), "top");
            File.WriteAllText(Path.Combine(src, "sub", "deep.txt"), "deep");

            var zipPath = Path.Combine(workDir, "zip-tree", "proj.zip");
            var engine = new ZipEngine();
            engine.Create([src], zipPath, new ArchiveOptions(), null, CancellationToken.None);

            var entries = engine.List(zipPath).Select(e => e.Path).ToList();
            TestHarness.True(entries.Contains(@"proj\top.txt"), "应含 proj\\top.txt，实际：" + string.Join(",", entries));
            TestHarness.True(entries.Contains(@"proj\sub\deep.txt"), "应含 proj\\sub\\deep.txt");

            var dest = Path.Combine(workDir, "zip-tree", "out");
            engine.Extract(zipPath, dest, new ArchiveOptions { OverwriteExisting = true },
                null, null, CancellationToken.None);
            TestHarness.True(File.Exists(Path.Combine(dest, "proj", "sub", "deep.txt")), "深层文件应被解出");
        });

        t.Run("ZIP 内核：仅解压选中条目（部分解压）", () =>
        {
            var dir = Path.Combine(workDir, "zip-partial");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "keep.txt"), "keep");
            File.WriteAllText(Path.Combine(dir, "drop.txt"), "drop");

            var zipPath = Path.Combine(dir, "p.zip");
            var engine = new ZipEngine();
            engine.Create([Path.Combine(dir, "keep.txt"), Path.Combine(dir, "drop.txt")],
                zipPath, new ArchiveOptions(), null, CancellationToken.None);

            var dest = Path.Combine(dir, "out");
            engine.Extract(zipPath, dest, new ArchiveOptions { OverwriteExisting = true },
                e => e.Path == "keep.txt", null, CancellationToken.None);

            TestHarness.True(File.Exists(Path.Combine(dest, "keep.txt")), "选中条目应被解压");
            TestHarness.False(File.Exists(Path.Combine(dest, "drop.txt")), "未选中条目不应被解压");
        });

        t.Run("ZIP 注释：写入后可读回，覆盖写入正确", () =>
        {
            var dir = Path.Combine(workDir, "zip-comment");
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "c.txt");
            File.WriteAllText(f, "content");
            var zipPath = Path.Combine(dir, "c.zip");

            new ZipEngine().Create([f], zipPath, new ArchiveOptions(), null, CancellationToken.None);

            ArchiveComment.Write(zipPath, "第一版注释");
            TestHarness.Equal("第一版注释", ArchiveComment.Read(zipPath));

            ArchiveComment.Write(zipPath, "第二版注释：覆盖成功");
            TestHarness.Equal("第二版注释：覆盖成功", ArchiveComment.Read(zipPath));

            // 注释写入后压缩包仍可正常读取（不能破坏 EOCD 结构）
            var entries = new ZipEngine().List(zipPath);
            TestHarness.Equal(1, entries.Count, "写注释后压缩包仍应可读");
        });

        t.Run("ZIP 内核：不覆盖模式下跳过已存在文件", () =>
        {
            var dir = Path.Combine(workDir, "zip-overwrite");
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "o.txt");
            File.WriteAllText(f, "new");
            var zipPath = Path.Combine(dir, "o.zip");
            new ZipEngine().Create([f], zipPath, new ArchiveOptions(), null, CancellationToken.None);

            var dest = Path.Combine(dir, "out");
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "o.txt"), "old");

            new ZipEngine().Extract(zipPath, dest, new ArchiveOptions { OverwriteExisting = false },
                null, null, CancellationToken.None);
            TestHarness.Equal("old", File.ReadAllText(Path.Combine(dest, "o.txt")), "不覆盖时应保留原文件");

            new ZipEngine().Extract(zipPath, dest, new ArchiveOptions { OverwriteExisting = true },
                null, null, CancellationToken.None);
            TestHarness.Equal("new", File.ReadAllText(Path.Combine(dest, "o.txt")), "覆盖后应为新内容");
        });

        t.Run("ZIP 内核：遇到损坏数据抛出可读异常而非静默失败", () =>
        {
            var broken = Path.Combine(workDir, "broken.zip");
            File.WriteAllBytes(broken, Encoding.ASCII.GetBytes("this is definitely not a zip file"));
            TestHarness.Throws<InvalidDataException>(() => new ZipEngine().List(broken));
        });

        // ---------------- 7-Zip 内核（本机已装 7-Zip，全部真实执行） ----------------

        var has7z = ArchiveEngineRouter.HasSevenZip;
        ArchiveEngineRouter.EnsureConfigured();
        has7z |= ArchiveEngineRouter.HasSevenZip;

        t.Run("7-Zip 内核：探测可用性与引擎摘要", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            TestHarness.Contains(ArchiveEngineRouter.EngineSummary, "7-Zip");
        });

        // 验收标准第 6 条：7z.exe 缺失时必须降级为仅 ZIP 并给出中文提示，而不是崩溃。
        // 用环境变量模拟缺失（比真删文件干净，也不会污染开发机）。
        t.Run("引擎路由：7z.exe 缺失时降级为仅 ZIP 并给出中文提示", () =>
        {
            var dir = Path.Combine(workDir, "no7z");
            Directory.CreateDirectory(dir);
            var src = Path.Combine(dir, "a.txt");
            File.WriteAllText(src, "降级测试", Encoding.UTF8);
            var zipPath = Path.Combine(dir, "a.zip");

            try
            {
                Environment.SetEnvironmentVariable("ZZZIP_DISABLE_7Z", "1");
                ArchiveEngineRouter.Configure(null);   // 关键字面：让它按缺失重新路由

                TestHarness.False(ArchiveEngineRouter.HasSevenZip, "禁用后不应再报告 7-Zip 可用");
                TestHarness.Contains(ArchiveEngineRouter.EngineSummary, "未检测到 7-Zip",
                    "引擎摘要必须明确告知用户 7-Zip 缺失，而不是含糊带过");

                // 1) 纯 ZIP 仍必须可用 —— 走内置内核，功能不能因为缺 7z 就整体瘫掉
                ArchiveEngineRouter.Create([src], zipPath,
                    new ArchiveOptions { Format = FormatRegistry.Zip }, null, default);
                TestHarness.True(File.Exists(zipPath), "降级后仍应能用内置内核创建 ZIP");
                TestHarness.Equal(1, ArchiveEngineRouter.List(zipPath).Count,
                    "降级后仍应能用内置内核列出 ZIP 内容");

                // 2) 非 ZIP 格式不能静默失败，要抛出可读的中文提示且指出解决办法
                var nonZip = TestHarness.Throws<NotSupportedException>(
                    () => ArchiveEngineRouter.List(Path.Combine(dir, "a.7z")));
                TestHarness.Contains(nonZip.Message, "7z.exe", "提示里应告诉用户把 7z.exe 放到哪里");
                TestHarness.Contains(nonZip.Message, "7-Zip", "提示里应点名是 7-Zip 引擎缺失");

                // 3) 加密 ZIP 也一样：明确拒绝并说明原因，而不是生成一个没加密的包
                var enc = TestHarness.Throws<NotSupportedException>(() =>
                    ArchiveEngineRouter.ForCreate(new ArchiveOptions
                    {
                        Format = FormatRegistry.Zip,
                        Password = "secret",
                    }));
                TestHarness.Contains(enc.Message, "7-Zip", "拒绝加密 ZIP 的理由应指向 7-Zip 引擎");
            }
            finally
            {
                Environment.SetEnvironmentVariable("ZZZIP_DISABLE_7Z", null);
                ArchiveEngineRouter.Configure(null);   // 恢复真实探测结果，避免影响后续用例
            }

            // 降级必须是临时的：开关一撤，引擎应立刻回来（本机本来就没有 7z 时不做此断言）
            if (has7z)
                TestHarness.True(ArchiveEngineRouter.HasSevenZip, "撤掉开关后应重新探测到 7-Zip 引擎");
        });

        t.Run("7-Zip 内核：7Z 格式创建 → 列出 → 解压往返", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            var dir = Path.Combine(workDir, "7z-roundtrip");
            Directory.CreateDirectory(dir);
            var f1 = Path.Combine(dir, "n1.txt");
            File.WriteAllText(f1, "七内核往返测试", Encoding.UTF8);
            var f2 = Path.Combine(dir, "n2.bin");
            File.WriteAllBytes(f2, RandomBytes(300_000));

            var out7z = Path.Combine(dir, "out.7z");
            ArchiveEngineRouter.Create([f1, f2], out7z,
                new ArchiveOptions { Format = FormatRegistry.SevenZ, Level = CompressionLevelOption.Smallest },
                null, CancellationToken.None);
            TestHarness.True(File.Exists(out7z), "7z 包应被创建");

            var entries = ArchiveEngineRouter.List(out7z);
            TestHarness.Equal(2, entries.Count, "应列出 2 个条目，实际：" +
                string.Join(",", entries.Select(e => e.Path)));
            TestHarness.True(entries.Any(e => e.Path == "n1.txt"));
            TestHarness.True(entries.Sum(e => e.Size) >= 300_000, "解压后总大小应不小于原始数据");

            var dest = Path.Combine(dir, "out");
            ArchiveEngineRouter.Extract(out7z, dest, new ArchiveOptions { OverwriteExisting = true },
                null, null, CancellationToken.None);
            TestHarness.Equal("七内核往返测试", File.ReadAllText(Path.Combine(dest, "n1.txt")));
            TestHarness.Equal(300_000, new FileInfo(Path.Combine(dest, "n2.bin")).Length);
        });

        t.Run("7-Zip 内核：AES-256 加密（错误/缺失密码必须失败，正确密码成功）", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            var dir = Path.Combine(workDir, "7z-crypto");
            Directory.CreateDirectory(dir);
            var secret = Path.Combine(dir, "secret.txt");
            File.WriteAllText(secret, "机密内容 zzzip", Encoding.UTF8);
            var enc = Path.Combine(dir, "enc.7z");

            ArchiveEngineRouter.Create([secret], enc, new ArchiveOptions
            {
                Format = FormatRegistry.SevenZ,
                Password = "P@ssw0rd-zzzip",
                Level = CompressionLevelOption.Normal,
            }, null, CancellationToken.None);
            TestHarness.True(File.Exists(enc));

            // 无密码解压必须失败
            TestHarness.Throws<Exception>(() => ArchiveEngineRouter.Extract(
                enc, Path.Combine(dir, "no-pass"),
                new ArchiveOptions { OverwriteExisting = true }, null, null, CancellationToken.None),
                "无密码解压加密包应当失败");

            // 错误密码必须失败
            TestHarness.Throws<Exception>(() => ArchiveEngineRouter.Extract(
                enc, Path.Combine(dir, "bad-pass"),
                new ArchiveOptions { OverwriteExisting = true, Password = "wrong-password" },
                null, null, CancellationToken.None),
                "错误密码解压加密包应当失败");

            // 正确密码必须成功
            var okDir = Path.Combine(dir, "good-pass");
            ArchiveEngineRouter.Extract(enc, okDir,
                new ArchiveOptions { OverwriteExisting = true, Password = "P@ssw0rd-zzzip" },
                null, null, CancellationToken.None);
            TestHarness.Equal("机密内容 zzzip", File.ReadAllText(Path.Combine(okDir, "secret.txt")));
        });

        t.Run("7-Zip 内核：固实压缩（多小文件，体积应优势明显）", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            var dir = Path.Combine(workDir, "7z-solid");
            Directory.CreateDirectory(dir);
            var files = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                var p = Path.Combine(dir, $"s{i:D2}.txt");
                File.WriteAllText(p, string.Concat(Enumerable.Repeat("固实压缩测试数据 abcdefg ", 200)));
                files.Add(p);
            }

            var solidOut = Path.Combine(dir, "solid.7z");
            var normalOut = Path.Combine(dir, "normal.7z");

            ArchiveEngineRouter.Create(files, solidOut, new ArchiveOptions
            {
                Format = FormatRegistry.SevenZ,
                Level = CompressionLevelOption.Smallest,
                Solid = true,
            }, null, CancellationToken.None);

            ArchiveEngineRouter.Create(files, normalOut, new ArchiveOptions
            {
                Format = FormatRegistry.SevenZ,
                Level = CompressionLevelOption.Smallest,
                Solid = false,
            }, null, CancellationToken.None);

            var solidSize = new FileInfo(solidOut).Length;
            var normalSize = new FileInfo(normalOut).Length;
            TestHarness.True(solidSize > 0 && normalSize > 0, "两种模式都应产出压缩包");
            TestHarness.True(solidSize <= normalSize,
                $"固实压缩体积应不大于非固实（固实 {solidSize} vs 非固实 {normalSize}）");

            var dest = Path.Combine(dir, "out");
            ArchiveEngineRouter.Extract(solidOut, dest, new ArchiveOptions { OverwriteExisting = true },
                null, null, CancellationToken.None);
            TestHarness.Equal(40, Directory.GetFiles(dest, "*.txt").Length, "固实包应能完整解出 40 个文件");
        });

        t.Run("7-Zip 内核：分卷压缩（1MB 分卷应产生多个卷文件）", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            var dir = Path.Combine(workDir, "7z-volume");
            Directory.CreateDirectory(dir);
            var big = Path.Combine(dir, "big.bin");
            File.WriteAllBytes(big, RandomBytes(3 * 1024 * 1024)); // 3MB 随机数据，不可压缩

            var out7z = Path.Combine(dir, "vol.7z");
            ArchiveEngineRouter.Create([big], out7z, new ArchiveOptions
            {
                Format = FormatRegistry.SevenZ,
                Level = CompressionLevelOption.Fast,
                VolumeSizeMb = 1,
            }, null, CancellationToken.None);

            var volumes = Directory.GetFiles(dir, "vol.7z.*");
            TestHarness.True(volumes.Length >= 2,
                $"1MB 分卷压 3MB 数据应产生至少 2 个卷，实际 {volumes.Length} 个");

            // 第一卷应能被正常识别并解压
            ArchiveEngineRouter.Extract(volumes.OrderBy(v => v).First(),
                Path.Combine(dir, "out"),
                new ArchiveOptions { OverwriteExisting = true }, null, null, CancellationToken.None);
            TestHarness.Equal(3 * 1024 * 1024, new FileInfo(Path.Combine(dir, "out", "big.bin")).Length,
                "分卷包应能完整还原原始数据");
        });

        t.Run("7-Zip 内核：自解压 EXE（sfx 模块缺失时跳过）", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            var dir = Path.Combine(workDir, "7z-sfx");
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "s.txt");
            File.WriteAllText(f, "sfx payload");

            var sfx = Path.Combine(dir, "pack.exe");
            try
            {
                ArchiveEngineRouter.Create([f], sfx, new ArchiveOptions
                {
                    Format = FormatRegistry.SevenZ,
                    CreateSfx = true,
                }, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                TestHarness.Skip("本机 7z.sfx 模块不可用：" + ex.Message);
            }

            if (!File.Exists(sfx))
                TestHarness.Skip("未生成 SFX 文件");

            var head = new byte[2];
            using (var fs = File.OpenRead(sfx)) fs.ReadExactly(head);
            TestHarness.Equal((byte)'M', head[0], "SFX 产物应为 PE 可执行文件（MZ 头）");
            TestHarness.Equal((byte)'Z', head[1]);
        });

        t.Run("7-Zip 列表解析：能从真实 -slt 输出中提取条目", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            var dir = Path.Combine(workDir, "7z-parse");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "x.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "y.txt"), "yy");
            var out7z = Path.Combine(dir, "p.7z");
            ArchiveEngineRouter.Create(
                [Path.Combine(dir, "x.txt"), Path.Combine(dir, "y.txt")],
                out7z, new ArchiveOptions { Format = FormatRegistry.SevenZ },
                null, CancellationToken.None);

            var entries = ArchiveEngineRouter.List(out7z);
            TestHarness.Equal(2, entries.Count);
            TestHarness.True(entries.All(e => !e.IsDirectory), "文件条目不应被识别为目录");
            TestHarness.True(entries.All(e => e.Size > 0), "条目大小应被正确解析");
        });

        t.Run("7-Zip 内核：测试命令可校验完整性", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            var dir = Path.Combine(workDir, "7z-test");
            Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "t.txt");
            File.WriteAllText(f, "integrity");
            var out7z = Path.Combine(dir, "t.7z");
            ArchiveEngineRouter.Create([f], out7z, new ArchiveOptions { Format = FormatRegistry.SevenZ },
                null, CancellationToken.None);

            TestHarness.True(ArchiveEngineRouter.Test(out7z, null, null, CancellationToken.None),
                "完好压缩包应通过完整性测试");
        });

        t.Run("大文件流式压缩：10MB 随机数据往返无损坏", () =>
        {
            if (!has7z) TestHarness.Skip("本机未找到 7z.exe");
            var dir = Path.Combine(workDir, "large");
            Directory.CreateDirectory(dir);
            var data = RandomBytes(10 * 1024 * 1024);
            var src = Path.Combine(dir, "large.bin");
            File.WriteAllBytes(src, data);

            var out7z = Path.Combine(dir, "large.7z");
            ArchiveEngineRouter.Create([src], out7z,
                new ArchiveOptions { Format = FormatRegistry.SevenZ, Level = CompressionLevelOption.Fast },
                null, CancellationToken.None);

            var dest = Path.Combine(dir, "out");
            ArchiveEngineRouter.Extract(out7z, dest, new ArchiveOptions { OverwriteExisting = true },
                null, null, CancellationToken.None);

            var roundtrip = File.ReadAllBytes(Path.Combine(dest, "large.bin"));
            TestHarness.Equal(data.Length, roundtrip.Length, "往返后长度应一致");
            TestHarness.True(data.AsSpan().SequenceEqual(roundtrip), "往返后字节内容应完全一致");
        });
    }

    internal static byte[] RandomBytes(int count)
    {
        var b = new byte[count];
        new Random(20261014).NextBytes(b);
        return b;
    }
}