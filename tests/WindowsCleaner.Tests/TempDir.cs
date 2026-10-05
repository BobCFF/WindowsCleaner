using System.Diagnostics;

namespace WindowsCleaner.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-test-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string Write(string relPath, string content = "x", DateTime? lastWriteUtc = null)
    {
        var full = System.IO.Path.Combine(Path, relPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        if (lastWriteUtc is { } t) File.SetLastWriteTimeUtc(full, t);
        return full;
    }

    public static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
        psi.Arguments = $"/c mklink /J \"{link}\" \"{target}\"";
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("mklink /J failed");
    }

    public void Dispose()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories)
                         .Where(d => new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList())
                Directory.Delete(d); // removes the junction itself, never its target
            Directory.Delete(Path, true);
        }
        catch { /* best effort */ }
    }
}
