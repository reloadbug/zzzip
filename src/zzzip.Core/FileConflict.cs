namespace zzzip.Core;

/// <summary>
/// 解压时遇到同名文件，用户可以做出的选择。
///
/// 「全部xx」的语义是：**这一个**按该动作处理，并且**后续所有冲突**都沿用同一动作、
/// 不再打扰用户（就是 WinRAR「全部替换」/ 360 压缩「全部覆盖」的意思）。
/// 把它收敛成决策枚举而不是让 UI 自己记状态，是为了让 ZIP 与 7z 两个内核
/// 走完全一样的规则 —— 双内核最容易出的问题就是两边行为悄悄跑偏。
/// </summary>
public enum ConflictDecision
{
    /// <summary>覆盖这一个：用压缩包里的版本替换掉磁盘上已有的文件。</summary>
    Overwrite,

    /// <summary>覆盖这一个，并且后续冲突一律覆盖，不再询问。</summary>
    OverwriteAll,

    /// <summary>跳过这一个：磁盘上已有的文件保持原样不动。</summary>
    Skip,

    /// <summary>跳过这一个，并且后续冲突一律跳过，不再询问。</summary>
    SkipAll,

    /// <summary>改名写入：解压出来的文件自动加序号（name (1).ext），原文件不动。</summary>
    Rename,

    /// <summary>改名写入这一个，并且后续冲突一律改名，不再询问。</summary>
    RenameAll,

    /// <summary>中止本次解压（已经解出来的文件保留）。</summary>
    Cancel,
}

/// <summary>一处「目标目录里已经有同名文件」的现场，用来给用户做左右对比。</summary>
public sealed record FileConflict
{
    /// <summary>压缩包内的那个条目。</summary>
    public required ArchiveEntry Entry { get; init; }

    /// <summary>磁盘上已存在文件的完整路径。</summary>
    public required string TargetPath { get; init; }

    /// <summary>磁盘上已存在文件的大小（字节）。</summary>
    public required long ExistingSize { get; init; }

    /// <summary>磁盘上已存在文件的修改时间（UTC）。</summary>
    public required DateTimeOffset ExistingModifiedUtc { get; init; }

    /// <summary>这是第几处冲突（从 1 开始）。</summary>
    public required int Index { get; init; }

    /// <summary>本次解压一共有几处冲突。</summary>
    public required int Total { get; init; }

    /// <summary>包内路径，例如 ~modsdd\BlueprintLoader\x.pak。</summary>
    public string EntryPath => Entry.Path;

    /// <summary>包内文件名（不含目录）。</summary>
    public string EntryFileName => System.IO.Path.GetFileName(Entry.Path);

    /// <summary>目标文件名（不含目录）。</summary>
    public string TargetFileName => System.IO.Path.GetFileName(TargetPath);

    /// <summary>
    /// 两侧大小是否一致。一致时多半就是同一个文件，
    /// 对话框会顺带提示一句，帮用户更快下决心。
    /// </summary>
    public bool SameSize => Entry.Size == ExistingSize;
}

/// <summary>
/// 冲突决策提供者：Core 层只认这个接口，具体的询问方式交给调用方
/// （桌面端弹「文件替换」对话框，自动化测试里直接返回一个固定答案）。
/// 这样内核代码既不用碰 WPF，也能被完整地自动化验证。
/// </summary>
public interface IConflictResolver
{
    ConflictDecision Resolve(FileConflict conflict);
}

/// <summary>用户在冲突对话框里点了「取消」：整包解压中止，已经解出来的文件保留。</summary>
/// <remarks>
/// 继承自 <see cref="OperationCanceledException"/>，便于调用方把「用户主动取消」
/// 与「解压真的失败了」区分开 —— 前者不该弹红色报错框。
/// </remarks>
public sealed class ExtractCancelledException : OperationCanceledException
{
    public ExtractCancelledException() : base("已取消解压。") { }
}
