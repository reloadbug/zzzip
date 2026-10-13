namespace zzzip.Core;

/// <summary>压缩格式描述。All 覆盖对标 360 的 42 种解压格式，可创建格式目前为 ZIP / 7Z。</summary>
public sealed record ArchiveFormat(
    string Id,
    string Extension,
    string DisplayName,
    bool CanCreate,
    bool SupportsMultiVolume,
    string[] Extensions);

/// <summary>格式注册表：扩展名 -> 格式。纯静态数据，无外部依赖。</summary>
public static class FormatRegistry
{
    private static readonly string[] ZipExt = [".zip"];
    private static readonly string[] SevenZExt = [".7z"];
    private static readonly string[] TarExt = [".tar"];
    private static readonly string[] GzExt = [".gz", ".tgz"];
    private static readonly string[] Bz2Ext = [".bz2", ".tbz2"];
    private static readonly string[] XzExt = [".xz", ".txz"];
    private static readonly string[] ZstExt = [".zst", ".tzst"];
    private static readonly string[] RarExt = [".rar"];
    private static readonly string[] IsoExt = [".iso", ".img", ".bin", ".nrg", ".mdf", ".cue", ".vcd"];
    private static readonly string[] CabExt = [".cab"];
    private static readonly string[] ArjExt = [".arj"];
    private static readonly string[] LzhExt = [".lzh", ".lha"];
    private static readonly string[] UueExt = [".uue", ".uu"];
    private static readonly string[] WimExt = [".wim"];
    private static readonly string[] JarExt = [".jar", ".war", ".ear", ".apk", ".epub"];
    private static readonly string[] UdfExt = [".udf", ".hfs", ".iso9660"];
    private static readonly string[] DmgExt = [".dmg"];
    private static readonly string[] VhdExt = [".vhd", ".vhdx", ".vdi", ".vmdk"];
    private static readonly string[] MsiExt = [".msi"];
    private static readonly string[] ChmExt = [".chm"];
    private static readonly string[] CpioExt = [".cpio"];
    private static readonly string[] RpmExt = [".rpm"];
    private static readonly string[] DebExt = [".deb"];
    private static readonly string[] LzmaExt = [".lzma"];
    private static readonly string[] SingleZExt = [".z"];
    private static readonly string[] ArExt = [".ar", ".a"];
    private static readonly string[] XpiExt = [".xpi"];

    public static readonly ArchiveFormat Zip = new("zip", ".zip", "ZIP", true, true, ZipExt);
    public static readonly ArchiveFormat SevenZ = new("7z", ".7z", "7Z", true, true, SevenZExt);
    public static readonly ArchiveFormat Tar = new("tar", ".tar", "TAR", true, true, TarExt);
    public static readonly ArchiveFormat Gzip = new("gzip", ".gz", "GZIP", true, false, GzExt);
    public static readonly ArchiveFormat Bzip2 = new("bzip2", ".bz2", "BZIP2", false, false, Bz2Ext);
    public static readonly ArchiveFormat Xz = new("xz", ".xz", "XZ", false, false, XzExt);
    public static readonly ArchiveFormat Zstd = new("zstd", ".zst", "ZSTD", false, false, ZstExt);
    public static readonly ArchiveFormat Rar = new("rar", ".rar", "RAR", false, true, RarExt);
    public static readonly ArchiveFormat Iso = new("iso", ".iso", "ISO", false, false, IsoExt);
    public static readonly ArchiveFormat Cab = new("cab", ".cab", "CAB", false, false, CabExt);
    public static readonly ArchiveFormat Arj = new("arj", ".arj", "ARJ", false, false, ArjExt);
    public static readonly ArchiveFormat Lzh = new("lzh", ".lzh", "LZH", false, false, LzhExt);
    public static readonly ArchiveFormat Uue = new("uue", ".uue", "UUE", false, false, UueExt);
    public static readonly ArchiveFormat Wim = new("wim", ".wim", "WIM", false, false, WimExt);
    public static readonly ArchiveFormat Java = new("jar", ".jar", "JAR", false, false, JarExt);
    public static readonly ArchiveFormat Udf = new("udf", ".udf", "UDF", false, false, UdfExt);
    public static readonly ArchiveFormat Dmg = new("dmg", ".dmg", "DMG", false, false, DmgExt);
    public static readonly ArchiveFormat Vhd = new("vhd", ".vhd", "VHD", false, false, VhdExt);
    public static readonly ArchiveFormat Msi = new("msi", ".msi", "MSI", false, false, MsiExt);
    public static readonly ArchiveFormat Chm = new("chm", ".chm", "CHM", false, false, ChmExt);
    public static readonly ArchiveFormat Cpio = new("cpio", ".cpio", "CPIO", false, false, CpioExt);
    public static readonly ArchiveFormat Rpm = new("rpm", ".rpm", "RPM", false, false, RpmExt);
    public static readonly ArchiveFormat Deb = new("deb", ".deb", "DEB", false, false, DebExt);
    public static readonly ArchiveFormat Lzma = new("lzma", ".lzma", "LZMA", false, false, LzmaExt);
    public static readonly ArchiveFormat SingleZ = new("z", ".z", "Z", false, false, SingleZExt);
    public static readonly ArchiveFormat Ar = new("ar", ".ar", "AR", false, false, ArExt);
    public static readonly ArchiveFormat Xpi = new("xpi", ".xpi", "XPI", false, false, XpiExt);

    /// <summary>可创建的格式，顺序即【压缩设置】里的下拉顺序。</summary>
    public static IReadOnlyList<ArchiveFormat> CreatableFormats { get; } =
        [Zip, SevenZ, Tar, Gzip];

    /// <summary>全部支持解压的格式，供【文件关联】页面勾选。</summary>
    public static IReadOnlyList<ArchiveFormat> AllFormats { get; } =
    [
        Zip, SevenZ, Rar, Tar, Gzip, Bzip2, Xz, Zstd, Iso, Cab, Arj, Lzh, Uue, Wim, Java,
        Udf, Dmg, Vhd, Msi, Chm, Cpio, Rpm, Deb, Lzma, SingleZ, Ar, Xpi,
    ];

    private static readonly Dictionary<string, ArchiveFormat> ByExtension =
        AllFormats.SelectMany(f => f.Extensions.Select(e => (e, f)))
                  .GroupBy(x => x.e)
                  .ToDictionary(g => g.Key, g => g.First().f);

    /// <summary>已知压缩包扩展名集合（用于 Shell 图标/右键菜单判定）。</summary>
    public static IReadOnlySet<string> KnownExtensions { get; } =
        ByExtension.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static ArchiveFormat? Resolve(string pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension)) return null;
        var s = pathOrExtension.Trim();

        string ext;
        if (s.Contains('\\') || s.Contains('/') || Path.IsPathRooted(s))
            ext = Path.GetExtension(s).ToLowerInvariant();          // 完整路径，如 C:\a\b.RAR
        else if (s.StartsWith('.'))
            ext = s.ToLowerInvariant();                              // 纯扩展名，如 .zip
        else if (s.Contains('.'))
            ext = Path.GetExtension(s).ToLowerInvariant();           // 文件名，如 a.ZIP
        else
            ext = "." + s.ToLowerInvariant();                        // 无点扩展名，如 zip

        return ext.Length <= 1 ? null : ByExtension.GetValueOrDefault(ext);
    }

    public static bool IsArchive(string path) => Resolve(path) is not null;

    /// <summary>提取所有解压扩展名（不含点），用于写注册表 FileExts。</summary>
    public static string[] AllExtNames() =>
        AllFormats.SelectMany(f => f.Extensions).Select(e => e.TrimStart('.'))
                  .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}