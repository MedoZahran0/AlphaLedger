using System.Text.RegularExpressions;

namespace AlphaLedger.Core.Security;

public static class WindowsPathSafety
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SanitizeFolderName(string? name, string fallback = "شركة")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(ch => invalid.Contains(ch) || ch < 32 ? '_' : ch).ToArray();
        var cleaned = new string(chars);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        cleaned = cleaned.TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = fallback;
        }

        if (ReservedNames.Contains(cleaned) || ReservedNames.Contains(Path.GetFileNameWithoutExtension(cleaned)))
        {
            cleaned = "_" + cleaned;
        }

        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].TrimEnd('.', ' ');
        }

        return cleaned;
    }

    public static string NormalizeComparableName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var value = Regex.Replace(name.Trim(), @"\s+", " ");
        if (value.StartsWith("ال", StringComparison.Ordinal) && value.Length > 2)
        {
            value = value[2..].Trim();
        }

        return value.Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا").Replace("ة", "ه").Replace("ى", "ي");
    }

    public static string NormalizeTaxNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, @"[\s\-]", string.Empty);
    }
}
