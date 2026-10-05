using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed class RunKeyStartupSource(IRegistryAccess reg) : IStartupSource
{
    public const string SourceName = "Run key";
    private const char Sep = '\t';
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    private static readonly (RegistryHive Hive, string Path)[] Locations =
        [(RegistryHive.CurrentUser, RunPath), (RegistryHive.LocalMachine, RunPath), (RegistryHive.LocalMachine, Run32Path)];

    public string Name => SourceName;

    private static string Approved(string runPath) =>
        StartupApproval.BasePath + (runPath == Run32Path ? "Run32" : "Run");

    public IReadOnlyList<StartupEntry> List()
    {
        var result = new List<StartupEntry>();
        foreach (var (hive, path) in Locations)
            foreach (var name in reg.GetValueNames(hive, path))
            {
                if (name.Length == 0) continue;
                var command = reg.GetValue(hive, path, name) as string ?? "";
                result.Add(new StartupEntry($"{hive}{Sep}{path}{Sep}{name}", name, command, SourceName,
                    StartupApproval.IsEnabled(reg, hive, Approved(path), name)));
            }
        return result;
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        var p = entry.Id.Split(Sep);
        StartupApproval.Set(reg, Enum.Parse<RegistryHive>(p[0]), Approved(p[1]), p[2], enabled);
    }
}
