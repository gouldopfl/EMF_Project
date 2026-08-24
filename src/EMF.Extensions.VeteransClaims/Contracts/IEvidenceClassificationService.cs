using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IEvidenceClassificationService
{
    Task<EvidenceClassification> ClassifyAsync(
        ArtifactId artifactId,
        string classification,
        ClaimIssueId? claimIssueId = null,
        CancellationToken cancellationToken = default);
    Task AssociateRequirementAsync(
        EvidenceClassificationId classificationId,
        RequirementId requirementId,
        CancellationToken cancellationToken = default);

}
