using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteExposureArtifactRepositoryTests
{
    [Fact]
    public async Task ExposureArtifact_RoundTripsInBothDirections()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("v-exposure-artifact")
            };
            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(veteran);

            var exposure = new Exposure
            {
                Id = new ExposureId("exposure-artifact"),
                VeteranId = veteran.Id,
                ExposureType = "Environmental"
            };

            var repository =
                new SqliteServiceHistoryRepository(path);
            await repository.AddExposureAsync(exposure);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-exposure"),
                Name = "Exposure record",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            var association = new ExposureArtifact
            {
                ExposureId = exposure.Id,
                ArtifactId = artifact.Id,
                Role = ExposureTraceabilityRoles.Qualifying
            };
            await repository.AddExposureArtifactAsync(association);

            var byExposure = Assert.Single(
                await repository.GetExposureArtifactsAsync(exposure.Id));
            var byArtifact = Assert.Single(
                await repository.GetExposureArtifactsAsync(artifact.Id));

            Assert.Equal(exposure.Id, byExposure.ExposureId);
            Assert.Equal(artifact.Id, byExposure.ArtifactId);
            Assert.Equal(
                ExposureTraceabilityRoles.Qualifying,
                byExposure.Role);
            Assert.Equal(byExposure.ExposureId, byArtifact.ExposureId);
            Assert.Equal(byExposure.ArtifactId, byArtifact.ArtifactId);
            Assert.Equal(byExposure.Role, byArtifact.Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExposureArtifact_RejectsInvalidRole()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = new SqliteServiceHistoryRepository(path);

            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.AddExposureArtifactAsync(
                    new ExposureArtifact
                    {
                        ExposureId = new ExposureId("exposure"),
                        ArtifactId = new ArtifactId("artifact"),
                        Role = "Unknown"
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExposureArtifact_RejectsMissingArtifact()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("v-exposure-missing-artifact")
            };
            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(veteran);

            var exposure = new Exposure
            {
                Id = new ExposureId("exposure-missing-artifact"),
                VeteranId = veteran.Id,
                ExposureType = "Environmental"
            };

            var repository = new SqliteServiceHistoryRepository(path);
            await repository.AddExposureAsync(exposure);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddExposureArtifactAsync(
                    new ExposureArtifact
                    {
                        ExposureId = exposure.Id,
                        ArtifactId = new ArtifactId("missing-artifact"),
                        Role = ExposureTraceabilityRoles.Supporting
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
