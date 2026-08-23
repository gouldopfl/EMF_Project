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

    private readonly IArtifactContentStore _contentStore;

    public XlsArtifactTextExtractionProvider(
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

        do
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (var column = 0;
                     column < reader.FieldCount;
                     column++)
                {
                    var value = reader.GetValue(column);

                    if (value is null)
                        continue;

                    if (builder.Length > 0)
                        builder.Append(' ');

                    builder.Append(
                        Convert.ToString(
                            value,
                            CultureInfo.InvariantCulture));
                }

                if (builder.Length > 0)
                    builder.AppendLine();
            }
        }
        while (reader.NextResult());

        return builder.ToString().TrimEnd();
    }
}
