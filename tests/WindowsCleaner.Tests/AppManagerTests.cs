using WindowsCleaner.Core;
using static Microsoft.Win32.RegistryHive;

namespace WindowsCleaner.Tests;

public class AppManagerTests
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    [Fact]
    public void List_returns_visible_apps_sorted_and_deduplicated()
    {
        var reg = new FakeRegistry();
        reg.Add(LocalMachine, Uninstall + @"\B",
            ("DisplayName", "Beta"), ("Publisher", "Acme"), ("DisplayVersion", "2.0"),
            ("EstimatedSize", 2048), ("UninstallString", @"C:\b\unins.exe"));
        reg.Add(LocalMachine, Uninstall + @"\A",
            ("DisplayName", "Alpha"), ("UninstallString", @"C:\a\unins.exe"));
        reg.Add(CurrentUser, Uninstall + @"\A2",
            ("DisplayName", "Alpha"), ("UninstallString", @"C:\a\unins.exe"));
        reg.Add(LocalMachine, Uninstall + @"\Sys",
            ("DisplayName", "Hidden"), ("SystemComponent", 1), ("UninstallString", "x"));
        reg.Add(LocalMachine, Uninstall + @"\Patch",
            ("DisplayName", "Patch"), ("ParentKeyName", "A"), ("UninstallString", "x"));
        reg.Add(LocalMachine, Uninstall + @"\NoName", ("UninstallString", "x"));
        reg.Add(LocalMachine, Uninstall + @"\NoUninstaller", ("DisplayName", "Stuck"));

        var apps = new AppManager(reg).List();

        Assert.Equal(["Alpha", "Beta"], apps.Select(a => a.Name).ToArray());
        var beta = apps.Single(a => a.Name == "Beta");
        Assert.Equal("Acme", beta.Publisher);
        Assert.Equal("2.0", beta.Version);
        Assert.Equal(2048L * 1024, beta.SizeBytes);
    }
}
