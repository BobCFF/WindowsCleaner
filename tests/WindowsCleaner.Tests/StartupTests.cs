using Microsoft.Win32;
using WindowsCleaner.Core;
using static Microsoft.Win32.RegistryHive;

namespace WindowsCleaner.Tests;

public class StartupTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    [Fact]
    public void RunKey_lists_entries_with_enabled_state_and_toggles_via_StartupApproved()
    {
        var reg = new FakeRegistry();
        reg.Add(CurrentUser, Run, ("Foo", @"C:\foo.exe"), ("Bar", @"C:\bar.exe"));
        reg.Add(CurrentUser, ApprovedRun, ("Bar", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        var src = new RunKeyStartupSource(reg);

        var list = src.List();
        Assert.True(list.Single(e => e.Name == "Foo").Enabled);
        Assert.False(list.Single(e => e.Name == "Bar").Enabled);
        Assert.Equal(@"C:\foo.exe", list.Single(e => e.Name == "Foo").Command);

        src.SetEnabled(list.Single(e => e.Name == "Foo"), false);
        Assert.Equal(3, ((byte[])reg.GetValue(CurrentUser, ApprovedRun, "Foo")!)[0]);
        Assert.False(src.List().Single(e => e.Name == "Foo").Enabled);

        src.SetEnabled(src.List().Single(e => e.Name == "Foo"), true);
        Assert.Equal(2, ((byte[])reg.GetValue(CurrentUser, ApprovedRun, "Foo")!)[0]);
        Assert.True(src.List().Single(e => e.Name == "Foo").Enabled);
    }

    [Fact]
    public void StartupFolder_lists_files_except_desktop_ini_and_toggles()
    {
        using var t = new TempDir();
        t.Write(@"user\app.lnk");
        t.Write(@"user\desktop.ini");
        var reg = new FakeRegistry();
        var src = new StartupFolderSource(reg, Path.Combine(t.Path, "user"), Path.Combine(t.Path, "common"));

        var entry = Assert.Single(src.List());
        Assert.Equal("app.lnk", entry.Name);
        Assert.True(entry.Enabled);

        src.SetEnabled(entry, false);
        Assert.Equal(3, ((byte[])reg.GetValue(CurrentUser, ApprovedFolder, "app.lnk")!)[0]);
        Assert.False(Assert.Single(src.List()).Enabled);
    }

    [Fact]
    public void Manager_routes_toggle_to_the_owning_source()
    {
        var reg = new FakeRegistry();
        reg.Add(CurrentUser, Run, ("Foo", @"C:\foo.exe"));
        var mgr = new StartupManager([new RunKeyStartupSource(reg)]);
        var e = Assert.Single(mgr.List());
        mgr.SetEnabled(e, false);
        Assert.False(Assert.Single(mgr.List()).Enabled);
    }
}
