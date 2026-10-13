using System.Text;

namespace zzzip.Core;

/// <summary>压缩包注释读写。ZIP 的注释存放在 EOCD 记录尾部，符合规范，WinRAR / 7-Zip 均可读取。</summary>
public static class ArchiveComment
{
    /// <summary>把注释写入压缩包。ZIP 直接改 EOCD；其他格式交由 7-Zip 内核。</summary>
    public static void Write(string archivePath, string comment)
    {
        var format = FormatRegistry.Resolve(archivePath);
        if (format is null || format.Id != "zip")
            throw new NotSupportedException("当前仅支持为 ZIP 压缩包添加注释。");

        var bytes = Encoding.UTF8.GetBytes(comment);
        if (bytes.Length > 0xFFFF)
            throw new InvalidOperationException("注释内容过长（上限 65535 字节）。");

        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite);
        var len = fs.Length;
        if (len < 22) throw new InvalidDataException("文件太小，不是有效的 ZIP 压缩包。");

        var tailLen = (int)Math.Min(len, 66_000);
        var tail = new byte[tailLen];
        fs.Seek(-tailLen, SeekOrigin.End);
        fs.ReadExactly(tail);

        long eocd = -1;
        for (int i = tailLen - 22; i >= 0; i--)
        {
            if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
            {
                eocd = len - tailLen + i;
                break;
            }
        }
        if (eocd < 0) throw new InvalidDataException("未找到 ZIP 的 EOCD 记录，压缩包可能已损坏。");

        // EOCD 布局：偏移 20 是注释长度字段，之后紧跟注释本体。
        // 覆盖写 = 更新长度 + 写入新注释 + 截断尾部（旧注释随之丢弃）。
        fs.Seek(eocd + 20, SeekOrigin.Begin);
        fs.Write(BitConverter.GetBytes((ushort)bytes.Length));
        fs.Write(bytes);
        fs.SetLength(fs.Position);
    }

    /// <summary>读取 ZIP 注释；无注释或非 ZIP 返回 null。</summary>
    public static string? Read(string archivePath)
    {
        var format = FormatRegistry.Resolve(archivePath);
        if (format is null || format.Id != "zip") return null;

        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var len = fs.Length;
        if (len < 22) return null;

        var tailLen = (int)Math.Min(len, 66_000);
        var tail = new byte[tailLen];
        fs.Seek(-tailLen, SeekOrigin.End);
        fs.ReadExactly(tail);

        for (int i = tailLen - 22; i >= 0; i--)
        {
            if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
            {
                var cmtLen = BitConverter.ToUInt16(tail, i + 20);
                if (cmtLen == 0) return null;
                var start = i + 22;
                if (start + cmtLen > tailLen) return null;
                return Encoding.UTF8.GetString(tail, start, cmtLen);
            }
        }
        return null;
    }
}