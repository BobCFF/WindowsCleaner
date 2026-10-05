using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed class StartupFolderSource(IRegistryAccess reg, string userFolder, string commonFolder) : IStartupSource
{
    public const string SourceName = "Startup folder";
    private const char Sep = '\t';
    private const string Approved = StartupApproval.BasePath + "StartupFolder";

    public string Name => SourceName;

    public IReadOnlyList<StartupEntry> List()
    {
        var result = new List<StartupEntry>();
        foreach (var (hive, folder) in new[] { (RegistryHive.CurrentUser, userFolder), (RegistryHive.LocalMachine, commonFolder) })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new StartupEntry($"{hive}{Sep}{file}", name, file, SourceName,
                    StartupApproval.IsEnabled(reg, hive, Approved, name)));
            }
        }
        return result;
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        var p = entry.Id.Split(Sep);
        StartupApproval.Set(reg, Enum.Parse<RegistryHive>(p[0]), Approved, Path.GetFileName(p[1]), enabled);
    }
}
