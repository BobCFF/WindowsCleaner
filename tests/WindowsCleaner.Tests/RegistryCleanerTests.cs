using static Microsoft.Win32.RegistryHive;
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class RegistryCleanerTests
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string SharedDlls = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
    private const string AppPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    private static RegistryCleaner Make(FakeRegistry reg, TempDir backups, Func<string, bool>? exists = null) =>
        new(reg, exists ?? (p => p.StartsWith(@"C:\Here")), backups.Path, () => new DateTime(2026, 10, 5, 12, 0, 0));

    private static void AddOrphanUninstall(FakeRegistry reg) =>
        reg.Add(LocalMachine, Uninstall + @"\Gone",
            ("DisplayName", "Gone App"), ("UninstallString", "\"C:\\Gone\\unins.exe\" /S"));

    [Fact]
    public async Task Scan_flags_uninstall_entry_with_missing_uninstaller_only()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        AddOrphanUninstall(reg);
        reg.Add(LocalMachine, Uninstall + @"\Here",
            ("DisplayName", "Here App"), ("UninstallString", @"C:\Here\unins.exe /S"));
        reg.Add(LocalMachine, Uninstall + @"\Msi",
            ("DisplayName", "Msi App"), ("UninstallString", "MsiExec.exe /I{GUID}"));
        var item = Assert.Single(await Make(reg, b).ScanAsync());
        Assert.Contains("Gone App", item.Description);
    }

    [Fact]
    public async Task Scan_skips_unquoted_non_exe_uninstall_command()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        reg.Add(LocalMachine, Uninstall + @"\Bat",
            ("DisplayName", "Bat App"), ("UninstallString", @"C:\Program Files\App\uninst.bat /S"));
        Assert.Empty(await Make(reg, b).ScanAsync());
    }

    [Fact]
    public async Task Scan_flags_missing_shared_dll_stale_app_path_and_dead_extension()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        reg.Add(LocalMachine, SharedDlls, (@"C:\Gone\a.dll", 1), (@"C:\Here\b.dll", 1));
        reg.Add(LocalMachine, AppPaths + @"\gone.exe", ("", @"C:\Gone\gone.exe"));
        reg.Add(LocalMachine, AppPaths + @"\here.exe", ("", @"C:\Here\here.exe"));
        reg.Add(CurrentUser, @"Software\Classes\.xyz", ("", "xyzfile"));
        reg.Add(CurrentUser, @"Software\Classes\.abc", ("", "abcfile"));
        reg.Add(CurrentUser, @"Software\Classes\abcfile");
        var items = await Make(reg, b).ScanAsync();
        Assert.Equal(3, items.Count);
        Assert.Equal(["App Path", "File extension", "Shared DLL"], items.Select(i => i.Category).Order().ToArray());
    }

    [Fact]
    public async Task Clean_backs_up_then_deletes()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        AddOrphanUninstall(reg);
        var c = Make(reg, b);
        var r = await c.CleanAsync(await c.ScanAsync());
        Assert.False(reg.KeyExists(LocalMachine, Uninstall + @"\Gone"));
        Assert.Equal(1, r.Deleted);
        Assert.Single(reg.Exported);
        Assert.True(File.Exists(Path.Combine(b.Path, "20261005-120000", "001.reg")));
    }

    [Fact]
    public async Task Clean_aborts_without_deleting_when_backup_fails()
    {
        var reg = new FakeRegistry { FailExport = true };
        using var b = new TempDir();
        AddOrphanUninstall(reg);
        var c = Make(reg, b);
        var r = await c.CleanAsync(await c.ScanAsync());
        Assert.True(reg.KeyExists(LocalMachine, Uninstall + @"\Gone"));
        Assert.Equal(0, r.Deleted);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public async Task Value_items_are_deleted_and_restore_imports_every_reg_file()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        reg.Add(LocalMachine, SharedDlls, (@"C:\Gone\a.dll", 1), (@"C:\Here\b.dll", 1));
        var c = Make(reg, b);
        await c.CleanAsync(await c.ScanAsync());
        Assert.Equal(["C:\\Here\\b.dll"], reg.GetValueNames(LocalMachine, SharedDlls));

        var backup = Assert.Single(c.ListBackups());
        c.Restore(backup);
        Assert.Single(reg.Imported);
        Assert.EndsWith("001.reg", reg.Imported[0]);
    }
}
