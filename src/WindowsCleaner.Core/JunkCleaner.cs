using System.Runtime.InteropServices;

namespace WindowsCleaner.Core;

public interface IRecycleBin
{
    long GetSizeBytes();
    void Empty();
}

public sealed class ShellRecycleBin : IRecycleBin
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    private const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;

    public long GetSizeBytes()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }

    public void Empty()
    {
        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }
}

public sealed class JunkCleaner : ICleaner
{
    public const string RecycleBinId = "junk.recyclebin";

    private readonly FileTargetCleaner _files;
    private readonly IRecycleBin _bin;

    public JunkCleaner(IEnumerable<FileTarget> targets, SafePaths safe, IRecycleBin bin)
    {
        _files = new FileTargetCleaner("Junk files", targets, safe);
        _bin = bin;
    }

    public string Name => "System junk";

    public static Func<FileInfo, bool> OlderThan(TimeSpan age) => f => f.LastWriteTimeUtc < DateTime.UtcNow - age;

    public async Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default)
    {
        var items = (await _files.ScanAsync(ct)).ToList();
        long bin = 0;
        try { bin = _bin.GetSizeBytes(); } catch { /* treat as empty */ }
        if (bin > 0) items.Add(new CleanItem(RecycleBinId, "Windows", "Recycle Bin", bin));
        return items;
    }

    public async Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var list = selected.ToList();
        var result = await _files.CleanAsync(list, progress, ct);
        var binItem = list.FirstOrDefault(i => i.Id == RecycleBinId);
        if (binItem is null) return result;
        progress?.Report("Recycle Bin");
        try
        {
            _bin.Empty();
            return result.Plus(new CleanResult(binItem.SizeBytes, 1, 0, []));
        }
        catch (Exception e)
        {
            return result.Plus(new CleanResult(0, 0, 1, [$"Recycle Bin: {e.Message}"]));
        }
    }

    public static JunkCleaner CreateDefault(AppSettings settings)
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temp = Path.GetTempPath();
        string W(params string[] parts) => Path.Combine([win, .. parts]);
        var explorer = Path.Combine(local, "Microsoft", "Windows", "Explorer");
        var crashDumps = Path.Combine(local, "CrashDumps");

        var day = OlderThan(TimeSpan.FromDays(1));
        var logAge = OlderThan(TimeSpan.FromDays(settings.LogRetentionDays));
        bool IsLog(FileInfo f) =>
            f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.Equals(".etl", StringComparison.OrdinalIgnoreCase);

        FileTarget[] targets =
        [
            new("junk.usertemp", "Windows", "User temp files", () => FileEnumerator.Files(temp, "*", day)),
            new("junk.wintemp", "Windows", "Windows temp files", () => FileEnumerator.Files(W("Temp"), "*", day)),
            new("junk.prefetch", "Windows", "Prefetch", () => FileEnumerator.Files(W("Prefetch"), "*.pf")),
            new("junk.wucache", "Windows", "Windows Update download cache", () => FileEnumerator.Files(W("SoftwareDistribution", "Download"))),
            new("junk.logs", "Windows", "Old log files", () => FileEnumerator.Files(W("Logs"), "*", f => logAge(f) && IsLog(f))),
            new("junk.thumbs", "Windows", "Thumbnail cache", () => FileEnumerator.Files(explorer, "thumbcache_*.db")),
            new("junk.dumps", "Windows", "Crash dumps",
                () => FileEnumerator.Files(crashDumps).Concat(FileEnumerator.Files(W("Minidump")))),
        ];

        var safe = new SafePaths([temp, W("Temp"), W("Prefetch"), W("SoftwareDistribution", "Download"),
            W("Logs"), explorer, crashDumps, W("Minidump")]);
        return new JunkCleaner(targets, safe, new ShellRecycleBin());
    }
}
