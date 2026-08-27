using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentInterpretationValidator
{
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
}
