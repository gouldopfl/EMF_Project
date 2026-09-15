using System.Text.RegularExpressions;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerEvidenceProjectionService
{
    private const int ContextLineRadius = 1;

    private readonly IEvidenceRecognitionTermRepository _recognitionTerms;

    public VeteransReviewerEvidenceProjectionService(
        IEvidenceRecognitionTermRepository recognitionTerms)
    {
        ArgumentNullException.ThrowIfNull(recognitionTerms);

        _recognitionTerms = recognitionTerms;
    }

    public async Task<IReadOnlyList<VeteransReviewerEvidenceProjection>>
        ProjectAsync(
            ClaimIssueAdjudicationDetails details,
            IReadOnlyList<VeteransReviewerEvidenceSource> evidenceSources,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(evidenceSources);

        var artifactIds =
            evidenceSources
                .Select(source => source.ArtifactId)
                .ToArray();

        if (artifactIds.Length != artifactIds.Distinct().Count())
        {
            throw new InvalidOperationException(
                "Reviewer evidence projection contains duplicate artifact IDs.");
        }

        var terms =
            await BuildTermsAsync(
                details,
                cancellationToken);

        var projections =
            new List<VeteransReviewerEvidenceProjection>(
                evidenceSources.Count);

        foreach (var source in evidenceSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            projections.Add(
                ProjectSource(
                    source,
                    terms));
        }

        return projections;
    }

    private async Task<IReadOnlyList<string>> BuildTermsAsync(
        ClaimIssueAdjudicationDetails details,
        CancellationToken cancellationToken)
    {
        var terms =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var condition in details.ClaimedConditions)
            AddNameTerms(terms, condition.Name);

        foreach (var requirement in details.Requirements)
        {
            var recognitionTerms =
                await _recognitionTerms.GetEvidenceRecognitionTermsAsync(
                    requirement.Requirement.Id,
                    cancellationToken);

            if (recognitionTerms.Any(
                    term =>
                        term.RequirementId != requirement.Requirement.Id))
            {
                throw new InvalidOperationException(
                    "Reviewer evidence projection recognition term " +
                    "requirement mismatch.");
            }

            foreach (var recognitionTerm in recognitionTerms)
                AddTerm(terms, recognitionTerm.Term);
        }

        var theoryTypesById =
            details.ServiceConnectionTheories
                .ToDictionary(
                    theory => theory.Id,
                    theory => theory.TheoryType);

        foreach (var condition in details.ServiceConnectedConditions)
        {
            if (IsTheoryType(
                    condition.Basis,
                    theoryTypesById,
                    ServiceConnectionTheoryTypes.Secondary))
            {
                AddNameTerms(
                    terms,
                    condition.ServiceConnectedCondition.Name);
            }
        }

        foreach (var medication in details.PrescribedMedications)
        {
            if (IsTheoryType(
                    medication.Basis,
                    theoryTypesById,
                    ServiceConnectionTheoryTypes.Secondary))
            {
                AddNameTerms(
                    terms,
                    medication.MedicationName);
            }
        }

        foreach (var exposure in details.Exposures)
        {
            if (IsAnyTheoryType(
                    exposure.Basis,
                    theoryTypesById,
                    ServiceConnectionTheoryTypes.Direct,
                    ServiceConnectionTheoryTypes.Presumptive))
            {
                AddNameTerms(
                    terms,
                    exposure.Exposure.ExposureType);
            }
        }

        foreach (var preexisting in details.PreexistingConditions)
        {
            if (IsTheoryType(
                    preexisting.Basis,
                    theoryTypesById,
                    ServiceConnectionTheoryTypes.Aggravation))
            {
                AddNameTerms(
                    terms,
                    preexisting.PreexistingCondition.Name);
            }
        }

        return terms
            .OrderBy(
                term => term,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                term => term,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static VeteransReviewerEvidenceProjection ProjectSource(
        VeteransReviewerEvidenceSource source,
        IReadOnlyList<string> terms)
    {
        var lines = SplitLines(source.Text);
        var matchingLineIndexes = new List<int>();
        var matchedTerms =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Length; i++)
        {
            var normalizedLine = NormalizeWhitespace(lines[i]);

            if (normalizedLine.Length == 0)
                continue;

            var lineMatched = false;

            foreach (var term in terms)
            {
                if (!EvidenceRecognitionTextMatcher.ContainsTerm(
                        normalizedLine,
                        term))
                {
                    continue;
                }

                lineMatched = true;
                matchedTerms.Add(term);
            }

            if (lineMatched)
                matchingLineIndexes.Add(i);
        }

        if (matchingLineIndexes.Count == 0)
        {
            return new VeteransReviewerEvidenceProjection
            {
                ArtifactId = source.ArtifactId,
                Text = string.Empty,
                SourceLineNumbers = [],
                MatchedTerms = []
            };
        }

        var selected = new bool[lines.Length];

        foreach (var index in matchingLineIndexes)
        {
            var start = Math.Max(0, index - ContextLineRadius);
            var end = Math.Min(lines.Length - 1, index + ContextLineRadius);

            for (var selectedIndex = start;
                 selectedIndex <= end;
                 selectedIndex++)
            {
                selected[selectedIndex] = true;
            }
        }

        var projectedLines = new List<string>();
        var sourceLineNumbers = new List<int>();
        var seenLines =
            new HashSet<string>(
                StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!selected[i])
                continue;

            var normalizedLine = NormalizeWhitespace(lines[i]);

            if (normalizedLine.Length == 0 ||
                !seenLines.Add(normalizedLine))
            {
                continue;
            }

            projectedLines.Add(normalizedLine);
            sourceLineNumbers.Add(i + 1);
        }

        return new VeteransReviewerEvidenceProjection
        {
            ArtifactId = source.ArtifactId,
            Text = string.Join("\n", projectedLines),
            SourceLineNumbers = sourceLineNumbers,
            MatchedTerms =
                matchedTerms
                    .OrderBy(
                        term => term,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        term => term,
                        StringComparer.Ordinal)
                    .ToArray()
        };
    }

    private static string[] SplitLines(string text) =>
        text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static string NormalizeWhitespace(string value) =>
        Regex.Replace(
                value,
                @"\s+",
                " ")
            .Trim();

    private static void AddNameTerms(
        ISet<string> terms,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (Match match in Regex.Matches(
                     value,
                     @"[\p{L}\p{N}]+(?:[-/][\p{L}\p{N}]+)*"))
        {
            var term = match.Value.Trim();

            if (term.Length >= 3 || term.Any(char.IsDigit))
                AddTerm(terms, term);
        }
    }

    private static void AddTerm(
        ISet<string> terms,
        string value)
    {
        var normalized = NormalizeWhitespace(value);

        if (normalized.Length > 0)
            terms.Add(normalized);
    }

    private static bool IsTheoryType(
        ServiceConnectionBasis basis,
        IReadOnlyDictionary<ServiceConnectionTheoryId, string>
            theoryTypesById,
        string expectedTheoryType) =>
        theoryTypesById.TryGetValue(
            basis.ServiceConnectionTheoryId,
            out var theoryType) &&
        string.Equals(
            theoryType,
            expectedTheoryType,
            StringComparison.Ordinal);

    private static bool IsAnyTheoryType(
        ServiceConnectionBasis basis,
        IReadOnlyDictionary<ServiceConnectionTheoryId, string>
            theoryTypesById,
        params string[] expectedTheoryTypes) =>
        theoryTypesById.TryGetValue(
            basis.ServiceConnectionTheoryId,
            out var theoryType) &&
        expectedTheoryTypes.Contains(
            theoryType,
            StringComparer.Ordinal);
}
