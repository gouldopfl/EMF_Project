using System.Globalization;
using System.Text;

namespace EMF.ConsoleApplication;

internal static class ConsoleTextSanitizer
{
    public static string Sanitize(string? value)
    {
        if (value is null)
            return string.Empty;

        var sanitized =
            new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is '\r' or '\n' or '\t')
            {
                sanitized.Append(character);
                continue;
            }

            if (char.IsControl(character) ||
                char.GetUnicodeCategory(character) ==
                    UnicodeCategory.Format)
            {
                sanitized.Append(
                    $"\\u{(int)character:X4}");

                continue;
            }

            sanitized.Append(character);
        }

        return sanitized.ToString();
    }
}
