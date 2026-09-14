using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IBoundedEvidenceInterpretationRepository
{
    Task AddReviewedInterpretationAsync(
        ReviewedBoundedEvidenceInterpretation interpretation,
        CancellationToken cancellationToken = default) =>
        AddReviewedInterpretationsAsync(
            [interpretation],
            cancellationToken);

    Task AddReviewedInterpretationsAsync(
        IReadOnlyList<ReviewedBoundedEvidenceInterpretation>
            interpretations,
        CancellationToken cancellationToken = default);

    Task SupersedeReviewedInterpretationsAsync(
        string supersededCorrelationId,
        IReadOnlyList<ReviewedBoundedEvidenceInterpretation>
            replacements,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewedBoundedEvidenceInterpretation>>
        GetReviewedInterpretationsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewedBoundedEvidenceInterpretation>>
        GetReviewedInterpretationsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default);
}
