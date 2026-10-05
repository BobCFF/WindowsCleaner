using Microsoft.Win32;

namespace WindowsCleaner.Core;

/// <summary>Registry operations the cleaners need. <c>name == ""</c> addresses a key's default value.</summary>
public interface IRegistryAccess
{
    bool KeyExists(RegistryHive hive, string path);
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string path);
    IReadOnlyList<string> GetValueNames(RegistryHive hive, string path);
    object? GetValue(RegistryHive hive, string path, string name);
    void SetValue(RegistryHive hive, string path, string name, object value, RegistryValueKind kind);
    void DeleteSubKeyTree(RegistryHive hive, string path);
    void DeleteValue(RegistryHive hive, string path, string name);
    void ExportKey(RegistryHive hive, string path, string file);
    void ImportFile(string file);
}
