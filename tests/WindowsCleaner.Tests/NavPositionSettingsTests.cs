using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class NavPositionSettingsTests
{
    private static string Write(TempDir t, string json)
    {
        var path = Path.Combine(t.Path, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void NavPosition_defaults_to_Left()
    {
        using var t = new TempDir();
        Assert.Equal(NavPosition.Left, new AppSettings().NavPosition);
        Assert.Equal(NavPosition.Left, AppSettings.Load(Path.Combine(t.Path, "missing.json")).NavPosition);
        Assert.Equal(NavPosition.Left, AppSettings.Load(Write(t, "{\"LogRetentionDays\":5}")).NavPosition);
    }

    [Fact]
    public void NavPosition_Top_roundtrips()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "settings.json");
        new AppSettings { NavPosition = NavPosition.Top }.Save(path);
        Assert.Equal(NavPosition.Top, AppSettings.Load(path).NavPosition);
    }

    [Theory]
    [InlineData("\"Sideways\"")]
    [InlineData("99")]
    [InlineData("-3")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    public void NavPosition_invalid_loads_as_Left_and_keeps_other_settings(string value)
    {
        using var t = new TempDir();
        var s = AppSettings.Load(Write(t, $"{{\"LogRetentionDays\":30,\"NavPosition\":{value}}}"));
        Assert.Equal(NavPosition.Left, s.NavPosition);
        Assert.Equal(30, s.LogRetentionDays);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9999, 365)]
    public void LogRetentionDays_clamp_still_works_with_NavPosition(int input, int expected)
    {
        using var t = new TempDir();
        var s = AppSettings.Load(Write(t, $"{{\"LogRetentionDays\":{input},\"NavPosition\":\"Top\"}}"));
        Assert.Equal(expected, s.LogRetentionDays);
        Assert.Equal(NavPosition.Top, s.NavPosition);
    }
}
