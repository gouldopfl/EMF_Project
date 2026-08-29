namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EvidenceDevelopmentPlanDetails
{
    public required EvidenceDevelopmentPlan Plan { get; init; }

    public required IReadOnlyList<EvidenceDevelopmentPlanRequirement>
        Requirements { get; init; }

    public EvidenceDevelopmentPlanStatus? Status { get; init; }

    public required IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>
        EvidenceGaps { get; init; }

    public IReadOnlyList<EvidenceGap>
        GapDetails { get; init; } = [];

    public required IReadOnlyList<EvidenceDevelopmentPlanArtifact>
        Artifacts { get; init; }

    public required IReadOnlyList<EvidenceDevelopmentExecution>
        Executions { get; init; }

    public required IReadOnlyList<EvidenceDevelopmentResult>
        Results { get; init; }
}
