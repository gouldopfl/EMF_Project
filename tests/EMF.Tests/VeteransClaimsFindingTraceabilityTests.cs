using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsFindingTraceabilityTests
{
    [Fact]
    public void FindingArtifact_ReferencesPlatformArtifactWithRole()
    {
        var findingId = new FindingId("finding-001");
        var artifactId = new ArtifactId("artifact-001");

        var reference = new FindingArtifact
        {
            FindingId = findingId,
            ArtifactId = artifactId,
            Role = FindingTraceabilityRoles.Supporting
        };

        Assert.Equal(findingId, reference.FindingId);
        Assert.Equal(artifactId, reference.ArtifactId);
        Assert.Equal(FindingTraceabilityRoles.Supporting, reference.Role);
    }

    [Fact]
    public void FindingRegulatoryProvision_ReferencesAuthorityWithRole()
    {
        var findingId = new FindingId("finding-001");
        var provisionId = new RegulatoryProvisionId("provision-001");

        var reference = new FindingRegulatoryProvision
        {
            FindingId = findingId,
            RegulatoryProvisionId = provisionId,
            Role = FindingTraceabilityRoles.Qualifying
        };

        Assert.Equal(findingId, reference.FindingId);
        Assert.Equal(provisionId, reference.RegulatoryProvisionId);
        Assert.Equal(FindingTraceabilityRoles.Qualifying, reference.Role);
    }
}
