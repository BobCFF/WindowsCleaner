namespace WindowsCleaner.Core;

public static class PathProbe
{
    /// <summary>True if the path exists. Also true when its drive root is not present (unplugged/unmounted),
    /// so callers never flag entries as orphaned just because a drive is missing.</summary>
    public static bool Exists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return true;
        try
        {
            if (File.Exists(path) || Directory.Exists(path)) return true;
            var root = Path.GetPathRoot(path);
            return !Directory.Exists(root);
        }
        catch { return true; }
    }
}
