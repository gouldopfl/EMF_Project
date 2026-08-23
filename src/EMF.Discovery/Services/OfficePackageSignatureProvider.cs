using System.Text;
using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class OfficePackageSignatureProvider :
    IArtifactSignatureProvider
{
    public bool TryDetect(
        ReadOnlySpan<byte> content,
        out string contentType,
        out string format)
    {
        contentType = string.Empty;
        format = string.Empty;

        if (content.Length < 4 ||
            content[0] != 0x50 ||
            content[1] != 0x4B)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(content);

        if (text.Contains("application/vnd.oasis.opendocument.text",
            StringComparison.Ordinal))
            return Match(
                "application/vnd.oasis.opendocument.text",
                "ODT",
                out contentType,
                out format);

        if (text.Contains("application/vnd.oasis.opendocument.spreadsheet",
            StringComparison.Ordinal))
            return Match(
                "application/vnd.oasis.opendocument.spreadsheet",
                "ODS",
                out contentType,
                out format);

        if (text.Contains("application/vnd.oasis.opendocument.presentation",
            StringComparison.Ordinal))
            return Match(
                "application/vnd.oasis.opendocument.presentation",
                "ODP",
                out contentType,
                out format);

        foreach (var name in ReadLocalEntryNames(content))
        {
            if (name.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
                return Match(
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "DOCX",
                    out contentType,
                    out format);

            if (name.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                return Match(
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "XLSX",
                    out contentType,
                    out format);

            if (name.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))
                return Match(
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    "PPTX",
                    out contentType,
                    out format);
        }

        return false;
    }

    private static IReadOnlyList<string> ReadLocalEntryNames(
        ReadOnlySpan<byte> content)
    {
        var names = new List<string>();

        for (var i = 0; i <= content.Length - 30; i++)
        {
            if (content[i] != 0x50 ||
                content[i + 1] != 0x4B ||
                content[i + 2] != 0x03 ||
                content[i + 3] != 0x04)
            {
                continue;
            }

            var nameLength =
                content[i + 26] |
                content[i + 27] << 8;

            var start = i + 30;

            if (start + nameLength > content.Length)
                continue;

            names.Add(
                Encoding.UTF8.GetString(
                    content.Slice(start, nameLength)));
        }

        return names;
    }

    private static bool Match(
        string type,
        string name,
        out string contentType,
        out string format)
    {
        contentType = type;
        format = name;
        return true;
    }
}
