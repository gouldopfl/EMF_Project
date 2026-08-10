using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionBasisMedicalOpinionTests
{
    [Fact]
    public void Association_PreservesBasisMedicalOpinionAndRole()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var medicalOpinionId =
            new MedicalOpinionId("medical-opinion-001");

        var association =
            new ServiceConnectionBasisMedicalOpinion
            {
                ServiceConnectionBasisId = basisId,
                MedicalOpinionId = medicalOpinionId,
                Role =
                    ServiceConnectionBasisTraceabilityRoles.Contradicting
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            medicalOpinionId,
            association.MedicalOpinionId);

        Assert.Equal(
            ServiceConnectionBasisTraceabilityRoles.Contradicting,
            association.Role);
    }
}
