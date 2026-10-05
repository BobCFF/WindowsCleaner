using System.Diagnostics;
using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed class RegistryAccess : IRegistryAccess
{
    private static RegistryKey Base(RegistryHive h) => RegistryKey.OpenBaseKey(h, RegistryView.Registry64);

    public bool KeyExists(RegistryHive hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path);
        return k is not null;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path);
        return k?.GetSubKeyNames() ?? [];
    }

    public IReadOnlyList<string> GetValueNames(RegistryHive hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path);
        return k?.GetValueNames() ?? [];
    }

    public object? GetValue(RegistryHive hive, string path, string name)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path);
        return k?.GetValue(name);
    }

    public void SetValue(RegistryHive hive, string path, string name, object value, RegistryValueKind kind)
    {
        using var b = Base(hive);
        using var k = b.CreateSubKey(path);
        k.SetValue(name, value, kind);
    }

    public void DeleteSubKeyTree(RegistryHive hive, string path)
    {
        using var b = Base(hive);
        b.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    public void DeleteValue(RegistryHive hive, string path, string name)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void ExportKey(RegistryHive hive, string path, string file) =>
        RunReg("export", $"{Short(hive)}\\{path}", file, "/y");

    public void ImportFile(string file) => RunReg("import", file);

    private static string Short(RegistryHive h) => h switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.ClassesRoot => "HKCR",
        _ => throw new NotSupportedException(h.ToString()),
    };

    private static void RunReg(params string[] args)
    {
        var psi = new ProcessStartInfo("reg.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"reg.exe {args[0]} failed: {err.Trim()}");
    }
}
