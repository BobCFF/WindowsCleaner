using Microsoft.Win32;

namespace WindowsCleaner.Core;

/// <summary>Reads/writes the Explorer "StartupApproved" binary flags (first byte even = enabled, odd = disabled).
/// This is the same reversible switch Task Manager uses; nothing is deleted.</summary>
internal static class StartupApproval
{
    public const string BasePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static bool IsEnabled(IRegistryAccess reg, RegistryHive hive, string approvedPath, string name) =>
        reg.GetValue(hive, approvedPath, name) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;

    public static void Set(IRegistryAccess reg, RegistryHive hive, string approvedPath, string name, bool enabled)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        reg.SetValue(hive, approvedPath, name, data, RegistryValueKind.Binary);
    }
}
