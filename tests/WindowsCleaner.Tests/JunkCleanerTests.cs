using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class JunkCleanerTests
{
    private sealed class FakeBin(long size) : IRecycleBin
    {
        public int EmptyCalls;
        public long GetSizeBytes() => size;
        public void Empty() => EmptyCalls++;
    }

    [Fact]
    public async Task Scan_includes_recycle_bin_and_clean_empties_it_when_selected()
    {
        using var t = new TempDir();
        var bin = new FakeBin(1000);
        var c = new JunkCleaner([], new SafePaths([t.Path]), bin);
        var items = await c.ScanAsync();
        var item = Assert.Single(items);
        Assert.Equal(JunkCleaner.RecycleBinId, item.Id);
        var r = await c.CleanAsync(items);
        Assert.Equal(1, bin.EmptyCalls);
        Assert.Equal(1000, r.BytesFreed);
    }

    [Fact]
    public async Task Clean_does_not_touch_recycle_bin_when_not_selected()
    {
        using var t = new TempDir();
        var bin = new FakeBin(1000);
        var c = new JunkCleaner([], new SafePaths([t.Path]), bin);
        await c.CleanAsync([]);
        Assert.Equal(0, bin.EmptyCalls);
    }

    [Fact]
    public void OlderThan_filters_by_last_write_time()
    {
        using var t = new TempDir();
        var old = new FileInfo(t.Write("old.txt", lastWriteUtc: DateTime.UtcNow.AddDays(-3)));
        var fresh = new FileInfo(t.Write("fresh.txt"));
        var filter = JunkCleaner.OlderThan(TimeSpan.FromDays(1));
        Assert.True(filter(old));
        Assert.False(filter(fresh));
    }
}
