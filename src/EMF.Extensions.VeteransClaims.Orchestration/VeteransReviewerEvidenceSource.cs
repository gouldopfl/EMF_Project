using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerEvidenceSource
{
    public required ArtifactId ArtifactId { get; init; }

    public string? ArtifactName { get; init; }

    public string? ArtifactType { get; init; }

    public string? ContentRole { get; init; }

    public required IReadOnlyList<string> Classifications { get; init; }

    public IReadOnlyList<ReviewedMedicalLiteratureClassification>
        ReviewedMedicalLiteratureClassifications { get; init; } = [];

    public required string Text { get; init; }
}
