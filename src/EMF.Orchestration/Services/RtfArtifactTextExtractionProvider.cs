using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using OfficeIMO.Rtf;

namespace EMF.Orchestration.Services;

public sealed class RtfArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/rtf";

    private readonly IArtifactContentStore _contentStore;

    public RtfArtifactTextExtractionProvider(
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

        var result =
            RtfDocument.Load(
                content,
                options: null);

        var builder = new StringBuilder();

        foreach (var paragraph in result.Document.Paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var run in paragraph.Runs)
                builder.Append(run.Text);

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }
}
