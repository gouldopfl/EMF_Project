using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Persistence.Repositories;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class MedicalLiteratureClassificationCoordinatorTests
{
    [Fact]
    public async Task ClassifyAsync_ResolvesAndTracesInputs()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature = new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var regulatory = new SqliteRegulatoryRepository(path);
            var sourceId = new MedicalLiteratureSourceId("source-a");
            var artifactId = new ArtifactId("artifact-a");
            var requirementId = new RequirementId("requirement-a");

            await literature.AddMedicalLiteratureSourceAsync(
                new MedicalLiteratureSource
                {
                    Id = sourceId,
                    Title = "Test article",
                    Authors = "Test Author",
                    Publication = "Test Journal",
                    VaAffiliated = false,
                    VaFunded = false,
                    PeerReviewed = true
                });

            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = artifactId,
                    Name = "article.xml",
                    ArtifactType = "xml"
                });

            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = sourceId,
                    ArtifactId = artifactId
                });

            var authority =
                new RegulatoryAuthority
                {
                    Id = new("authority-a"),
                    AuthorityType = "Regulation",
                    Citation = "38 CFR",
                    Title = "Test authority"
                };

            await regulatory.AddRegulatoryAuthorityAsync(
                authority);

            var provision =
                new RegulatoryProvision
                {
                    Id = new("provision-a"),
                    RegulatoryAuthorityId = authority.Id,
                    ProvisionType =
                        RegulatoryProvisionTypes.Requirement,
                    Citation = "38 CFR 3.310"
                };

            await regulatory.AddRegulatoryProvisionAsync(
                provision);

            await regulatory.AddRequirementAsync(
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provision.Id,
                    Description = "Candidate requirement."
                });

            var capabilityResult =
                new IntelligenceCapabilityResult<string>
                {
                    Success = false,
                    Message = "Unavailable.",
                    RequiresReview = true,
                    Metadata = Metadata()
                };

            var executor = new FakeExecutor(capabilityResult);
            var extractor = new FakeTextExtractor("Article text.");

            var coordinator =
                new MedicalLiteratureClassificationCoordinator(
                    literature,
                    regulatory,
                    extractor,
                    executor);

            var result = await coordinator.ClassifyAsync(
                sourceId,
                artifactId,
                [requirementId],
                Context());

            Assert.Same(capabilityResult, result.IntelligenceResult);
            Assert.Equal(artifactId, extractor.ArtifactId);
            Assert.Contains(artifactId, executor.Context!.InputArtifactIds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeTextExtractor(string? text) :
        IArtifactTextExtractor
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

    private sealed class FakeExecutor(
        IntelligenceCapabilityResult<string> result) :
        IIntelligenceCapabilityExecutor<TextStructuredExtractionRequest, string>
    {
        public IntelligenceExecutionContext? Context { get; private set; }

        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(result);
        }
    }

    private static IntelligenceExecutionContext Context() =>
        new(
            "security-steward",
            new IntelligenceCorrelationId("literature-coordinator-test"),
            new ProtectionClassificationId("confidential"),
            []);

    private static IntelligenceExecutionMetadata Metadata() =>
        new()
        {
            CapabilityId = IntelligenceCapabilityIds.TextStructuredExtraction,
            ProviderId = new IntelligenceProviderId("development.test"),
            CorrelationId =
                new IntelligenceCorrelationId("literature-coordinator-test"),
            EngineName = "test",
            StartedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task ClassifyAsync_RejectsUnassociatedArtifact()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature =
                new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var regulatory =
                new SqliteRegulatoryRepository(path);

            var sourceId =
                new MedicalLiteratureSourceId("source-unlinked");

            await literature.AddMedicalLiteratureSourceAsync(
                new MedicalLiteratureSource
                {
                    Id = sourceId,
                    Title = "Test article",
                    Authors = "Test Author",
                    Publication = "Test Journal",
                    VaAffiliated = false,
                    VaFunded = false,
                    PeerReviewed = true
                });

            var extractor = new FakeTextExtractor("Article text.");

            var executor = new FakeExecutor(
                new IntelligenceCapabilityResult<string>
                {
                    Success = false,
                    Message = "Should not execute.",
                    RequiresReview = true,
                    Metadata = Metadata()
                });

            var coordinator =
                new MedicalLiteratureClassificationCoordinator(
                    literature,
                    regulatory,
                    extractor,
                    executor);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ClassifyAsync(
                    sourceId,
                    new ArtifactId("artifact-unlinked"),
                    [],
                    Context()));

            Assert.Null(extractor.ArtifactId);
            Assert.Null(executor.Context);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ClassifyAsync_RejectsUnknownRequirement()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature =
                new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var regulatory =
                new SqliteRegulatoryRepository(path);

            var sourceId =
                new MedicalLiteratureSourceId("source-unknown");
            var artifactId =
                new ArtifactId("artifact-unknown");

            await literature.AddMedicalLiteratureSourceAsync(
                new MedicalLiteratureSource
                {
                    Id = sourceId,
                    Title = "Test article",
                    Authors = "Test Author",
                    Publication = "Test Journal",
                    VaAffiliated = false,
                    VaFunded = false,
                    PeerReviewed = true
                });

            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = artifactId,
                    Name = "article.xml",
                    ArtifactType = "xml"
                });

            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = sourceId,
                    ArtifactId = artifactId
                });

            var extractor =
                new FakeTextExtractor("Article text.");

            var executor = new FakeExecutor(
                new IntelligenceCapabilityResult<string>
                {
                    Success = false,
                    Message = "Should not execute.",
                    RequiresReview = true,
                    Metadata = Metadata()
                });

            var coordinator =
                new MedicalLiteratureClassificationCoordinator(
                    literature,
                    regulatory,
                    extractor,
                    executor);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ClassifyAsync(
                    sourceId,
                    artifactId,
                    [new RequirementId("requirement-missing")],
                    Context()));

            Assert.Null(extractor.ArtifactId);
            Assert.Null(executor.Context);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ClassifyAsync_RejectsMissingExtractedText()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature =
                new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var regulatory =
                new SqliteRegulatoryRepository(path);

            var sourceId =
                new MedicalLiteratureSourceId("source-no-text");
            var artifactId =
                new ArtifactId("artifact-no-text");

            await literature.AddMedicalLiteratureSourceAsync(
                new MedicalLiteratureSource
                {
                    Id = sourceId,
                    Title = "Test article",
                    Authors = "Test Author",
                    Publication = "Test Journal",
                    VaAffiliated = false,
                    VaFunded = false,
                    PeerReviewed = true
                });

            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = artifactId,
                    Name = "article.xml",
                    ArtifactType = "xml"
                });

            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = sourceId,
                    ArtifactId = artifactId
                });

            var extractor = new FakeTextExtractor(null);

            var executor = new FakeExecutor(
                new IntelligenceCapabilityResult<string>
                {
                    Success = false,
                    Message = "Should not execute.",
                    RequiresReview = true,
                    Metadata = Metadata()
                });

            var coordinator =
                new MedicalLiteratureClassificationCoordinator(
                    literature,
                    regulatory,
                    extractor,
                    executor);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ClassifyAsync(
                    sourceId,
                    artifactId,
                    [],
                    Context()));

            Assert.Equal(artifactId, extractor.ArtifactId);
            Assert.Null(executor.Context);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ClassifyAsync_RejectsDuplicateCandidateRequirements()
    {
        var path = Path.GetTempFileName();

        try
        {
            var literature =
                new SqliteMedicalLiteratureRepository(path);
            await literature.InitializeAsync();

            var regulatory =
                new SqliteRegulatoryRepository(path);

            var extractor =
                new FakeTextExtractor("Article text.");

            var executor = new FakeExecutor(
                new IntelligenceCapabilityResult<string>
                {
                    Success = false,
                    Message = "Should not execute.",
                    RequiresReview = true,
                    Metadata = Metadata()
                });

            var coordinator =
                new MedicalLiteratureClassificationCoordinator(
                    literature,
                    regulatory,
                    extractor,
                    executor);

            var requirementId =
                new RequirementId("requirement-duplicate");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ClassifyAsync(
                    new MedicalLiteratureSourceId("source-duplicate"),
                    new ArtifactId("artifact-duplicate"),
                    [requirementId, requirementId],
                    Context()));

            Assert.Null(extractor.ArtifactId);
            Assert.Null(executor.Context);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
