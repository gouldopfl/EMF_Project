using System.Text.RegularExpressions;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Development.Providers;

public sealed class
    DevelopmentTextKeywordExtractionProvider :
    IIntelligenceCapabilityProvider<
        TextKeywordExtractionRequest,
        IReadOnlyList<TextKeyword>>
{
    private static readonly HashSet<string>
        DefaultExcludedTerms =
        new(
            [
                "a", "an", "and", "are", "as",
                "at", "be", "by", "for", "from",
                "in", "is", "it", "of", "on",
                "or", "that", "the", "to", "was",
                "were", "with"
            ],
            StringComparer.OrdinalIgnoreCase);

    public IntelligenceCapabilityId Id =>
        IntelligenceCapabilityIds
            .TextKeywordExtraction;

    public IntelligenceProviderId ProviderId
    {
        get;
    } = new("development.local");

    public Task<
        IntelligenceCapabilityResult<
            IReadOnlyList<TextKeyword>>>
        ExecuteAsync(
            TextKeywordExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;

        var excludedTerms =
            new HashSet<string>(
                DefaultExcludedTerms,
                StringComparer.OrdinalIgnoreCase);

        excludedTerms.UnionWith(
            request.ExcludedTerms);

        var offsetsByTerm =
            new Dictionary<
                string,
                List<int>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (Match match in
            Regex.Matches(
                request.Text,
                @"[\p{L}\p{N}]+"))
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (match.Length <
                    request.MinimumKeywordLength ||
                excludedTerms.Contains(match.Value))
            {
                continue;
            }

            var term =
                match.Value.ToLowerInvariant();

            if (!offsetsByTerm.TryGetValue(
                    term,
                    out var offsets))
            {
                offsets = [];
                offsetsByTerm.Add(term, offsets);
            }

            offsets.Add(match.Index);
        }

        IReadOnlyList<TextKeyword> keywords =
            offsetsByTerm
                .Select(
                    pair =>
                        new TextKeyword(
                            pair.Key,
                            pair.Value))
                .OrderByDescending(
                    keyword =>
                        keyword.Occurrences)
                .ThenBy(
                    keyword =>
                        keyword.FirstOffset)
                .ThenBy(
                    keyword =>
                        keyword.Term,
                    StringComparer.Ordinal)
                .Take(request.MaximumKeywords)
                .ToArray();

        return Task.FromResult(
            new IntelligenceCapabilityResult<
                IReadOnlyList<TextKeyword>>
            {
                Success = true,
                Message =
                    $"{keywords.Count} keywords extracted.",
                Output = keywords,
                Metadata =
                    new IntelligenceExecutionMetadata
                    {
                        CapabilityId = Id,
                        ProviderId = ProviderId,
                        CorrelationId =
                            context.CorrelationId,
                        EngineName =
                            "deterministic-frequency",
                        EngineVersion = "1.0",
                        StartedUtc = startedUtc,
                        CompletedUtc =
                            DateTimeOffset.UtcNow
                    },
                SourceArtifactIds =
                    context.InputArtifactIds.ToArray(),
                Warnings =
                [
                    "Keywords use deterministic lexical " +
                    "frequency, not semantic relevance."
                ],
                RequiresReview = true
            });
    }
}
