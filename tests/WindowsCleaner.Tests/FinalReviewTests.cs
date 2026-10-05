using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class FinalReviewTests
{
    private sealed class FakeBin(long size) : IRecycleBin
    {
        public long GetSizeBytes() => size;
        public void Empty() { }
    }

    [Fact]
    public async Task Recycle_bin_item_is_not_selected_by_default()
    {
        using var t = new TempDir();
        var c = new JunkCleaner([], new SafePaths([t.Path]), new FakeBin(10));
        var item = Assert.Single(await c.ScanAsync());
        Assert.False(item.SelectedByDefault);
    }

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\", true)]
    [InlineData(@"C:\Windows\TMP", true)]
    [InlineData(@"D:\Data", false)]
    [InlineData(@"C:\Users\x", false)]
    [InlineData(@"C:\", false)]
    public void IsSafeTempFolder_checks_final_directory_name(string path, bool expected) =>
        Assert.Equal(expected, JunkCleaner.IsSafeTempFolder(path));

    [Fact]
    public void PathProbe_true_for_existing_false_for_missing_on_present_drive()
    {
        using var t = new TempDir();
        var f = t.Write("a.txt");
        Assert.True(PathProbe.Exists(f));
        Assert.True(PathProbe.Exists(t.Path));
        Assert.False(PathProbe.Exists(Path.Combine(t.Path, "missing.txt")));
    }

    [Fact]
    public void PathProbe_assumes_exists_when_drive_not_mounted()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var letter = "ZYXWVUTSRQPONMLKJIHGFED".First(c => !used.Contains(c));
        Assert.True(PathProbe.Exists($@"{letter}:\no\such\file.exe"));
    }

    [Theory]
    [InlineData("20240315", "2024-03-15")]
    [InlineData("", "")]
    [InlineData("3/15/2024", "3/15/2024")]
    [InlineData("20241399", "20241399")]
    public void InstallDateDisplay_formats(string raw, string expected) =>
        Assert.Equal(expected, new InstalledApp("n", "p", "v", raw, 0, "u").InstallDateDisplay);

    [Theory]
    [InlineData("9999", 365)]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("30", 30)]
    public void Settings_load_clamps_retention(string value, int expected)
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "s.json");
        File.WriteAllText(path, $"{{\"LogRetentionDays\": {value}}}");
        Assert.Equal(expected, AppSettings.Load(path).LogRetentionDays);
    }
}
