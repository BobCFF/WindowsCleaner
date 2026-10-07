using System.Text.Json;

namespace WindowsCleaner.Core;

public sealed class AppSettings
{
    public int LogRetentionDays { get; set; } = 7;

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
            s.LogRetentionDays = Math.Clamp(s.LogRetentionDays, 1, 365);
            return s;
        }
        catch { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
