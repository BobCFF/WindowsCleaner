using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class SettingsCompatTests
{
    [Theory]
    [InlineData("\"Top\"")]
    [InlineData("\"Sideways\"")]
    [InlineData("99")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Legacy_NavPosition_is_ignored_and_other_settings_kept(string value)
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "settings.json");
        File.WriteAllText(path, $"{{\"LogRetentionDays\":30,\"NavPosition\":{value}}}");
        Assert.Equal(30, AppSettings.Load(path).LogRetentionDays);
    }

    [Fact]
    public void Saved_settings_no_longer_contain_NavPosition()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "settings.json");
        new AppSettings().Save(path);
        Assert.DoesNotContain("NavPosition", File.ReadAllText(path));
    }
}
