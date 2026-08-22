using System.IO.Compression;

namespace EMF.Discovery.Services;

public sealed class OfficeOpenXmlContainerClassifier
{
    public string? Classify(
        ReadOnlySpan<byte> content)
    {
        using var stream = new MemoryStream(content.ToArray());
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read);

        var names =
            archive.Entries
                .Select(entry => entry.FullName)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        if (names.Contains("xl/workbook.xml"))
            return "XLSX";

        if (names.Contains("word/document.xml"))
            return "DOCX";

        if (names.Contains("ppt/presentation.xml"))
            return "PPTX";

        return null;
    }
}
