namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class VaIssueDecisionInterpretation
{
    public required string IssueDescription { get; init; }

    public required string Outcome { get; init; }

    public required string Rationale { get; init; }

    public required IReadOnlyList<string>
        FavorableFindings { get; init; }

    public required IReadOnlyList<string>
        AdverseFindings { get; init; }

    public required IReadOnlyList<string>
        CitedRegulations { get; init; }

    public required IReadOnlyList<string>
        ReferencedEvidence { get; init; }

    public required IReadOnlyList<DecisionDocumentSourceExcerpt>
        SourceExcerpts { get; init; }
}
