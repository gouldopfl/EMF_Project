using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationDetails
{
    public required ClaimIssue ClaimIssue { get; init; }

    public required IReadOnlyList<ClaimedCondition>
        ClaimedConditions { get; init; }

    public IReadOnlyList<ServiceConnectionBasisClaimedConditionDetails>
        ClaimedConditionBases { get; init; } = [];

    public required IReadOnlyList<ServiceConnectionTheory>
        ServiceConnectionTheories { get; init; }

    public required IReadOnlyList<ServiceConnectionBasis>
        ServiceConnectionBases { get; init; }

    public required IReadOnlyList<ServiceConnectionBasisConditionDetails>
        ServiceConnectedConditions { get; init; }

    public IReadOnlyList<ServiceConnectionBasisMedicationDetails>
        PrescribedMedications { get; init; } = [];

    public IReadOnlyList<ServiceConnectionBasisExposureDetails>
        Exposures { get; init; } = [];

    public IReadOnlyList<ServiceConnectionBasisPreexistingConditionDetails>
        PreexistingConditions { get; init; } = [];

    public IReadOnlyList<ServiceConnectionBasisPresumptionDetails>
        Presumptions { get; init; } = [];

    public IReadOnlyList<ServiceConnectionBasisMedicalOpinionDetails>
        MedicalOpinions { get; init; } = [];

    public IReadOnlyList<ServiceConnectionBasisArtifactDetails>
        BasisArtifacts { get; init; } = [];

    public required IReadOnlyList<ServiceConnectionBasisServiceEventDetails>
        ServiceEvents { get; init; }

    public required IReadOnlyList<ServiceConnectionBasisRequirementDetails>
        Requirements { get; init; }

    public required ClaimIssueEvidenceDetails Evidence { get; init; }

    public required IReadOnlyList<ClaimIssueAdjudicationEvent>
        Timeline { get; init; }
}
