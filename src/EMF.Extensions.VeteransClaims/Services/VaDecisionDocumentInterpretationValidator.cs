using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentInterpretationValidator
{
    public void ValidateAgainstSource(
        VaDecisionDocumentInterpretation interpretation,
        string sourceText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);

        Validate(interpretation);

        foreach (var issue in interpretation.IssueDecisions)
        {
            foreach (var excerpt in issue.SourceExcerpts)
            {
                ValidateExcerptAgainstSource(
                    interpretation.ArtifactId,
                    excerpt,
                    sourceText);
            }
        }
    }

    public void Validate(
        VaDecisionDocumentInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(interpretation);

        if (interpretation.IssueDecisions.Count == 0)
        {
            throw new InvalidOperationException(
                "A VA decision interpretation must contain at least one issue decision.");
        }

        foreach (var issue in interpretation.IssueDecisions)
        {
            ValidateIssue(issue);
        }
    }

    private static void ValidateIssue(
        VaIssueDecisionInterpretation issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        if (string.IsNullOrWhiteSpace(issue.IssueDescription))
        {
            throw new InvalidOperationException(
                "An interpreted issue must contain a description.");
        }

        if (issue.Outcome != IssueDecisionOutcomes.Granted &&
            issue.Outcome != IssueDecisionOutcomes.Denied &&
            issue.Outcome != IssueDecisionOutcomes.Deferred &&
            issue.Outcome != IssueDecisionOutcomes.PartiallyGranted)
        {
            throw new InvalidOperationException(
                $"Unknown interpreted issue outcome '{issue.Outcome}'.");
        }

        if (issue.Outcome == IssueDecisionOutcomes.Denied &&
            string.IsNullOrWhiteSpace(issue.Rationale))
        {
            throw new InvalidOperationException(
                "A denied issue interpretation must contain rationale.");
        }

        foreach (var excerpt in issue.SourceExcerpts)
        {
            ValidateExcerpt(excerpt);
        }
    }

    private static void ValidateExcerpt(
        DecisionDocumentSourceExcerpt excerpt)
    {
        ArgumentNullException.ThrowIfNull(excerpt);

        if (string.IsNullOrWhiteSpace(excerpt.Text))
        {
            throw new InvalidOperationException(
                "A decision source excerpt cannot be empty.");
        }

        if (excerpt.StartOffset is < 0)
        {
            throw new InvalidOperationException(
                "A decision source excerpt start offset cannot be negative.");
        }

        if (excerpt.Length is < 0)
        {
            throw new InvalidOperationException(
                "A decision source excerpt length cannot be negative.");
        }
    }

    private static void ValidateExcerptAgainstSource(
        EMF.Core.Models.Identities.ArtifactId artifactId,
        DecisionDocumentSourceExcerpt excerpt,
        string sourceText)
    {
        if (excerpt.ArtifactId != artifactId)
        {
            throw new InvalidOperationException(
                "A decision source excerpt must reference " +
                "the interpreted artifact.");
        }

        if (excerpt.StartOffset is null || excerpt.Length is null)
        {
            throw new InvalidOperationException(
                "A decision source excerpt must contain " +
                "a start offset and length.");
        }

        var startOffset = excerpt.StartOffset.Value;
        var length = excerpt.Length.Value;

        if (length != excerpt.Text.Length ||
            length > sourceText.Length ||
            startOffset > sourceText.Length - length)
        {
            throw new InvalidOperationException(
                "A decision source excerpt range is invalid.");
        }

        if (!sourceText.AsSpan(startOffset, length)
                .SequenceEqual(excerpt.Text.AsSpan()))
        {
            throw new InvalidOperationException(
                "A decision source excerpt does not match " +
                "the source document.");
        }
    }
}
