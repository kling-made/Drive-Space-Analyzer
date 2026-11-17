using System.Globalization;

namespace DriveSpaceAnalyzer.Core;

public static class SizeUnits
{
    public static readonly string[] InputUnits = ["MB", "GB", "TB"];
    public static bool TryParse(string text, string unit, out long bytes, out string error,
        CultureInfo? culture = null)
    {
        bytes = 0; error = "Enter a finite, non-negative number within the 64-bit byte range.";
        if (!decimal.TryParse(text, NumberStyles.Float, culture ?? CultureInfo.CurrentCulture, out var value)
            || value < 0) return false;
        var factor = unit switch { "MB" => 1_000_000m, "GB" => 1_000_000_000m, "TB" => 1_000_000_000_000m, _ => 0m };
        if (factor == 0) { error = "Choose MB, GB, or TB."; return false; }
        try
        {
            var product = checked(value * factor);
            if (product > long.MaxValue) return false;
            bytes = (long)decimal.Floor(product); error = ""; return true;
        }
        catch (OverflowException) { return false; }
    }
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        double size = bytes; var index = 0;
        while (size >= 1000 && index < units.Length - 1) { size /= 1000; index++; }
        return $"{size:0.##} {units[index]}";
    }
    public static string Time(TimeSpan time) => $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
}
