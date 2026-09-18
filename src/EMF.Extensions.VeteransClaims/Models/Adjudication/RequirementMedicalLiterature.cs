using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class RequirementMedicalLiterature
{
    // Null is accepted only by the single-basis compatibility write path.
    public ServiceConnectionBasisId? ServiceConnectionBasisId { get; init; }

    public required RequirementId RequirementId { get; init; }

    public required MedicalLiteratureSourceId
        MedicalLiteratureSourceId { get; init; }

    public required string GuidanceRole { get; init; }

    public required string Description { get; init; }
}
