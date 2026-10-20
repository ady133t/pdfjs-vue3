using System.Text.RegularExpressions;

namespace PdfConverter.Api;

public static partial class FileNames
{
    /// <summary>Strips a PDF extension and unsafe characters from an uploaded name.</summary>
    public static string BaseName(string? name, string fallback = "document")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var fileName = Path.GetFileName(name);
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var cleaned = InvalidFileNameChars().Replace(withoutExtension, "-").Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned[..Math.Min(cleaned.Length, 100)];
    }

    /// <summary>Turns a label into a safe, unique Excel worksheet name.</summary>
    public static string SheetName(string label, ISet<string> used)
    {
        var cleaned = InvalidSheetChars().Replace(label, " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "Table";
        }

        cleaned = cleaned[..Math.Min(cleaned.Length, 31)];
        var candidate = cleaned;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            var tail = $" ({suffix++})";
            var head = cleaned[..Math.Min(cleaned.Length, 31 - tail.Length)].TrimEnd();
            candidate = head + tail;
        }

        return candidate;
    }

    [GeneratedRegex("[<>:\"/\\\\|?*\\x00-\\x1F]")]
    private static partial Regex InvalidFileNameChars();

    [GeneratedRegex("[\\[\\]:*?/\\\\]")]
    private static partial Regex InvalidSheetChars();
}
