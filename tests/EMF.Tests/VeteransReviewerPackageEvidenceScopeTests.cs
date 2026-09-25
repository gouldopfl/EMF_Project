using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageEvidenceScopeTests
{
    [Fact]
    public void Contains_RequiresDirectMemberAndAuthoritativeSelectedPages()
    {
        var package = new EvidencePackage { Id = new("p"), ClaimIssueId = new("issue"), Purpose = "review", ReviewerRole = "medical" };
        var artifact = new Artifact { Id = new("member"), Name = "Synthetic.pdf", ArtifactType = "file" };
        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
            {
                Package = package,
                Artifacts = [new EvidencePackageArtifact { EvidencePackageId = package.Id, ArtifactId = artifact.Id,
                    ContentRole = EvidencePackageContentRoles.UnderlyingEvidence, ReviewerPageSelection = "2,4" }]
            },
            Artifacts = [artifact],
            ArtifactContents = [new VeteransReviewerArtifactContent { Artifact = artifact, Text = "",
                PrintablePages = Enumerable.Range(1, 4).Select(n => new PrintableArtifactPage
                { PageNumber = n, ContentType = "image/png", Content = new byte[] { 1 } }).ToArray() }]
        };
        var scope = new VeteransReviewerPackageEvidenceScope(details);
        Assert.True(scope.Contains(artifact.Id, 2, 2));
        Assert.False(scope.Contains(artifact.Id, 2, 4));
        Assert.False(scope.Contains(new("parent"), 2, 2));
        Assert.False(scope.Contains(new("later"), 2, 2));
        Assert.Throws<InvalidDataException>(() => scope.Contains(artifact.Id, 4, 2));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Contains_RejectsMissingMemberArtifactOrContent(bool missingArtifact)
    {
        var original = ReviewerPackageTestScope.Create(new EvidencePackage
        { Id = new("p"), ClaimIssueId = new("i"), Purpose = "review", ReviewerRole = "medical" }, "source");
        var scope = new VeteransReviewerPackageEvidenceScope(new VeteransReviewerPackageDetails
        {
            PackageDetails = original.PackageDetails,
            Artifacts = missingArtifact ? [] : original.Artifacts,
            ArtifactContents = missingArtifact ? original.ArtifactContents : []
        });
        Assert.Throws<InvalidDataException>(() => scope.Contains(new("source"), 1, 1));
    }
}
