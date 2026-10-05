namespace WindowsCleaner.Core;

public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsCleaner");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string BackupsDir => Path.Combine(DataDir, "Backups");
}
