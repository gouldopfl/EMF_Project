using System.Net;
using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using MimeKit.Text;

namespace EMF.Orchestration.Services;

public sealed class HtmlArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private readonly IArtifactContentStore _contentStore;

    public HtmlArtifactTextExtractionProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        _contentStore = contentStore;
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            "text/html",
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

        using var stream =
            new MemoryStream(content, writable: false);

        var tokenizer =
            new HtmlTokenizer(stream, Encoding.UTF8);

        var builder = new StringBuilder();
        var suppressed = false;

        while (tokenizer.ReadNextToken(out var token))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (token is HtmlTagToken tag)
            {
                if (tag.Name.Equals("script",
                        StringComparison.OrdinalIgnoreCase) ||
                    tag.Name.Equals("style",
                        StringComparison.OrdinalIgnoreCase))
                {
                    suppressed = !tag.IsEndTag;
                }

                continue;
            }

            if (!suppressed &&
                token is HtmlDataToken data)
            {
                builder.Append(
                    WebUtility.HtmlDecode(data.Data));
            }
        }

        return builder.ToString().Trim();
    }
}
