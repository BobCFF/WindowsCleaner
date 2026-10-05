namespace WindowsCleaner.Core;

public static class FileEnumerator
{
    // Skipping ReparsePoint means junction/symlink directories and files are neither listed nor entered.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchType = MatchType.Win32,
    };

    public static IEnumerable<string> Files(string root, string pattern = "*", Func<FileInfo, bool>? filter = null)
    {
        if (!Directory.Exists(root)) return [];
        return new DirectoryInfo(root).EnumerateFiles(pattern, Options)
            .Where(f => filter?.Invoke(f) ?? true)
            .Select(f => f.FullName);
    }

    public static IEnumerable<string> Single(string file) => File.Exists(file) ? [file] : [];
}
