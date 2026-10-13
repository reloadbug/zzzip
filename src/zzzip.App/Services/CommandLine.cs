namespace zzzip.App.Services;

/// <summary>命令行参数解析结果。动词 ID 与 ContextMenuRegistrar.Verbs 一一对应。</summary>
public sealed class CommandLine
{
    public string? Verb { get; init; }
    public IReadOnlyList<string> Targets { get; init; } = [];
    public string? ExtractTo { get; init; }
    public string? PresetName { get; init; }
    public bool PickArchiveOnStart { get; init; }
    public bool IsRegisterOnly { get; init; }
    public bool IsUnregisterOnly { get; init; }
    public bool CascadeMenu { get; init; }

    /// <summary>静默模式：不弹任何对话框，错误写 stderr。供无人值守/自动化调用。</summary>
    public bool Silent { get; init; }

    public static CommandLine Parse(string[] args)
    {
        string? verb = null, extractTo = null, preset = null;
        bool pick = false;
        bool register = false;
        bool unregister = false;
        bool cascade = false;
        bool silent = false;
        var targets = new List<string>();

        foreach (var a in args)
        {
            if (a.StartsWith("--verb=", StringComparison.OrdinalIgnoreCase)) verb = a[7..];
            else if (a.StartsWith("--to=", StringComparison.OrdinalIgnoreCase)) extractTo = a[5..];
            else if (a.StartsWith("--name=", StringComparison.OrdinalIgnoreCase)) preset = a[7..];
            else if (string.Equals(a, "--pick", StringComparison.OrdinalIgnoreCase)) pick = true;
            else if (string.Equals(a, "--register", StringComparison.OrdinalIgnoreCase)) register = true;
            else if (string.Equals(a, "--unregister", StringComparison.OrdinalIgnoreCase)) unregister = true;
            else if (string.Equals(a, "--cascade", StringComparison.OrdinalIgnoreCase)) cascade = true;
            else if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)) silent = true;
            else if (!a.StartsWith('-')) targets.Add(a);
        }

        return new CommandLine
        {
            Verb = verb,
            Targets = targets,
            ExtractTo = extractTo,
            PresetName = preset,
            PickArchiveOnStart = pick,
            IsRegisterOnly = register,
            IsUnregisterOnly = unregister,
            CascadeMenu = cascade,
            Silent = silent,
        };
    }
}