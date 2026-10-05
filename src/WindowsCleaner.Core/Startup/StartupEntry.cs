namespace WindowsCleaner.Core;

public sealed record StartupEntry(string Id, string Name, string Command, string Source, bool Enabled);

public interface IStartupSource
{
    string Name { get; }
    IReadOnlyList<StartupEntry> List();
    void SetEnabled(StartupEntry entry, bool enabled);
}
