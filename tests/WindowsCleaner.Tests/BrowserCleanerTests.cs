using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class BrowserCleanerTests
{
    [Fact]
    public async Task Scan_and_clean_touch_only_selected_browser_data()
    {
        using var t = new TempDir();
        var local = t.Path;
        var roaming = Path.Combine(t.Path, "roaming");
        var cache = t.Write(@"Google\Chrome\User Data\Default\Cache\f1", "12345");
        var codeCache = t.Write(@"Google\Chrome\User Data\Profile 1\Code Cache\f2", "123");
        var cookies = t.Write(@"Google\Chrome\User Data\Default\Network\Cookies");
        var history = t.Write(@"Google\Chrome\User Data\Default\History");
        var crashpad = t.Write(@"Google\Chrome\User Data\Crashpad\c1");
        var ffCache = t.Write(@"Mozilla\Firefox\Profiles\abc.default\cache2\entries\e1", "12");
        var ffCookies = t.Write(@"roaming\Mozilla\Firefox\Profiles\abc.default\cookies.sqlite");

        var c = BrowserCleaner.Create(local, roaming, _ => false);
        var items = await c.ScanAsync();

        var chromeCache = items.Single(i => i.Id == "chrome.cache");
        Assert.Equal(8, chromeCache.SizeBytes);
        Assert.True(chromeCache.SelectedByDefault);
        Assert.False(items.Single(i => i.Id == "chrome.cookies").SelectedByDefault);
        Assert.False(items.Single(i => i.Id == "chrome.history").SelectedByDefault);
        Assert.Contains(items, i => i.Id == "firefox.cache");

        await c.CleanAsync(items.Where(i => i.Id is "chrome.cache" or "firefox.cache"));

        Assert.False(File.Exists(cache));
        Assert.False(File.Exists(codeCache));
        Assert.False(File.Exists(ffCache));
        Assert.True(File.Exists(cookies));
        Assert.True(File.Exists(history));
        Assert.True(File.Exists(crashpad));
        Assert.True(File.Exists(ffCookies));
    }

    [Fact]
    public async Task Running_browser_is_flagged_not_cleaned()
    {
        using var t = new TempDir();
        var cache = t.Write(@"Microsoft\Edge\User Data\Default\Cache\f1");
        var c = BrowserCleaner.Create(t.Path, Path.Combine(t.Path, "roaming"), n => n == "msedge");
        var item = Assert.Single(await c.ScanAsync());
        Assert.Equal("edge.cache", item.Id);
        Assert.False(item.SelectedByDefault);
        await c.CleanAsync([item]);
        Assert.True(File.Exists(cache));
    }
}
