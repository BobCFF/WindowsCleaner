using System.Diagnostics;

namespace WindowsCleaner.Core;

public sealed record FileTarget(
    string Id, string Category, string Description,
    Func<IEnumerable<string>> EnumerateFiles,
    bool SelectedByDefault = true,
    string? BlockedByProcess = null);

public sealed class FileTargetCleaner : ICleaner
{
    private readonly IReadOnlyList<FileTarget> _targets;
    private readonly SafePaths _safe;
    private readonly Func<string, bool> _isRunning;

    public FileTargetCleaner(string name, IEnumerable<FileTarget> targets, SafePaths safe, Func<string, bool>? isRunning = null)
    {
        Name = name;
        _targets = targets.ToList();
        _safe = safe;
        _isRunning = isRunning ?? (n => Process.GetProcessesByName(n).Length > 0);
    }

    public string Name { get; }

    public Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var items = new List<CleanItem>();
        foreach (var t in _targets)
        {
            ct.ThrowIfCancellationRequested();
            var blocked = t.BlockedByProcess is { } p && _isRunning(p);
            long size = 0;
            if (blocked)
            {
                // Don't measure locked data, but only surface the target if something is actually there.
                if (!t.EnumerateFiles().Any(_safe.IsAllowed)) continue;
            }
            else
            {
                size = t.EnumerateFiles().Where(_safe.IsAllowed).Sum(SizeOf);
                if (size == 0) continue;
            }
            var desc = blocked ? $"{t.Description} (close {t.BlockedByProcess} first)" : t.Description;
            items.Add(new CleanItem(t.Id, t.Category, desc, size, t.SelectedByDefault && !blocked));
        }
        return (IReadOnlyList<CleanItem>)items;
    }, ct);

    public Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var ids = selected.Select(i => i.Id).ToHashSet();
            long freed = 0;
            int deleted = 0, skipped = 0;
            var errors = new List<string>();
            foreach (var t in _targets.Where(t => ids.Contains(t.Id)))
            {
                if (t.BlockedByProcess is { } p && _isRunning(p))
                {
                    skipped++;
                    errors.Add($"{t.Description}: {p} is running");
                    continue;
                }
                progress?.Report(t.Description);
                foreach (var file in t.EnumerateFiles().ToList())
                {
                    ct.ThrowIfCancellationRequested();
                    if (!_safe.IsAllowed(file)) { skipped++; continue; }
                    try
                    {
                        var size = SizeOf(file);
                        File.Delete(file);
                        freed += size;
                        deleted++;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            return new CleanResult(freed, deleted, skipped, errors);
        }, ct);

    private static long SizeOf(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
