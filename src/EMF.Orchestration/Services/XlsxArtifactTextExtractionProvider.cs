using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class XlsxArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const int DefaultMaxPackageEntryCount = 1000;
    public const long DefaultMaxPackageEntryBytes = 25L * 1024 * 1024;
    public const long DefaultMaxPackageTotalBytes = 100L * 1024 * 1024;
    public const int DefaultMaxWorksheetCount = 1000;
    public const int DefaultMaxRowCount = 1_000_000;
    public const int DefaultMaxCellCount = 1_000_000;
    public const int DefaultMaxSharedStringCount = 1_000_000;
    public const int DefaultMaxExtractedTextChars = 10 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly int _maxPackageEntryCount;
    private readonly long _maxPackageEntryBytes;
    private readonly long _maxPackageTotalBytes;
    private readonly int _maxWorksheetCount;
    private readonly int _maxRowCount;
    private readonly int _maxCellCount;
    private readonly int _maxSharedStringCount;
    private readonly int _maxExtractedTextChars;

    public XlsxArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        int maxPackageEntryCount = DefaultMaxPackageEntryCount,
        long maxPackageEntryBytes = DefaultMaxPackageEntryBytes,
        long maxPackageTotalBytes = DefaultMaxPackageTotalBytes,
        int maxWorksheetCount = DefaultMaxWorksheetCount,
        int maxRowCount = DefaultMaxRowCount,
        int maxCellCount = DefaultMaxCellCount,
        int maxSharedStringCount = DefaultMaxSharedStringCount,
        int maxExtractedTextChars = DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        ValidatePositive(
            maxPackageEntryCount,
            nameof(maxPackageEntryCount));
        ValidatePositive(
            maxPackageEntryBytes,
            nameof(maxPackageEntryBytes));
        ValidatePositive(
            maxPackageTotalBytes,
            nameof(maxPackageTotalBytes));
        ValidatePositive(
            maxWorksheetCount,
            nameof(maxWorksheetCount));
        ValidatePositive(
            maxRowCount,
            nameof(maxRowCount));
        ValidatePositive(
            maxCellCount,
            nameof(maxCellCount));
        ValidatePositive(
            maxSharedStringCount,
            nameof(maxSharedStringCount));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _maxPackageEntryCount = maxPackageEntryCount;
        _maxPackageEntryBytes = maxPackageEntryBytes;
        _maxPackageTotalBytes = maxPackageTotalBytes;
        _maxWorksheetCount = maxWorksheetCount;
        _maxRowCount = maxRowCount;
        _maxCellCount = maxCellCount;
        _maxSharedStringCount = maxSharedStringCount;
        _maxExtractedTextChars = maxExtractedTextChars;
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            ContentType,
            StringComparison.OrdinalIgnoreCase);

    public async Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var content = await _contentStore.ReadAsync(
            artifactId,
            cancellationToken);

        if (content is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        ValidatePackage(content);

        using var stream = new MemoryStream(
            content,
            writable: false);

        using var document =
            SpreadsheetDocument.Open(stream, false);

        var workbookPart = document.WorkbookPart;

        if (workbookPart is null)
            return string.Empty;

        var workbook = workbookPart.Workbook;

        if (workbook is null)
            return string.Empty;

        var sheets = workbook.Sheets;

        if (sheets is null)
            return string.Empty;

        var sharedStrings =
            workbookPart.SharedStringTablePart?
                .SharedStringTable?
                .Elements<SharedStringItem>()
                .Select(item => item.InnerText)
                .Take(_maxSharedStringCount + 1)
                .ToArray()
            ?? [];

        if (sharedStrings.Length > _maxSharedStringCount)
        {
            throw new InvalidDataException(
                "XLSX shared-string table exceeds the maximum allowed count.");
        }

        var output = new StringBuilder();
        var worksheetCount = 0;
        var rowCount = 0;
        var cellCount = 0;

        foreach (var sheet in sheets.Elements<Sheet>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            worksheetCount++;

            if (worksheetCount > _maxWorksheetCount)
            {
                throw new InvalidDataException(
                    "XLSX exceeds the maximum allowed worksheet count.");
            }

            var relationshipId = sheet.Id?.Value;

            if (string.IsNullOrWhiteSpace(relationshipId))
                continue;

            var worksheetPart =
                (WorksheetPart)workbookPart.GetPartById(
                    relationshipId);

            AppendLine(
                output,
                $"[Worksheet: {sheet.Name?.Value ?? "(unnamed)"}]");

            var worksheet = worksheetPart.Worksheet;

            if (worksheet is null)
                continue;

            var sheetData =
                worksheet.GetFirstChild<SheetData>();

            if (sheetData is null)
                continue;

            var rows = sheetData.Elements<Row>();

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                rowCount++;

                if (rowCount > _maxRowCount)
                {
                    throw new InvalidDataException(
                        "XLSX exceeds the maximum allowed row count.");
                }

                foreach (var cell in row.Elements<Cell>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    cellCount++;

                    if (cellCount > _maxCellCount)
                    {
                        throw new InvalidDataException(
                            "XLSX exceeds the maximum allowed cell count.");
                    }

                    var value = GetCellText(
                        cell,
                        sharedStrings);

                    if (string.IsNullOrEmpty(value))
                        continue;

                    AppendLine(
                        output,
                        $"{cell.CellReference?.Value}: {value}");
                }
            }
        }

        return output.ToString();
    }

    private void AppendLine(
        StringBuilder builder,
        string value)
    {
        var requiredCharacters =
            value.Length +
            Environment.NewLine.Length;

        if (requiredCharacters >
            _maxExtractedTextChars - builder.Length)
        {
            throw new InvalidDataException(
                "XLSX extracted text exceeds the maximum allowed size.");
        }

        builder.AppendLine(value);
    }

    private static string? GetCellText(
        Cell cell,
        IReadOnlyList<string> sharedStrings)
    {
        var value = cell.CellValue?.Text;

        if (value is null)
            return cell.InlineString?.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(value, out var index))
        {
            return index >= 0 &&
                index < sharedStrings.Count
                    ? sharedStrings[index]
                    : null;
        }

        return value;
    }
    private static void ValidatePositive(
        long value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "XLSX extraction limits must be positive.");
        }
    }

    private void ValidatePackage(byte[] content)
    {
        using var stream =
            new MemoryStream(content, writable: false);

        using var archive =
            new ZipArchive(
                stream,
                ZipArchiveMode.Read,
                leaveOpen: false);

        if (archive.Entries.Count > _maxPackageEntryCount)
        {
            throw new InvalidDataException(
                "XLSX package exceeds the maximum allowed entry count.");
        }

        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.Length > _maxPackageEntryBytes)
            {
                throw new InvalidDataException(
                    "XLSX package entry exceeds the maximum allowed size.");
            }

            if (entry.Length >
                _maxPackageTotalBytes - totalBytes)
            {
                throw new InvalidDataException(
                    "XLSX package exceeds the maximum allowed extracted size.");
            }

            totalBytes += entry.Length;
        }
    }

}
