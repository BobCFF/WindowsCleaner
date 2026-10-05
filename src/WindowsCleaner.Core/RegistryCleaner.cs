using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed class RegistryCleaner : ICleaner
{
    private const char Sep = '\t';
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows\CurrentVersion";
    private static readonly string[] UninstallPaths =
        [CurrentVersion + @"\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"];
    private const string MuiCache = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

    private readonly IRegistryAccess _reg;
    private readonly Func<string, bool> _exists;
    private readonly string _backupRoot;
    private readonly Func<DateTime> _now;

    public RegistryCleaner(IRegistryAccess reg, Func<string, bool> pathExists, string backupRoot, Func<DateTime>? now = null)
    {
        _reg = reg;
        _exists = pathExists;
        _backupRoot = backupRoot;
        _now = now ?? (() => DateTime.Now);
    }

    public string Name => "Registry";

    // ---- scan -------------------------------------------------------------------------------

    public Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default) =>
        Task.Run(() => (IReadOnlyList<CleanItem>)Scan(ct).ToList(), ct);

    private IEnumerable<CleanItem> Scan(CancellationToken ct)
    {
        foreach (var i in ScanUninstall()) { ct.ThrowIfCancellationRequested(); yield return i; }
        foreach (var i in ScanSharedDlls()) { ct.ThrowIfCancellationRequested(); yield return i; }
        foreach (var i in ScanAppPaths()) { ct.ThrowIfCancellationRequested(); yield return i; }
        foreach (var i in ScanMuiCache()) { ct.ThrowIfCancellationRequested(); yield return i; }
        foreach (var i in ScanExtensions()) { ct.ThrowIfCancellationRequested(); yield return i; }
    }

    private IEnumerable<CleanItem> ScanUninstall()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var basePath in UninstallPaths)
        foreach (var sub in _reg.GetSubKeyNames(hive, basePath))
        {
            var path = $"{basePath}\\{sub}";
            if (_reg.GetValue(hive, path, "DisplayName") is not string name || name.Length == 0) continue;
            if (_reg.GetValue(hive, path, "SystemComponent") is 1) continue;
            if (_reg.GetValue(hive, path, "UninstallString") is not string cmd) continue;
            var exe = ExtractExe(cmd);
            if (exe is null || !IsRootedFile(exe) || Path.GetFileNameWithoutExtension(exe).Equals("msiexec", StringComparison.OrdinalIgnoreCase)) continue;
            if (_exists(exe)) continue;
            var loc = _reg.GetValue(hive, path, "InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(loc) && _exists(loc)) continue;
            yield return KeyItem("Uninstall entry", $"{name} (uninstaller missing)", hive, path);
        }
    }

    private IEnumerable<CleanItem> ScanSharedDlls()
    {
        var path = CurrentVersion + @"\SharedDLLs";
        foreach (var n in _reg.GetValueNames(RegistryHive.LocalMachine, path))
            if (IsRootedFile(n) && !_exists(n))
                yield return ValueItem("Shared DLL", n, RegistryHive.LocalMachine, path, n);
    }

    private IEnumerable<CleanItem> ScanAppPaths()
    {
        var basePath = CurrentVersion + @"\App Paths";
        foreach (var sub in _reg.GetSubKeyNames(RegistryHive.LocalMachine, basePath))
        {
            var path = $"{basePath}\\{sub}";
            if (_reg.GetValue(RegistryHive.LocalMachine, path, "") is not string target) continue;
            target = target.Trim('"');
            if (IsRootedFile(target) && !_exists(target))
                yield return KeyItem("App Path", $"{sub} → {target}", RegistryHive.LocalMachine, path);
        }
    }

    private IEnumerable<CleanItem> ScanMuiCache()
    {
        foreach (var n in _reg.GetValueNames(RegistryHive.CurrentUser, MuiCache))
        {
            var file = n;
            foreach (var suffix in new[] { ".FriendlyAppName", ".ApplicationCompany" })
                if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) file = file[..^suffix.Length];
            if (IsRootedFile(file) && !_exists(file))
                yield return ValueItem("MUI cache", n, RegistryHive.CurrentUser, MuiCache, n);
        }
    }

    private IEnumerable<CleanItem> ScanExtensions()
    {
        const string classes = @"Software\Classes";
        foreach (var sub in _reg.GetSubKeyNames(RegistryHive.CurrentUser, classes))
        {
            if (!sub.StartsWith('.')) continue;
            if (_reg.GetValue(RegistryHive.CurrentUser, $"{classes}\\{sub}", "") is not string progId || progId.Length == 0) continue;
            if (_reg.KeyExists(RegistryHive.CurrentUser, $"{classes}\\{progId}") ||
                _reg.KeyExists(RegistryHive.LocalMachine, $"Software\\Classes\\{progId}")) continue;
            yield return KeyItem("File extension", $"{sub} → missing handler {progId}", RegistryHive.CurrentUser, $"{classes}\\{sub}");
        }
    }

    // ---- clean / restore --------------------------------------------------------------------

    public Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var refs = selected.Select(i => Parse(i.Id)).Where(r => r is not null).Select(r => r!).ToList();
            if (refs.Count == 0) return CleanResult.Empty;

            var dir = Path.Combine(_backupRoot, _now().ToString("yyyyMMdd-HHmmss"));
            try
            {
                progress?.Report("Backing up registry keys");
                Directory.CreateDirectory(dir);
                var n = 0;
                foreach (var key in refs.Select(r => (r.Hive, r.Path)).Distinct())
                    _reg.ExportKey(key.Hive, key.Path, Path.Combine(dir, $"{++n:D3}.reg"));
            }
            catch (Exception e)
            {
                return new CleanResult(0, 0, refs.Count, [$"Backup failed, nothing was changed: {e.Message}"]);
            }

            int deleted = 0, skipped = 0;
            var errors = new List<string>();
            foreach (var r in refs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (r.ValueName is null) _reg.DeleteSubKeyTree(r.Hive, r.Path);
                    else _reg.DeleteValue(r.Hive, r.Path, r.ValueName);
                    deleted++;
                }
                catch (Exception e)
                {
                    skipped++;
                    errors.Add($"{r.Path}: {e.Message}");
                }
            }
            return new CleanResult(0, deleted, skipped, errors);
        }, ct);

    public IReadOnlyList<string> ListBackups() =>
        Directory.Exists(_backupRoot)
            ? Directory.GetDirectories(_backupRoot).OrderByDescending(d => d, StringComparer.Ordinal).ToList()
            : [];

    public void Restore(string backupDir)
    {
        foreach (var file in Directory.GetFiles(backupDir, "*.reg").Order()) _reg.ImportFile(file);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private sealed record Ref(RegistryHive Hive, string Path, string? ValueName);

    private static CleanItem KeyItem(string category, string description, RegistryHive hive, string path) =>
        new($"K{Sep}{hive}{Sep}{path}", category, description, 0);

    private static CleanItem ValueItem(string category, string description, RegistryHive hive, string path, string value) =>
        new($"V{Sep}{hive}{Sep}{path}{Sep}{value}", category, description, 0);

    private static Ref? Parse(string id)
    {
        var p = id.Split(Sep);
        if (p.Length >= 3 && Enum.TryParse<RegistryHive>(p[1], out var hive))
        {
            if (p[0] == "K" && p.Length == 3) return new Ref(hive, p[2], null);
            if (p[0] == "V" && p.Length == 4) return new Ref(hive, p[2], p[3]);
        }
        return null;
    }

    private static bool IsRootedFile(string s) => s.Length > 3 && s[1] == ':' && s[2] == '\\';

    private static string? ExtractExe(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var i = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? command[..(i + 4)] : null;
    }
}
