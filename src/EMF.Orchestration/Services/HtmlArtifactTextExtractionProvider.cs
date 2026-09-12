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
    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxTokenCount = 1_000_000;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly long _maxInputBytes;
    private readonly int _maxTokenCount;
    private readonly int _maxExtractedTextChars;

    public HtmlArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        long maxInputBytes =
            DefaultMaxInputBytes,
        int maxTokenCount =
            DefaultMaxTokenCount,
        int maxExtractedTextChars =
            DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ValidatePositive(
            maxInputBytes,
            nameof(maxInputBytes));
        ValidatePositive(
            maxTokenCount,
            nameof(maxTokenCount));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _maxInputBytes = maxInputBytes;
        _maxTokenCount = maxTokenCount;
        _maxExtractedTextChars = maxExtractedTextChars;
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

        cancellationToken.ThrowIfCancellationRequested();

        if (content.LongLength > _maxInputBytes)
        {
            throw new InvalidDataException(
                "HTML input exceeds the maximum allowed size.");
        }

        var preferred = FindPreferredContainer(content, cancellationToken);

        using var stream = new MemoryStream(content, writable: false);
        var tokenizer = new HtmlTokenizer(stream, Encoding.UTF8);
        var builder = new StringBuilder();
        var targetDepth = preferred is null ? 1 : 0;
        var suppressedDepth = 0;
        var tokenCount = 0;

        while (tokenizer.ReadNextToken(out var token))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tokenCount++ >= _maxTokenCount)
                throw new InvalidDataException(
                    "HTML input exceeds the maximum allowed token count.");

            if (token is HtmlTagToken tag)
            {
                if (preferred is not null &&
                    tag.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                {
                    targetDepth += tag.IsEndTag ? -1 : 1;
                    continue;
                }

                if (IsSuppressedTag(tag.Name))
                {
                    suppressedDepth += tag.IsEndTag ? -1 : 1;
                    suppressedDepth = Math.Max(0, suppressedDepth);
                    continue;
                }

                if (targetDepth > 0 && suppressedDepth == 0 &&
                    IsBlockTag(tag.Name))
                    AppendBounded(builder, "\n");

                continue;
            }

            if (targetDepth > 0 && suppressedDepth == 0 &&
                token is HtmlDataToken data)
                AppendBounded(builder, WebUtility.HtmlDecode(data.Data));
        }

        return CleanExtractedText(builder.ToString());
    }

    private string? FindPreferredContainer(
        byte[] content,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        var tokenizer = new HtmlTokenizer(stream, Encoding.UTF8);
        var hasMain = false;
        var hasBody = false;
        var tokenCount = 0;

        while (tokenizer.ReadNextToken(out var token))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tokenCount++ >= _maxTokenCount)
                throw new InvalidDataException(
                    "HTML input exceeds the maximum allowed token count.");

            if (token is not HtmlTagToken tag || tag.IsEndTag)
                continue;

            if (tag.Name.Equals("article", StringComparison.OrdinalIgnoreCase))
                return "article";

            hasMain |= tag.Name.Equals("main", StringComparison.OrdinalIgnoreCase);
            hasBody |= tag.Name.Equals("body", StringComparison.OrdinalIgnoreCase);
        }

        return hasMain ? "main" : hasBody ? "body" : null;
    }

    private static bool IsSuppressedTag(string name) =>
        name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("nav", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("header", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("footer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("aside", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("form", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("noscript", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("svg", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockTag(string name) =>
        name.Equals("p", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("br", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("div", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("section", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("li", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("tr", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("h1", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("h2", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("h3", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("h4", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("h5", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("h6", StringComparison.OrdinalIgnoreCase);

    private static string CleanExtractedText(string value)
    {
        var cleaned = NormalizeText(value);

        var abstractIndex =
            cleaned.IndexOf(
                "Abstract",
                StringComparison.OrdinalIgnoreCase);

        if (abstractIndex > 0 && abstractIndex <= 4096)
        {
            var prefix = cleaned[..abstractIndex];

            if (prefix.Contains(
                    "Download PDF",
                    StringComparison.OrdinalIgnoreCase) ||
                prefix.Contains(
                    "Outline",
                    StringComparison.OrdinalIgnoreCase) ||
                prefix.Contains(
                    "Get Rights",
                    StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[abstractIndex..];
            }
        }

        cleaned =
            cleaned.Replace(
                "Open table in a new tab",
                string.Empty,
                StringComparison.OrdinalIgnoreCase);

        var metricsIndex =
            cleaned.IndexOf(
                "Article metrics",
                StringComparison.OrdinalIgnoreCase);

        if (metricsIndex >= 0)
            cleaned = cleaned[..metricsIndex];

        return NormalizeText(cleaned);
    }

    private static string NormalizeText(string value) =>
        string.Join(
            Environment.NewLine,
            value.Replace("\r", string.Empty)
                .Split('\n')
                .Select(line => string.Join(
                    " ",
                    line.Split((char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries)))
                .Where(line => line.Length > 0));

    private void AppendBounded(
        StringBuilder builder,
        string value)
    {
        if (value.Length >
            _maxExtractedTextChars - builder.Length)
        {
            throw new InvalidDataException(
                "HTML extracted text exceeds " +
                "the maximum allowed size.");
        }

        builder.Append(value);
    }

    private static void ValidatePositive(
        long value,
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
