namespace WindowsCleaner.Core;

public sealed class StartupManager
{
    private readonly IReadOnlyList<IStartupSource> _sources;

    public StartupManager(IEnumerable<IStartupSource> sources) => _sources = sources.ToList();

    public static StartupManager CreateDefault(IRegistryAccess reg) => new(
    [
        new RunKeyStartupSource(reg),
        new StartupFolderSource(reg,
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)),
        new TaskStartupSource(),
    ]);

    public IReadOnlyList<StartupEntry> List() =>
        _sources.SelectMany(s => s.List()).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public void SetEnabled(StartupEntry entry, bool enabled) =>
        _sources.Single(s => s.Name == entry.Source).SetEnabled(entry, enabled);
}
