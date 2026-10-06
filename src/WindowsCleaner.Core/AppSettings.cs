using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsCleaner.Core;

public enum NavPosition { Left, Top }

/// <summary>Reads NavPosition as a name or number; anything unknown/invalid becomes Left.</summary>
public sealed class NavPositionConverter : JsonConverter<NavPosition>
{
    public override NavPosition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = NavPosition.Left;
        if (reader.TokenType == JsonTokenType.String)
        {
            if (Enum.TryParse<NavPosition>(reader.GetString(), true, out var v) && Enum.IsDefined(v)) result = v;
        }
        else if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var n) && Enum.IsDefined(typeof(NavPosition), n)) result = (NavPosition)n;
        }
        else
        {
            reader.Skip(); // objects/arrays: consume the value; scalars (true/false/null) are already consumed
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, NavPosition value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public sealed class AppSettings
{
    public int LogRetentionDays { get; set; } = 7;

    [JsonConverter(typeof(NavPositionConverter))]
    public NavPosition NavPosition { get; set; } = NavPosition.Left;

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
