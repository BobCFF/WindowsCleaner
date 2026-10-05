using System.Globalization;

namespace WindowsCleaner.Core;

public static class SizeFormat
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : string.Create(CultureInfo.InvariantCulture, $"{v:0.#} {Units[i]}");
    }
}
