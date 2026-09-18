using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.ConsoleApplication;

internal sealed class EcfrVeteransReviewerRegulatoryTextProvider :
    IVeteransReviewerRegulatoryTextProvider
{
    private const int MaxTitlesResponseBytes = 1_048_576;
    private const int MaxSectionResponseBytes = 2_097_152;
    private const string BaseUri = "https://www.ecfr.gov";

    private static readonly Regex CitationPattern =
        new(
            @"^\s*(?<title>\d+)\s+C\.?\s*F\.?\s*R\.?\s*(?:§+\s*)?" +
            @"(?<section>\d+(?:\.\d+)+)\s*" +
            @"(?<subsection>\([A-Za-z]\))?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern =
        new(
            @"\s+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public EcfrVeteransReviewerRegulatoryTextProvider(
        HttpClient? httpClient = null)
    {
        _httpClient =
            httpClient ??
            new HttpClient(
                new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.All
                })
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
    }

    public async Task<IReadOnlyList<VeteransReviewerApplicableRegulation>>
        GetCurrentAsync(
            IReadOnlyList<string> citations,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(citations);
        cancellationToken.ThrowIfCancellationRequested();

        var parsed =
            citations
                .Select(ParseCitation)
                .DistinctBy(
                    item => item.Citation,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item.Citation, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (parsed.Length == 0)
            return [];

        var titleDates =
            await GetCurrentTitleDatesAsync(
                parsed.Select(item => item.Title).Distinct().ToArray(),
                cancellationToken);

        var retrievedUtc = DateTimeOffset.UtcNow;
        var result = new List<VeteransReviewerApplicableRegulation>();

        foreach (var group in parsed.GroupBy(
            item => (item.Title, item.Section)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!titleDates.TryGetValue(group.Key.Title, out var currentDate))
            {
                throw new InvalidOperationException(
                    $"eCFR Title {group.Key.Title} currency metadata was not found.");
            }

            var section =
                await GetSectionAsync(
                    group.Key.Title,
                    group.Key.Section,
                    currentDate,
                    cancellationToken);

            foreach (var citation in group)
            {
                var text =
                    ExtractApplicableText(
                        section.Xml,
                        citation.Section,
                        citation.Subsection);

                result.Add(
                    new VeteransReviewerApplicableRegulation
                    {
                        Citation = citation.Citation,
                        Text = text,
                        SourceUri = section.SourceUri,
                        UpToDateAsOf = currentDate,
                        RetrievedUtc = retrievedUtc,
                        SourceSha256 = section.Sha256
                    });
            }
        }

        return result
            .OrderBy(item => item.Citation, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<Dictionary<int, DateOnly>> GetCurrentTitleDatesAsync(
        IReadOnlyCollection<int> requestedTitles,
        CancellationToken cancellationToken)
    {
        using var request =
            CreateRequest(
                new Uri($"{BaseUri}/api/versioner/v1/titles.json"),
                "application/json");

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var bytes =
            await ReadLimitedAsync(
                response,
                MaxTitlesResponseBytes,
                cancellationToken);

        using var document = JsonDocument.Parse(bytes);

        if (!document.RootElement.TryGetProperty("titles", out var titles) ||
            titles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "eCFR titles response did not contain a titles array.");
        }

        var requested = requestedTitles.ToHashSet();
        var result = new Dictionary<int, DateOnly>();

        foreach (var title in titles.EnumerateArray())
        {
            if (!title.TryGetProperty("number", out var numberElement) ||
                !numberElement.TryGetInt32(out var number) ||
                !requested.Contains(number))
            {
                continue;
            }

            if (!title.TryGetProperty(
                    "up_to_date_as_of",
                    out var dateElement) ||
                dateElement.ValueKind != JsonValueKind.String ||
                !DateOnly.TryParseExact(
                    dateElement.GetString(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                throw new InvalidDataException(
                    $"eCFR Title {number} has no valid up-to-date-as-of date.");
            }

            result[number] = date;
        }

        if (result.Count != requested.Count)
        {
            var missing =
                requested
                    .Where(number => !result.ContainsKey(number))
                    .OrderBy(number => number)
                    .ToArray();

            throw new InvalidDataException(
                "eCFR currency metadata is missing requested title(s): " +
                string.Join(", ", missing));
        }

        return result;
    }

    private async Task<SectionResponse> GetSectionAsync(
        int title,
        string section,
        DateOnly currentDate,
        CancellationToken cancellationToken)
    {
        var part = section.Split('.', 2)[0];
        var date = currentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sourceUri =
            $"{BaseUri}/api/versioner/v1/full/{date}/title-{title}.xml" +
            $"?part={Uri.EscapeDataString(part)}" +
            $"&section={Uri.EscapeDataString(section)}";

        using var request =
            CreateRequest(
                new Uri(sourceUri),
                "application/xml");

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        var bytes =
            await ReadLimitedAsync(
                response,
                MaxSectionResponseBytes,
                cancellationToken);

        if (bytes.Length == 0)
            throw new InvalidDataException("eCFR section response was empty.");

        var xml = Encoding.UTF8.GetString(bytes);

        try
        {
            _ = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException)
        {
            throw new InvalidDataException(
                "eCFR section response was not valid XML.",
                ex);
        }

        return new SectionResponse(
            xml,
            sourceUri,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static string ExtractApplicableText(
        string xml,
        string section,
        char? subsection)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

        var sectionNode =
            document
                .Descendants()
                .FirstOrDefault(
                    element =>
                        string.Equals(
                            (string?)element.Attribute("TYPE"),
                            "SECTION",
                            StringComparison.OrdinalIgnoreCase) &&
                        SectionMatches(
                            (string?)element.Attribute("N"),
                            section));

        if (sectionNode is null)
        {
            throw new InvalidDataException(
                $"eCFR response did not contain section {section}.");
        }

        var paragraphs =
            sectionNode
                .Descendants()
                .Where(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "P",
                        StringComparison.OrdinalIgnoreCase))
                .Select(element => NormalizeWhitespace(element.Value))
                .Where(value => value.Length > 0)
                .ToArray();

        if (paragraphs.Length == 0)
        {
            throw new InvalidDataException(
                $"eCFR section {section} contains no paragraph text.");
        }

        if (subsection is null)
            return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);

        var marker = $"({char.ToLowerInvariant(subsection.Value)})";
        var start =
            Array.FindIndex(
                paragraphs,
                paragraph => StartsWithMarker(paragraph, marker));

        if (start < 0)
        {
            throw new InvalidDataException(
                $"eCFR section {section} did not contain subsection {marker}.");
        }

        var end = paragraphs.Length;
        var next = char.ToLowerInvariant(subsection.Value) + 1;

        if (next <= 'z')
        {
            var nextMarker = $"({(char)next})";

            for (var index = start + 1; index < paragraphs.Length; index++)
            {
                if (StartsWithMarker(paragraphs[index], nextMarker))
                {
                    end = index;
                    break;
                }
            }
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            paragraphs[start..end]);
    }

    private static string NormalizeWhitespace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return WhitespacePattern.Replace(value, " ").Trim();
    }

    private static bool SectionMatches(
        string? value,
        string section)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized =
            value.Trim()
                .TrimStart('§')
                .Trim();

        return string.Equals(
            normalized,
            section,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithMarker(
        string text,
        string marker) =>
        text.StartsWith(marker, StringComparison.OrdinalIgnoreCase) &&
        (text.Length == marker.Length ||
         char.IsWhiteSpace(text[marker.Length]));

    private static ParsedCitation ParseCitation(string citation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(citation);

        var normalized = citation.Trim();
        var match = CitationPattern.Match(normalized);

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Unsupported reviewer regulatory citation: '{normalized}'. " +
                "Expected a section citation such as '38 C.F.R. § 3.310(a)' or '38 CFR 3.310(a)'.");
        }

        var title =
            int.Parse(
                match.Groups["title"].Value,
                CultureInfo.InvariantCulture);

        var section = match.Groups["section"].Value;
        char? subsection = null;

        if (match.Groups["subsection"].Success)
        {
            var value = match.Groups["subsection"].Value;
            subsection = char.ToLowerInvariant(value[1]);
        }

        return new ParsedCitation(
            normalized,
            title,
            section,
            subsection);
    }

    private static HttpRequestMessage CreateRequest(
        Uri uri,
        string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.UserAgent.ParseAdd("EMF-Veterans/1.0");
        return request;
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long length &&
            length > maximumBytes)
        {
            throw new InvalidDataException(
                "eCFR response exceeded the configured size limit.");
        }

        await using var source =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var total = 0;

        while (true)
        {
            var read =
                await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);

            if (read == 0)
                break;

            total += read;

            if (total > maximumBytes)
            {
                throw new InvalidDataException(
                    "eCFR response exceeded the configured size limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private sealed record ParsedCitation(
        string Citation,
        int Title,
        string Section,
        char? Subsection);

    private sealed record SectionResponse(
        string Xml,
        string SourceUri,
        string Sha256);
}
