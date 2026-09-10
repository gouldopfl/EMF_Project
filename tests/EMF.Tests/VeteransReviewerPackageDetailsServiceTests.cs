using System.Reflection;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_MissingPackageReturnsNull()
    {
        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService(),
                new InMemoryEvidenceRepository());

        var result =
            await service.GetAsync(
                new EvidencePackageId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ResolvesPackageArtifacts()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);
        Assert.Equal(packageId, result.PackageDetails.Package.Id);
        Assert.Same(artifact, Assert.Single(result.Artifacts));
    }

    [Fact]
    public async Task GetAsync_ExtractsReviewerArtifactContent()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        await evidence.AddRelationshipAsync(
            new Relationship
            {
                SourceArtifactId = artifact.Id,
                TargetArtifactId = new ArtifactId("derived-1"),
                RelationshipType = "DerivedFrom",
                CreatedUtc =
                    new DateTimeOffset(
                        2026, 8, 1, 14, 0, 0, TimeSpan.Zero)
            });

        await evidence.AddProvenanceAsync(
            new Provenance
            {
                ArtifactId = artifact.Id,
                Source = "/records/sleep-study.pdf",
                RecordedBy = "EMF.Discovery",
                RecordedUtc =
                    new DateTimeOffset(
                        2026, 8, 1, 12, 0, 0, TimeSpan.Zero)
            });

        var extractor =
            new RecordingTextExtractor("reviewable text");

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence,
                extractor);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);

        var content =
            Assert.Single(result.ArtifactContents);

        Assert.Same(artifact, content.Artifact);
        Assert.Equal("reviewable text", content.Text);

        var provenance = Assert.Single(content.Provenance);
        Assert.Equal("/records/sleep-study.pdf", provenance.Source);
        Assert.Equal("EMF.Discovery", provenance.RecordedBy);

        var relationship = Assert.Single(content.Relationships);
        Assert.Equal(artifact.Id, relationship.SourceArtifactId);
        Assert.Equal(
            new ArtifactId("derived-1"),
            relationship.TargetArtifactId);
        Assert.Equal("DerivedFrom", relationship.RelationshipType);

        Assert.Equal(artifact.Id, extractor.ArtifactId);
    }

    [Fact]
    public async Task GetAsync_RejectsWrongReturnedArtifactIdentity()
    {
        var packageId = new EvidencePackageId("package-1");
        var requestedId = new ArtifactId("artifact-1");
        var returned = CreateArtifact("artifact-other");

        var evidence =
            Proxy<IEvidenceRepository>(
                (method, args) =>
                    method.Name == "GetArtifactAsync"
                        ? Task.FromResult<Artifact?>(returned)
                        : throw new NotSupportedException());

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        requestedId)
                },
                evidence);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(packageId));
    }

    [Fact]
    public async Task GetAsync_RejectsWrongProvenanceArtifactIdentity()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence =
            Proxy<IEvidenceRepository>(
                (method, args) =>
                    method.Name switch
                    {
                        "GetArtifactAsync" =>
                            Task.FromResult<Artifact?>(artifact),
                        "GetProvenanceAsync" =>
                            Task.FromResult<IReadOnlyList<Provenance>>(
                            [
                                new Provenance
                                {
                                    ArtifactId =
                                        new ArtifactId("artifact-other"),
                                    Source = "test",
                                    RecordedBy = "test"
                                }
                            ]),
                        "GetRelationshipsAsync" =>
                            Task.FromResult<IReadOnlyList<Relationship>>([]),
                        _ => throw new NotSupportedException()
                    });

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(packageId, artifact.Id)
                },
                evidence,
                new RecordingTextExtractor("reviewable text"));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(packageId));

        Assert.Contains("provenance", exception.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsUnrelatedRelationship()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence =
            Proxy<IEvidenceRepository>(
                (method, args) =>
                    method.Name switch
                    {
                        "GetArtifactAsync" =>
                            Task.FromResult<Artifact?>(artifact),
                        "GetProvenanceAsync" =>
                            Task.FromResult<IReadOnlyList<Provenance>>([]),
                        "GetRelationshipsAsync" =>
                            Task.FromResult<IReadOnlyList<Relationship>>(
                            [
                                new Relationship
                                {
                                    SourceArtifactId =
                                        new ArtifactId("other-1"),
                                    TargetArtifactId =
                                        new ArtifactId("other-2"),
                                    RelationshipType = "Unrelated"
                                }
                            ]),
                        _ => throw new NotSupportedException()
                    });

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(packageId, artifact.Id)
                },
                evidence,
                new RecordingTextExtractor("reviewable text"));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(packageId));

        Assert.Contains("relationship", exception.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsMissingArtifacts()
    {
        var packageId = new EvidencePackageId("package-1");
        var existing = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(existing);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        existing.Id,
                        new ArtifactId("missing"))
                },
                evidence);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(packageId));

        Assert.Contains(
            packageId.Value,
            exception.Message);

        Assert.Contains(
            "missing",
            exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_UsesTextSummaryMetadata(
        bool persistedMetadataShape)
    {
        object summary =
            persistedMetadataShape
                ? global::System.Text.Json.JsonDocument
                    .Parse("\"reviewer summary\"")
                    .RootElement
                    .Clone()
                : "reviewer summary";

        var packageId = new EvidencePackageId("package-1");

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("summary-1"),
                Name = "Reviewer summary",
                ArtifactType = "text-summary",
                Metadata =
                    new Dictionary<string, object>
                    {
                        ["summary"] = summary
                    }
            };

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var extractor =
            new RecordingTextExtractor("extracted text");

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence,
                extractor);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);

        var content =
            Assert.Single(result.ArtifactContents);

        Assert.Same(artifact, content.Artifact);
        Assert.Equal("reviewer summary", content.Text);
        Assert.Null(extractor.ArtifactId);
    }

    private static T Proxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler
            { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler!(targetMethod!, args);
    }

    private static Artifact CreateArtifact(string id) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = id,
            ArtifactType = "test"
        };

    private static EvidencePackageDetails CreateDetails(
        EvidencePackageId packageId,
        params ArtifactId[] artifactIds) =>
        new()
        {
            Package = new EvidencePackage
            {
                Id = packageId,
                ClaimIssueId = new ClaimIssueId("issue-1"),
                Purpose = "Physician reviewer package",
                ReviewerRole = "MedicalProfessional"
            },
            Artifacts =
                artifactIds
                    .Select(
                        id => new EvidencePackageArtifact
                        {
                            EvidencePackageId = packageId,
                            ArtifactId = id,
                            ContentRole =
                                EvidencePackageContentRoles
                                    .UnderlyingEvidence
                        })
                    .ToArray()
        };
}

file sealed class RecordingTextExtractor(string? text) :
    EMF.Core.Contracts.IArtifactTextExtractor
{
    public ArtifactId? ArtifactId { get; private set; }

    public Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        ArtifactId = artifactId;
        return Task.FromResult(text);
    }
}

file sealed class RecordingPackageService :
    IEvidencePackageService
{
    public EvidencePackageDetails? Details { get; init; }

    public Task<EvidencePackageDetails?> GetAsync(
        EvidencePackageId evidencePackageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Details);

    public Task<EvidencePackage> CreateAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<EvidencePackageArtifact> AddArtifactAsync(
        EvidencePackageId evidencePackageId,
        ArtifactId artifactId,
        string contentRole,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<EvidencePackageDetails>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_AssignsMedicalEvidenceAppendix()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var classifications =
            new RecordingClassificationRepository
            {
                ExistingClassifications =
                [
                    new EvidenceClassification
                    {
                        Id =
                            new EvidenceClassificationId(
                                "classification-1"),
                        ArtifactId = artifact.Id,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.MedicalEvidence
                    }
                ]
            };

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence,
                classifications,
                new RecordingTextExtractor("reviewable text"));

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);

        Assert.Equal(
            VeteransReviewerPackageAppendix.MedicalEvidence,
            Assert.Single(result.ArtifactContents).Appendix);
    }
}

file sealed class RecordingClassificationRepository :
    IEvidenceClassificationRepository
{
    public IReadOnlyList<EvidenceClassification>
        ExistingClassifications { get; init; } = [];

    public bool ReturnAll { get; init; }

    public Task AddEvidenceClassificationAsync(
        EvidenceClassification classification,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<EvidenceClassification?> GetEvidenceClassificationAsync(
        EvidenceClassificationId classificationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<EvidenceClassification?>(null);

    public Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceClassificationsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EvidenceClassification>>(
            ReturnAll
                ? ExistingClassifications
                : ExistingClassifications
                    .Where(x => x.ArtifactId == artifactId)
                    .ToArray());

    public Task<EvidenceClassification?> FindEvidenceClassificationAsync(
        ArtifactId artifactId,
        ClaimIssueId? claimIssueId,
        string classification,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<EvidenceClassification?>(null);

    public Task AddEvidenceClassificationRequirementAsync(
        EvidenceClassificationRequirement association,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<EvidenceClassificationRequirement>>
        GetEvidenceClassificationRequirementsAsync(
            EvidenceClassificationId classificationId,
            CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EvidenceClassificationRequirement>>([]);

    public Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceClassificationsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EvidenceClassification>>([]);

    public Task<IReadOnlyList<EvidenceClassification>>
        GetEvidenceClassificationsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
        Task.FromResult(ExistingClassifications);
}

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_RejectsClassificationForDifferentArtifact()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");
        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var classifications = new RecordingClassificationRepository
        {
            ReturnAll = true,
            ExistingClassifications =
            [
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-wrong"),
                    ArtifactId = new ArtifactId("artifact-other"),
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Classification = EvidenceClassifications.MedicalEvidence
                }
            ]
        };

        var service = new VeteransReviewerPackageDetailsService(
            new RecordingPackageService
            {
                Details = CreateDetails(packageId, artifact.Id)
            },
            evidence,
            classifications,
            new RecordingTextExtractor("reviewable text"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(packageId));
    }

    [Fact]
    public async Task GetAsync_RejectsConflictingAppendixes()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var classifications =
            new RecordingClassificationRepository
            {
                ExistingClassifications =
                [
                    new EvidenceClassification
                    {
                        Id =
                            new EvidenceClassificationId(
                                "classification-medical"),
                        ArtifactId = artifact.Id,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.MedicalEvidence
                    },
                    new EvidenceClassification
                    {
                        Id =
                            new EvidenceClassificationId(
                                "classification-lay"),
                        ArtifactId = artifact.Id,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Classification =
                            EvidenceClassifications.LayEvidence
                    }
                ]
            };

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence,
                classifications,
                new RecordingTextExtractor("reviewable text"));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(packageId));

        Assert.Contains(
            artifact.Id.Value,
            exception.Message);
    }
}

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_RendersUnderlyingEvidenceForPrinting()
    {
        var packageId = new EvidencePackageId("package-print-1");
        var artifact = CreateArtifact("artifact-print-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var printRenderer =
            new RecordingPrintRenderer(
            [
                new PrintableArtifactPage
                {
                    PageNumber = 1,
                    ContentType = "image/png",
                    Content = new byte[] { 1, 2, 3 }
                }
            ]);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(packageId, artifact.Id)
                },
                evidence,
                new RecordingClassificationRepository(),
                new RecordingTextExtractor("reviewable text"),
                printRenderer);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);

        var content = Assert.Single(result.ArtifactContents);
        var page = Assert.Single(content.PrintablePages);

        Assert.Equal(artifact.Id, printRenderer.ArtifactId);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("image/png", page.ContentType);
    }

    [Fact]
    public async Task GetAsync_RejectsUnderlyingEvidenceWithoutPrintablePages()
    {
        var packageId = new EvidencePackageId("package-print-2");
        var artifact = CreateArtifact("artifact-print-2");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(packageId, artifact.Id)
                },
                evidence,
                new RecordingClassificationRepository(),
                new RecordingTextExtractor("reviewable text"),
                new RecordingPrintRenderer([]));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(packageId));

        Assert.Contains(artifact.Id.Value, exception.Message);
        Assert.Contains("printable", exception.Message);
    }
}

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_RetainsTextlessUnderlyingEvidenceWhenPrintable()
    {
        var packageId = new EvidencePackageId("package-print-3");
        var artifact = CreateArtifact("artifact-print-3");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(packageId, artifact.Id)
                },
                evidence,
                new RecordingClassificationRepository(),
                new RecordingTextExtractor(null),
                new RecordingPrintRenderer(
                [
                    new PrintableArtifactPage
                    {
                        PageNumber = 1,
                        ContentType = "image/png",
                        Content = new byte[] { 4, 5, 6 }
                    }
                ]));

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);

        var content = Assert.Single(result.ArtifactContents);

        Assert.Equal(string.Empty, content.Text);
        Assert.Single(content.PrintablePages);
    }

    [Fact]
    public async Task GetAsync_DoesNotPrintGeneratedOrganizationalMaterial()
    {
        var packageId = new EvidencePackageId("package-print-4");
        var artifact = CreateArtifact("artifact-print-4");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var details =
            new EvidencePackageDetails
            {
                Package = new EvidencePackage
                {
                    Id = packageId,
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Purpose = "Physician reviewer package",
                    ReviewerRole = "MedicalProfessional"
                },
                Artifacts =
                [
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = packageId,
                        ArtifactId = artifact.Id,
                        ContentRole =
                            EvidencePackageContentRoles
                                .GeneratedOrganizationalMaterial
                    }
                ]
            };

        var printRenderer = new RecordingPrintRenderer([]);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService { Details = details },
                evidence,
                new RecordingClassificationRepository(),
                new RecordingTextExtractor("generated content"),
                printRenderer);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);
        Assert.Single(result.ArtifactContents);
        Assert.Equal(0, printRenderer.CallCount);
    }
}

file sealed class RecordingPrintRenderer(
    IReadOnlyList<PrintableArtifactPage> pages) :
    IArtifactPrintRenderer
{
    public ArtifactId? ArtifactId { get; private set; }

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        ArtifactId = artifactId;
        CallCount++;
        return Task.FromResult(pages);
    }
}

public sealed partial class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_AssignsMedicalLiteratureAppendix()
    {
        var packageId = new EvidencePackageId("package-literature");
        var artifact = CreateArtifact("artifact-literature");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var classifications = new RecordingClassificationRepository
        {
            ExistingClassifications =
            [
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-lit"),
                    ArtifactId = artifact.Id,
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Classification = EvidenceClassifications.MedicalEvidence
                }
            ]
        };

        var literature = new RecordingMedicalLiteratureRepository
        {
            ArtifactId = artifact.Id,
            SourceId = new MedicalLiteratureSourceId("study-1")
        };

        var service = new VeteransReviewerPackageDetailsService(
            new RecordingPackageService
            {
                Details = CreateDetails(packageId, artifact.Id)
            },
            evidence,
            classifications,
            new RecordingTextExtractor("literature text"),
            literature);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);
        Assert.Equal(
            VeteransReviewerPackageAppendix.MedicalLiterature,
            Assert.Single(result.ArtifactContents).Appendix);
    }
}

file sealed class RecordingMedicalLiteratureRepository :
    IMedicalLiteratureRepository
{
    public required ArtifactId ArtifactId { get; init; }
    public required MedicalLiteratureSourceId SourceId { get; init; }

    public Task AddMedicalLiteratureSourceAsync(
        MedicalLiteratureSource source,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<MedicalLiteratureSource?> GetMedicalLiteratureSourceAsync(
        MedicalLiteratureSourceId sourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task AddRequirementMedicalLiteratureAsync(
        RequirementMedicalLiterature literature,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<RequirementMedicalLiterature>>
        GetRequirementMedicalLiteratureAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<MedicalLiteratureSourceId>>
        GetMedicalLiteratureSourceIdsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MedicalLiteratureSourceId>>(
            artifactId == ArtifactId ? [SourceId] : []);
}
