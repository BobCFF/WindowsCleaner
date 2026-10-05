using System.Diagnostics;
using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed record InstalledApp(string Name, string Publisher, string Version, string InstallDate, long SizeBytes, string UninstallString);

public sealed class AppManager(IRegistryAccess reg)
{
    private static readonly string[] Paths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public IReadOnlyList<InstalledApp> List()
    {
        var apps = new List<InstalledApp>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var basePath in Paths)
        foreach (var sub in reg.GetSubKeyNames(hive, basePath))
        {
            var p = $"{basePath}\\{sub}";
            string S(string n) => reg.GetValue(hive, p, n) as string ?? "";
            var name = S("DisplayName");
            var uninstall = S("UninstallString");
            if (name.Length == 0 || uninstall.Length == 0) continue;
            if (reg.GetValue(hive, p, "SystemComponent") is 1) continue;
            if (S("ParentKeyName").Length > 0) continue;
            var kb = reg.GetValue(hive, p, "EstimatedSize") is int i ? i : 0;
            apps.Add(new InstalledApp(name, S("Publisher"), S("DisplayVersion"), S("InstallDate"), kb * 1024L, uninstall));
        }
        return apps
            .GroupBy(a => (a.Name, a.Version))
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Launches the app's own uninstaller. The extra outer quotes stop cmd.exe from stripping the
    /// first and last quote of a quoted path.</summary>
    public void Uninstall(InstalledApp app) =>
        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c \"{app.UninstallString}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
}
