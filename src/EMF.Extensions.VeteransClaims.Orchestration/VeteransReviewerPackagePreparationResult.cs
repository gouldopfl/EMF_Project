using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackagePreparationResult
{
    public required Artifact SummaryArtifact { get; init; }

    public required EvidencePackage Package { get; init; }
}
