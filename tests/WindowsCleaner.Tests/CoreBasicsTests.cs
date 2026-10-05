using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class CoreBasicsTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    public void SizeFormat_formats(long bytes, string expected) =>
        Assert.Equal(expected, SizeFormat.Format(bytes));

    [Fact]
    public void CleanResult_Plus_adds_fields()
    {
        var r = new CleanResult(10, 1, 2, ["a"]).Plus(new CleanResult(5, 3, 4, ["b"]));
        Assert.Equal(15, r.BytesFreed);
        Assert.Equal(4, r.Deleted);
        Assert.Equal(6, r.Skipped);
        Assert.Equal(["a", "b"], r.Errors);
    }

    [Fact]
    public void Settings_roundtrip_and_default_on_missing_file()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "settings.json");
        Assert.Equal(7, AppSettings.Load(path).LogRetentionDays);
        new AppSettings { LogRetentionDays = 30 }.Save(path);
        Assert.Equal(30, AppSettings.Load(path).LogRetentionDays);
    }

    [Fact]
    public void SafePaths_allows_descendants_only()
    {
        using var t = new TempDir();
        var safe = new SafePaths([t.Path]);
        Assert.True(safe.IsAllowed(t.Write("a/b.txt")));
        Assert.False(safe.IsAllowed(t.Path));                                   // root itself
        Assert.False(safe.IsAllowed(Path.Combine(t.Path, "..", "other.txt")));  // .. escape
        Assert.False(safe.IsAllowed(Path.Combine(t.Path + "-evil", "f.txt")));  // prefix sibling
    }

    [Fact]
    public void SafePaths_refuses_paths_through_a_junction()
    {
        using var t = new TempDir();
        using var outside = new TempDir();
        outside.Write("secret.txt");
        var link = Path.Combine(t.Path, "link");
        TempDir.Junction(link, outside.Path);
        Assert.False(new SafePaths([t.Path]).IsAllowed(Path.Combine(link, "secret.txt")));
    }
}
