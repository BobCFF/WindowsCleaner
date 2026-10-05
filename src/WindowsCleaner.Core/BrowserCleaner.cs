namespace WindowsCleaner.Core;

public static class BrowserCleaner
{
    public static FileTargetCleaner CreateDefault() => Create(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static FileTargetCleaner Create(string localAppData, string appData, Func<string, bool>? isRunning = null)
    {
        var chrome = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        var edge = Path.Combine(localAppData, "Microsoft", "Edge", "User Data");
        var ffLocal = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        var ffRoaming = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");

        var targets = new List<FileTarget>();
        targets.AddRange(Chromium("chrome", "Google Chrome", "chrome", chrome));
        targets.AddRange(Chromium("edge", "Microsoft Edge", "msedge", edge));
        targets.Add(new FileTarget("firefox.cache", "Mozilla Firefox", "Firefox cache",
            () => Subdirs(ffLocal).SelectMany(p => FileEnumerator.Files(Path.Combine(p, "cache2"))),
            true, "firefox"));
        targets.Add(new FileTarget("firefox.cookies", "Mozilla Firefox", "Firefox cookies",
            () => Subdirs(ffRoaming).SelectMany(p => Singles(p, "cookies.sqlite", "cookies.sqlite-wal")),
            false, "firefox"));

        return new FileTargetCleaner("Browsers", targets, new SafePaths([chrome, edge, ffLocal, ffRoaming]), isRunning);
    }

    private static IEnumerable<FileTarget> Chromium(string id, string display, string process, string userData)
    {
        IEnumerable<string> Profiles() => Subdirs(userData).Where(d =>
        {
            var n = Path.GetFileName(d);
            return n == "Default" || n.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
        });

        yield return new FileTarget($"{id}.cache", display, $"{display} cache",
            () => Profiles().SelectMany(p => new[] { "Cache", "Code Cache", "GPUCache" }
                .SelectMany(n => FileEnumerator.Files(Path.Combine(p, n)))),
            true, process);
        yield return new FileTarget($"{id}.cookies", display, $"{display} cookies",
            () => Profiles().SelectMany(p => Singles(p, @"Network\Cookies", @"Network\Cookies-journal", "Cookies", "Cookies-journal")),
            false, process);
        yield return new FileTarget($"{id}.history", display, $"{display} history",
            () => Profiles().SelectMany(p => Singles(p, "History", "History-journal")),
            false, process);
    }

    private static IEnumerable<string> Subdirs(string dir) =>
        Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];

    private static IEnumerable<string> Singles(string dir, params string[] names) =>
        names.SelectMany(n => FileEnumerator.Single(Path.Combine(dir, n)));
}
