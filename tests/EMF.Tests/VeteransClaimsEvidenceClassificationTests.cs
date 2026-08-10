using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceClassificationTests
{
    [Fact]
    public void Classification_ReferencesPlatformArtifactAndClaimIssue()
    {
        var artifactId = new ArtifactId("artifact-001");
        var claimIssueId = new ClaimIssueId("claim-issue-001");

        var classification = new EvidenceClassification
        {
            Id = new EvidenceClassificationId("classification-001"),
            ArtifactId = artifactId,
            ClaimIssueId = claimIssueId,
            Classification = EvidenceClassifications.MedicalEvidence
        };

        Assert.Equal(artifactId, classification.ArtifactId);
        Assert.Equal(claimIssueId, classification.ClaimIssueId);
        Assert.Equal(
            EvidenceClassifications.MedicalEvidence,
            classification.Classification);
    }

    [Fact]
    public void Classification_DoesNotRequireClaimIssueScope()
    {
        var classification = new EvidenceClassification
        {
            Id = new EvidenceClassificationId("classification-002"),
            ArtifactId = new ArtifactId("artifact-002"),
            Classification = EvidenceClassifications.ServiceRecord
        };

        Assert.Null(classification.ClaimIssueId);
    }
}
