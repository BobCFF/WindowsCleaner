using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class FileTargetCleanerTests
{
    private static FileTargetCleaner Make(TempDir root, IEnumerable<FileTarget> targets, Func<string, bool>? running = null) =>
        new("test", targets, new SafePaths([root.Path]), running);

    [Fact]
    public async Task Scan_reports_sizes_and_omits_empty_targets()
    {
        using var t = new TempDir();
        t.Write("a/one.txt", "12345");
        var c = Make(t,
        [
            new FileTarget("t1", "Cat", "Has files", () => FileEnumerator.Files(t.Path)),
            new FileTarget("t2", "Cat", "Empty", () => FileEnumerator.Files(Path.Combine(t.Path, "none"))),
        ]);
        var item = Assert.Single(await c.ScanAsync());
        Assert.Equal("t1", item.Id);
        Assert.Equal(5, item.SizeBytes);
    }

    [Fact]
    public async Task Clean_deletes_selected_targets_and_reports_freed_bytes()
    {
        using var t = new TempDir();
        var f = t.Write("a.txt", "12345");
        var keep = t.Write("keep/b.txt", "xx");
        var c = Make(t,
        [
            new FileTarget("t1", "Cat", "A", () => FileEnumerator.Files(t.Path, "a.txt")),
            new FileTarget("t2", "Cat", "B", () => FileEnumerator.Files(Path.Combine(t.Path, "keep"))),
        ]);
        var items = await c.ScanAsync();
        var r = await c.CleanAsync(items.Where(i => i.Id == "t1"));
        Assert.False(File.Exists(f));
        Assert.True(File.Exists(keep));
        Assert.Equal(1, r.Deleted);
        Assert.Equal(5, r.BytesFreed);
    }

    [Fact]
    public async Task Clean_skips_locked_files()
    {
        using var t = new TempDir();
        var f = t.Write("locked.txt");
        var c = Make(t, [new FileTarget("t1", "Cat", "A", () => FileEnumerator.Files(t.Path))]);
        var items = await c.ScanAsync();
        using var hold = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);
        var r = await c.CleanAsync(items);
        Assert.True(File.Exists(f));
        Assert.Equal(0, r.Deleted);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public async Task Clean_refuses_files_outside_the_allowlist()
    {
        using var allowed = new TempDir();
        using var other = new TempDir();
        var f = other.Write("x.txt");
        var c = Make(allowed, [new FileTarget("t1", "Cat", "A", () => FileEnumerator.Files(other.Path))]);
        var r = await c.CleanAsync([new CleanItem("t1", "Cat", "A", 1)]);
        Assert.True(File.Exists(f));
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public async Task Blocked_target_is_listed_unchecked_and_not_cleaned()
    {
        using var t = new TempDir();
        var f = t.Write("x.txt");
        var c = Make(t,
            [new FileTarget("t1", "Cat", "Cache", () => FileEnumerator.Files(t.Path), true, "chrome")],
            running: name => name == "chrome");
        var item = Assert.Single(await c.ScanAsync());
        Assert.False(item.SelectedByDefault);
        Assert.Contains("close chrome", item.Description);
        var r = await c.CleanAsync([item]);
        Assert.True(File.Exists(f));
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void Enumerator_does_not_follow_junctions()
    {
        using var t = new TempDir();
        using var outside = new TempDir();
        outside.Write("secret.txt");
        t.Write("real.txt");
        TempDir.Junction(Path.Combine(t.Path, "link"), outside.Path);
        var files = FileEnumerator.Files(t.Path).Select(Path.GetFileName).ToList();
        Assert.Equal(["real.txt"], files);
    }
}
