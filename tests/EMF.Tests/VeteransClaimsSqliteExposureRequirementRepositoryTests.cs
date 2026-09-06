using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class
    VeteransClaimsSqliteExposureRequirementRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsExposureRequirement()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var veteranRepository =
                new SqliteVeteranRepository(databasePath);
            await veteranRepository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };
            await veteranRepository.AddVeteranAsync(veteran);

            var serviceHistory =
                new SqliteServiceHistoryRepository(databasePath);
            var exposure = new Exposure
            {
                Id = new ExposureId("exposure-001"),
                VeteranId = veteran.Id,
                ExposureType = "Environmental"
            };
            await serviceHistory.AddExposureAsync(exposure);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);
            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-001"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.303"
            };
            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-001"),
                RegulatoryProvisionId = provision.Id,
                Description = "Required element."
            };
            await regulatory.AddRequirementAsync(requirement);

            await serviceHistory.AddExposureRegulatoryProvisionAsync(
                new ExposureRegulatoryProvision
                {
                    ExposureId = exposure.Id,
                    RegulatoryProvisionId = provision.Id
                });

            await serviceHistory.AddExposureRequirementAsync(
                new ExposureRequirement
                {
                    ExposureId = exposure.Id,
                    RequirementId = requirement.Id
                });

            Assert.Equal(
                requirement.Id,
                Assert.Single(
                    await serviceHistory.GetRequirementIdsAsync(
                        exposure.Id)));

            Assert.Equal(
                exposure.Id,
                Assert.Single(
                    await serviceHistory.GetExposureIdsAsync(
                        requirement.Id)));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsRequirementWithoutLinkedProvision()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.db");

        try
        {
            var veteranRepository =
                new SqliteVeteranRepository(databasePath);
            await veteranRepository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };
            await veteranRepository.AddVeteranAsync(veteran);

            var serviceHistory =
                new SqliteServiceHistoryRepository(databasePath);
            var exposure = new Exposure
            {
                Id = new ExposureId("exposure-001"),
                VeteranId = veteran.Id,
                ExposureType = "Environmental"
            };
            await serviceHistory.AddExposureAsync(exposure);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);
            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var linkedProvision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-linked"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.303"
            };
            await regulatory.AddRegulatoryProvisionAsync(linkedProvision);

            var requirementProvision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-requirement"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.304"
            };
            await regulatory.AddRegulatoryProvisionAsync(
                requirementProvision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-001"),
                RegulatoryProvisionId = requirementProvision.Id,
                Description = "Required element."
            };
            await regulatory.AddRequirementAsync(requirement);

            await serviceHistory.AddExposureRegulatoryProvisionAsync(
                new ExposureRegulatoryProvision
                {
                    ExposureId = exposure.Id,
                    RegulatoryProvisionId = linkedProvision.Id
                });

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => serviceHistory.AddExposureRequirementAsync(
                        new ExposureRequirement
                        {
                            ExposureId = exposure.Id,
                            RequirementId = requirement.Id
                        }));

            Assert.Contains(
                "regulatory provision must be linked",
                exception.Message);
            Assert.Empty(
                await serviceHistory.GetRequirementIdsAsync(
                    exposure.Id));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
