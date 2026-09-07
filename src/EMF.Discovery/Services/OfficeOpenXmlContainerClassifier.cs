using System.IO.Compression;

namespace EMF.Discovery.Services;

public sealed class OfficeOpenXmlContainerClassifier
{
    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;

    private readonly long _maxInputBytes;

    public OfficeOpenXmlContainerClassifier(
        long maxInputBytes = DefaultMaxInputBytes)
    {
        if (maxInputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInputBytes));
        }

        _maxInputBytes = maxInputBytes;
    }

    public string? Classify(
        ReadOnlySpan<byte> content)
    {
        if (content.Length > _maxInputBytes)
        {
            throw new InvalidDataException(
                "Office container input exceeds the maximum allowed size.");
        }

        using var stream =
            new MemoryStream(content.ToArray());

        using var archive =
            new ZipArchive(
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
