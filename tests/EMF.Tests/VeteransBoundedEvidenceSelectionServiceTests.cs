using System.Text.Json;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransBoundedEvidenceSelectionServiceTests
{
    [Fact]
    public async Task GetAsync_SelectsOnlyMatchingRequirementAndSource()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var source = new ArtifactId("blue-button-001");

        await repository.AddArtifactAsync(
            new Artifact
            {
                Id = source,
                Name = "VA Blue Button Report",
                ArtifactType = "pdf"
            });

        var expected =
            CreateBoundedArtifact(
                "bounded-001",
                "issue-osa",
                "basis-osa-secondary",
                ["req-causation"],
                "2025-11-19",
                "718",
                "729",
                "261",
                "365");

        var wrongRequirement =
            CreateBoundedArtifact(
                "bounded-002",
                "issue-osa",
                "basis-osa-secondary",
                ["req-aggravation"],
                "2025-11-20",
                "730",
                "731",
                "1",
                "20");

        await repository.AddArtifactAsync(expected);
        await repository.AddArtifactAsync(wrongRequirement);
        await AddLineageAsync(repository, source, expected.Id);
        await AddLineageAsync(repository, source, wrongRequirement.Id);

        var service =
            new VeteransBoundedEvidenceSelectionService(repository);

        var results =
            await service.GetAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                new RequirementId("req-causation"),
                source);

        var result = Assert.Single(results);
        Assert.Equal(expected.Id, result.Artifact.Id);
        Assert.Equal(new DateOnly(2025, 11, 19), result.EvidenceDate);
        Assert.Equal(718, result.SourceStartPage);
        Assert.Equal(365, result.SourceEndLine);
        Assert.Equal(source, result.SourceArtifactId);
    }

    [Fact]
    public async Task GetAsync_ReadsSqliteStyleRequirementMetadata()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var source = new ArtifactId("blue-button-001");

        await repository.AddArtifactAsync(
            new Artifact
            {
                Id = source,
                Name = "source",
                ArtifactType = "pdf"
            });

        var artifact =
            CreateBoundedArtifact(
                "bounded-json",
                "issue-osa",
                "basis-osa-secondary",
                ["req-causation", "req-other"],
                "2021-09-29",
                "3021",
                "3042",
                "645",
                "668");

        var jsonMetadata =
            JsonSerializer.Deserialize<Dictionary<string, object>>(
                JsonSerializer.Serialize(artifact.Metadata))!;

        artifact =
            new Artifact
            {
                Id = artifact.Id,
                Name = artifact.Name,
                ArtifactType = artifact.ArtifactType,
                Metadata = jsonMetadata
            };

        await repository.AddArtifactAsync(artifact);
        await AddLineageAsync(repository, source, artifact.Id);

        var service =
            new VeteransBoundedEvidenceSelectionService(repository);

        var result =
            Assert.Single(
                await service.GetAsync(
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    new RequirementId("req-causation"),
                    source));

        Assert.Equal(2, result.RequirementIds.Count);
        Assert.Contains(
            new RequirementId("req-other"),
            result.RequirementIds);
    }

    [Fact]
    public async Task GetAsync_RejectsMissingDerivedFromLineage()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var source = new ArtifactId("blue-button-001");

        await repository.AddArtifactAsync(
            new Artifact
            {
                Id = source,
                Name = "source",
                ArtifactType = "pdf"
            });

        var artifact =
            CreateBoundedArtifact(
                "bounded-bad",
                "issue-osa",
                "basis-osa-secondary",
                ["req-causation"],
                "2025-11-19",
                "718",
                "729",
                "261",
                "365");

        await repository.AddArtifactAsync(artifact);

        var service =
            new VeteransBoundedEvidenceSelectionService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                new RequirementId("req-causation"),
                source));
    }

    [Fact]
    public async Task GetAsync_RejectsMissingContainsLineage()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var source = new ArtifactId("blue-button-001");

        await repository.AddArtifactAsync(
            new Artifact
            {
                Id = source,
                Name = "source",
                ArtifactType = "pdf"
            });

        var artifact =
            CreateBoundedArtifact(
                "bounded-bad",
                "issue-osa",
                "basis-osa-secondary",
                ["req-causation"],
                "2025-11-19",
                "718",
                "729",
                "261",
                "365");

        await repository.AddArtifactAsync(artifact);
        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = artifact.Id,
                TargetArtifactId = source,
                RelationshipType = RelationshipTypes.DerivedFrom
            });

        var service =
            new VeteransBoundedEvidenceSelectionService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                new RequirementId("req-causation"),
                source));
    }

    private static Artifact CreateBoundedArtifact(
        string id,
        string claimIssueId,
        string basisId,
        string[] requirementIds,
        string evidenceDate,
        string startPage,
        string endPage,
        string startLine,
        string endLine) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = id + ".txt",
            ArtifactType =
                VeteransBoundedEvidenceSelectionService
                    .BoundedEvidenceArtifactType,
            Metadata = new Dictionary<string, object>
            {
                [VeteransArtifactMetadataKeys.ClaimIssueId] = claimIssueId,
                [VeteransArtifactMetadataKeys.ServiceConnectionBasisId] = basisId,
                [VeteransArtifactMetadataKeys.RequirementIds] = requirementIds,
                [VeteransArtifactMetadataKeys.SourceStartPage] = startPage,
                [VeteransArtifactMetadataKeys.SourceEndPage] = endPage,
                [VeteransArtifactMetadataKeys.SourceStartLine] = startLine,
                [VeteransArtifactMetadataKeys.SourceEndLine] = endLine,
                [VeteransArtifactMetadataKeys.EvidenceDate] = evidenceDate,
                [VeteransArtifactMetadataKeys.EvidenceTitle] =
                    "C&P PROGRESS NOTE"
            }
        };

    private static async Task AddLineageAsync(
        TestInfrastructure.InMemoryEvidenceRepository repository,
        ArtifactId source,
        ArtifactId bounded)
    {
        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = source,
                TargetArtifactId = bounded,
                RelationshipType = RelationshipTypes.Contains
            });

        await repository.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = bounded,
                TargetArtifactId = source,
                RelationshipType = RelationshipTypes.DerivedFrom
            });
    }
}
