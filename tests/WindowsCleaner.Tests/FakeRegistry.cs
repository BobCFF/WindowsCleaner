using Microsoft.Win32;
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public sealed class FakeRegistry : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Exported { get; } = [];
    public List<string> Imported { get; } = [];
    public bool FailExport { get; set; }

    private static string K(RegistryHive h, string p) => $"{h}\\{p}";

    public void Add(RegistryHive h, string path, params (string name, object value)[] values)
    {
        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v) in values) d[n] = v;
        _keys[K(h, path)] = d;
    }

    public bool KeyExists(RegistryHive h, string path)
    {
        var k = K(h, path);
        return _keys.ContainsKey(k) || _keys.Keys.Any(x => x.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive h, string path)
    {
        var prefix = K(h, path) + "\\";
        return _keys.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(x => x[prefix.Length..].Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> GetValueNames(RegistryHive h, string path) =>
        _keys.TryGetValue(K(h, path), out var d) ? d.Keys.ToList() : [];

    public object? GetValue(RegistryHive h, string path, string name) =>
        _keys.TryGetValue(K(h, path), out var d) && d.TryGetValue(name, out var v) ? v : null;

    public void SetValue(RegistryHive h, string path, string name, object value, RegistryValueKind kind)
    {
        if (!_keys.TryGetValue(K(h, path), out var d)) _keys[K(h, path)] = d = new(StringComparer.OrdinalIgnoreCase);
        d[name] = value;
    }

    public void DeleteSubKeyTree(RegistryHive h, string path)
    {
        var k = K(h, path);
        foreach (var key in _keys.Keys.Where(x => x.Equals(k, StringComparison.OrdinalIgnoreCase) ||
                                                  x.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
            _keys.Remove(key);
    }

    public void DeleteValue(RegistryHive h, string path, string name)
    {
        if (_keys.TryGetValue(K(h, path), out var d)) d.Remove(name);
    }

    public void ExportKey(RegistryHive h, string path, string file)
    {
        if (FailExport) throw new IOException("export failed");
        File.WriteAllText(file, "Windows Registry Editor Version 5.00");
        Exported.Add(K(h, path));
    }

    public void ImportFile(string file) => Imported.Add(file);
}
