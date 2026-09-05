using System.Globalization;
using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using ExcelDataReader;

namespace EMF.Orchestration.Services;

public sealed class XlsArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.ms-excel";

    public const int DefaultMaxWorksheetCount = 1_000;
    public const int DefaultMaxRowCount = 1_000_000;
    public const int DefaultMaxCellCount = 1_000_000;
    public const int DefaultMaxExtractedTextChars =
        10 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly int _maxWorksheetCount;
    private readonly int _maxRowCount;
    private readonly int _maxCellCount;
    private readonly int _maxExtractedTextChars;

    public XlsArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        int maxWorksheetCount =
            DefaultMaxWorksheetCount,
        int maxRowCount =
            DefaultMaxRowCount,
        int maxCellCount =
            DefaultMaxCellCount,
        int maxExtractedTextChars =
            DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
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
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _maxWorksheetCount = maxWorksheetCount;
        _maxRowCount = maxRowCount;
        _maxCellCount = maxCellCount;
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
        var content =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (content is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        Encoding.RegisterProvider(
            CodePagesEncodingProvider.Instance);

        using var stream =
            new MemoryStream(content, writable: false);

        using var reader =
            ExcelReaderFactory.CreateBinaryReader(stream);

        var builder = new StringBuilder();
        var worksheetCount = 0;
        var rowCount = 0;
        var cellCount = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (worksheetCount >= _maxWorksheetCount)
            {
                throw new InvalidDataException(
                    "XLS workbook exceeds the maximum " +
                    "allowed worksheet count.");
            }

            worksheetCount++;

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (rowCount >= _maxRowCount)
                {
                    throw new InvalidDataException(
                        "XLS workbook exceeds the maximum " +
                        "allowed row count.");
                }

                rowCount++;

                for (var column = 0;
                     column < reader.FieldCount;
                     column++)
                {
                    if (cellCount >= _maxCellCount)
                    {
                        throw new InvalidDataException(
                            "XLS workbook exceeds the maximum " +
                            "allowed cell count.");
                    }

                    cellCount++;

                    var value = reader.GetValue(column);

                    if (value is null)
                        continue;

                    if (builder.Length > 0)
                        AppendBounded(builder, " ");

                    AppendBounded(
                        builder,
                        Convert.ToString(
                            value,
                            CultureInfo.InvariantCulture) ??
                        string.Empty);
                }

                if (builder.Length > 0)
                {
                    AppendBounded(
                        builder,
                        Environment.NewLine);
                }
            }
        }
        while (reader.NextResult());

        return builder.ToString().TrimEnd();
    }

    private void AppendBounded(
        StringBuilder builder,
        string value)
    {
        if (value.Length >
            _maxExtractedTextChars - builder.Length)
        {
            throw new InvalidDataException(
                "XLS extracted text exceeds " +
                "the maximum allowed size.");
        }

        builder.Append(value);
    }

    private static void ValidatePositive(
        int value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Resource limit must be greater than zero.");
        }
    }
}
