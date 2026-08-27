using EMF.Core.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class VaDecisionDocumentInterpretation
{
    public required ArtifactId ArtifactId { get; init; }

    public DateTimeOffset? DecisionDate { get; init; }

    public required IReadOnlyList<VaIssueDecisionInterpretation>
        IssueDecisions { get; init; }
}
