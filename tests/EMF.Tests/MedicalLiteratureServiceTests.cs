using EMF.Persistence.Repositories;
using EMF.Core.Models.Identities;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class MedicalLiteratureServiceTests
{
    [Fact]
    public async Task AddSourceAsync_AddsSourceAndIsIdempotent()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var service =
                new MedicalLiteratureService(
                    new SqliteRegulatoryRepository(path),
                    literature);

            var source = CreateSource("study-1");

            var first = await service.AddSourceAsync(source);
            var second = await service.AddSourceAsync(source);

            Assert.Equal(source.Id, first.Id);
            Assert.Equal(source.Id, second.Id);

            var stored =
                await literature.GetMedicalLiteratureSourceAsync(source.Id);

            Assert.NotNull(stored);
            Assert.Equal(source.Title, stored!.Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddSourceAsync_RejectsConflictingMetadata()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var service =
                new MedicalLiteratureService(
                    new SqliteRegulatoryRepository(path),
                    literature);

            await service.AddSourceAsync(CreateSource("study-1"));

            var conflicting =
                new MedicalLiteratureSource
                {
                    Id = new MedicalLiteratureSourceId("study-1"),
                    Title = "Different title",
                    Authors = "Example Authors",
                    Publication = "Example Journal",
                    PublicationYear = 2026,
                    PeerReviewed = true,
                    Doi = "10.1000/example"
                };

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.AddSourceAsync(conflicting));

            Assert.Contains(
                "already exists with different metadata",
                exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddRequirementLiteratureAsync_AddsLinkAndIsIdempotent()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var requirementId =
                await AddRequirementAsync(regulatory);

            var service =
                new MedicalLiteratureService(
                    regulatory,
                    literature);

            var source = CreateSource("study-1");
            await service.AddSourceAsync(source);

            var first =
                await service.AddRequirementLiteratureAsync(
                    requirementId,
                    source.Id,
                    EvidenceGuidanceRoles.SupportsRequirement,
                    "Supports the medical mechanism.");

            var second =
                await service.AddRequirementLiteratureAsync(
                    requirementId,
                    source.Id,
                    EvidenceGuidanceRoles.SupportsRequirement,
                    "Supports the medical mechanism.");

            Assert.Equal(
                source.Id,
                first.Association.MedicalLiteratureSourceId);

            Assert.Equal(
                first.Association.Description,
                second.Association.Description);

            var links =
                await literature.GetRequirementMedicalLiteratureAsync(
                    requirementId);

            Assert.Single(links);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddRequirementLiteratureAsync_RejectsConflictingDescription()
    {
        var path = Path.GetTempFileName();

        try
        {
            var regulatory = new SqliteRegulatoryRepository(path);
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var requirementId =
                await AddRequirementAsync(regulatory);

            var service =
                new MedicalLiteratureService(
                    regulatory,
                    literature);

            var source = CreateSource("study-1");
            await service.AddSourceAsync(source);

            await service.AddRequirementLiteratureAsync(
                requirementId,
                source.Id,
                EvidenceGuidanceRoles.SupportsRequirement,
                "First description.");

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.AddRequirementLiteratureAsync(
                        requirementId,
                        source.Id,
                        EvidenceGuidanceRoles.SupportsRequirement,
                        "Different description."));

            Assert.Contains(
                "different description",
                exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddRequirementLiteratureAsync_RejectsUnsupportedRole()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var service =
                new MedicalLiteratureService(
                    new SqliteRegulatoryRepository(path),
                    literature);

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.AddRequirementLiteratureAsync(
                    new RequirementId("requirement-1"),
                    new MedicalLiteratureSourceId("study-1"),
                    "Unsupported",
                    "Description."));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddRequirementLiteratureAsync_RejectsMissingRequirement()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var service =
                new MedicalLiteratureService(
                    new SqliteRegulatoryRepository(path),
                    literature);

            var source = CreateSource("study-1");
            await service.AddSourceAsync(source);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.AddRequirementLiteratureAsync(
                        new RequirementId("missing"),
                        source.Id,
                        EvidenceGuidanceRoles.SupportsRequirement,
                        "Description."));

            Assert.Contains(
                "Requirement not found",
                exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task AddSourceArtifactAsync_AddsAssociationAndIsIdempotent()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var service = new MedicalLiteratureService(
                new SqliteRegulatoryRepository(path),
                literature);

            var source = CreateSource("study-artifact");
            await service.AddSourceAsync(source);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-study"),
                Name = "study.pdf",
                ArtifactType = "pdf"
            };

            await evidence.AddArtifactAsync(artifact);

            var first =
                await service.AddSourceArtifactAsync(
                    source.Id,
                    artifact.Id);

            var second =
                await service.AddSourceArtifactAsync(
                    source.Id,
                    artifact.Id);

            Assert.Equal(source.Id, first.MedicalLiteratureSourceId);
            Assert.Equal(artifact.Id, first.ArtifactId);
            Assert.Equal(first.MedicalLiteratureSourceId,
                second.MedicalLiteratureSourceId);
            Assert.Equal(first.ArtifactId, second.ArtifactId);

            Assert.Single(
                await literature.GetArtifactIdsAsync(source.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task AddSourceArtifactAsync_RejectsMissingSource()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var service = new MedicalLiteratureService(
                new SqliteRegulatoryRepository(path),
                literature);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.AddSourceArtifactAsync(
                        new MedicalLiteratureSourceId("missing"),
                        new ArtifactId("artifact-missing")));

            Assert.Contains(
                "Medical literature source not found",
                exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MedicalLiteratureSource CreateSource(string id) =>
        new()
        {
            Id = new MedicalLiteratureSourceId(id),
            Title = "Example medical study",
            Authors = "Example Authors",
            Publication = "Example Journal",
            PublicationYear = 2026,
            PeerReviewed = true,
            Doi = "10.1000/example"
        };

    private static async Task<RequirementId> AddRequirementAsync(
        SqliteRegulatoryRepository regulatory)
    {
        var authority =
            new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-medlit-service"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

        await regulatory.AddRegulatoryAuthorityAsync(authority);

        var provision =
            new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-medlit-service"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };

        await regulatory.AddRegulatoryProvisionAsync(provision);

        var requirement =
            new Requirement
            {
                Id = new RequirementId("requirement-medlit-service"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary service connection requirement."
            };

        await regulatory.AddRequirementAsync(requirement);

        return requirement.Id;
    }
}
