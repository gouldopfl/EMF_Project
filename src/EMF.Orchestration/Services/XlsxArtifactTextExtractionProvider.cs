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

    private readonly IArtifactContentStore _contentStore;

    public XlsxArtifactTextExtractionProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        _contentStore = contentStore;
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
                .SharedStringTable;

        var output = new StringBuilder();

        foreach (var sheet in sheets.Elements<Sheet>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relationshipId = sheet.Id?.Value;

            if (string.IsNullOrWhiteSpace(relationshipId))
                continue;

            var worksheetPart =
                (WorksheetPart)workbookPart.GetPartById(
                    relationshipId);

            output.AppendLine(
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

                foreach (var cell in row.Elements<Cell>())
                {
                    var value = GetCellText(
                        cell,
                        sharedStrings);

                    if (string.IsNullOrEmpty(value))
                        continue;

                    output.Append(
                        cell.CellReference?.Value);
                    output.Append(": ");
                    output.AppendLine(value);
                }
            }
        }

        return output.ToString();
    }

    private static string? GetCellText(
        Cell cell,
        SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.Text;

        if (value is null)
            return cell.InlineString?.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(value, out var index) &&
            sharedStrings is not null)
        {
            return sharedStrings
                .Elements<SharedStringItem>()
                .ElementAtOrDefault(index)?
                .InnerText;
        }

        return value;
    }
}
