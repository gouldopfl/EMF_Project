using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class MedicalLiteratureClassificationProposal
{
    public required MedicalLiteratureSourceId
        MedicalLiteratureSourceId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required IReadOnlyList<
        MedicalLiteratureRequirementClassification>
        Classifications { get; init; }
}
