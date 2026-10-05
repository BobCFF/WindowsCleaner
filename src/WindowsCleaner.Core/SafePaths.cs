namespace WindowsCleaner.Core;

/// <summary>Allowlist of root folders deletions may happen under. Refuses the root itself,
/// anything outside it, and any path that passes through a reparse point (junction/symlink).</summary>
public sealed class SafePaths
{
    private readonly string[] _roots;

    public SafePaths(IEnumerable<string> roots) =>
        _roots = roots.Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r))).ToArray();

    public bool IsAllowed(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var root in _roots)
        {
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            return !HasReparsePoint(full, root);
        }
        return false;
    }

    private static bool HasReparsePoint(string full, string root)
    {
        for (var p = full; p is not null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
        {
            try
            {
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return true;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
        }
        return false;
    }
}
