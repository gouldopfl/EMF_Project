using EMF.ConsoleApplication;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Persistence.Repositories;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class MedicalLiteratureConsoleCommandTests
{
    [Fact]
    public async Task RunClassifyAsync_RendersGroundedProposal()
    {
        var databasePath = Path.GetTempFileName();
        var contentPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-literature-console-{Guid.NewGuid():N}");

        try
        {
            var sourceId = new MedicalLiteratureSourceId("source-console");
            var artifactId = new ArtifactId("artifact-console");
            var requirementId = new RequirementId("requirement-console");

            var literature = new SqliteMedicalLiteratureRepository(databasePath);
            await literature.InitializeAsync();
            await literature.AddMedicalLiteratureSourceAsync(
                new MedicalLiteratureSource
                {
                    Id = sourceId,
                    Title = "Test article",
                    Authors = "Test Author",
                    Publication = "Test Journal",
                    PeerReviewed = true
                });

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = artifactId,
                    Name = "study.txt",
                    ArtifactType = "file",
                    Metadata = new Dictionary<string, object>
                    {
                        [ArtifactMetadataKeys.FileExtension] = ".txt"
                    }
                });

            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = sourceId,
                    ArtifactId = artifactId
                });

            var regulatory = new SqliteRegulatoryRepository(databasePath);
            var authority = new RegulatoryAuthority
            {
                Id = new("authority-console"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Test authority"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new("provision-console"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };
            await regulatory.AddRegulatoryProvisionAsync(provision);
            await regulatory.AddRequirementAsync(
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provision.Id,
                    Description = "Candidate requirement."
                });

            var contentStore = new EMF.Persistence.Storage
                .FileSystemArtifactContentStore(contentPath);
            await contentStore.WriteAsync(
                artifactId,
                System.Text.Encoding.UTF8.GetBytes(
                    "PTSD was associated with OSA."));

            using var output = new StringWriter();
            var exitCode = await MedicalLiteratureConsoleCommand.RunClassifyAsync(
                databasePath,
                sourceId,
                artifactId,
                [requirementId],
                () => Task.FromResult(Runtime(requirementId)),
                contentStore,
                output);

            var rendered = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("Literature ID : source-console", rendered);
            Assert.Contains("Classifications: 1", rendered);
            Assert.Contains("Requirement   : requirement-console", rendered);
            Assert.Contains("Guidance Role : SupportsRequirement", rendered);
            Assert.Contains("offset=0 length=29", rendered);
            Assert.Contains("PTSD was associated with OSA.", rendered);
        }
        finally
        {
            File.Delete(databasePath);
            if (Directory.Exists(contentPath))
                Directory.Delete(contentPath, true);
        }
    }

    [Fact]
    public async Task RunClassifyAsync_PromotesReviewedClassification()
    {
        var databasePath = Path.GetTempFileName();
        var contentPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-literature-promote-{Guid.NewGuid():N}");

        try
        {
            var sourceId = new MedicalLiteratureSourceId("source-promote");
            var artifactId = new ArtifactId("artifact-promote");
            var requirementId = new RequirementId("requirement-promote");

            var literature = new SqliteMedicalLiteratureRepository(databasePath);
            await literature.InitializeAsync();
            await literature.AddMedicalLiteratureSourceAsync(
                new MedicalLiteratureSource
                {
                    Id = sourceId,
                    Title = "Promoted article",
                    Authors = "Test Author",
                    Publication = "Test Journal",
                    PeerReviewed = true
                });

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = artifactId,
                    Name = "promoted-study.txt",
                    ArtifactType = "file",
                    Metadata = new Dictionary<string, object>
                    {
                        [ArtifactMetadataKeys.FileExtension] = ".txt"
                    }
                });

            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = sourceId,
                    ArtifactId = artifactId
                });

            var regulatory = new SqliteRegulatoryRepository(databasePath);
            var authority = new RegulatoryAuthority
            {
                Id = new("authority-promote"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Test authority"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new("provision-promote"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };
            await regulatory.AddRegulatoryProvisionAsync(provision);
            await regulatory.AddRequirementAsync(
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provision.Id,
                    Description = "Candidate requirement."
                });

            var contentStore = new EMF.Persistence.Storage
                .FileSystemArtifactContentStore(contentPath);
            await contentStore.WriteAsync(
                artifactId,
                System.Text.Encoding.UTF8.GetBytes(
                    "PTSD was associated with OSA."));

            using var output = new StringWriter();
            var exitCode = await MedicalLiteratureConsoleCommand.RunClassifyAsync(
                databasePath,
                sourceId,
                artifactId,
                [requirementId],
                () => Task.FromResult(Runtime(requirementId)),
                contentStore,
                output,
                promote: true,
                reviewedBy: "reviewer@example.test");

            Assert.Equal(0, exitCode);
            Assert.Contains("Promoted      : 1", output.ToString());
            Assert.Contains(
                "Reviewed By   : reviewer@example.test",
                output.ToString());

            var accepted = Assert.Single(
                await literature.GetRequirementMedicalLiteratureAsync(
                    requirementId));
            Assert.Equal(sourceId, accepted.MedicalLiteratureSourceId);
            Assert.Equal(
                EvidenceGuidanceRoles.SupportsRequirement,
                accepted.GuidanceRole);

            var reviewed = Assert.Single(
                await literature.GetReviewedClassificationsAsync(
                    requirementId));
            Assert.Equal(artifactId, reviewed.ArtifactId);
            Assert.Equal("console-test", reviewed.PromotedBy);
            Assert.Equal("reviewer@example.test", reviewed.ReviewedBy);
            Assert.Equal(
                IntelligenceCapabilityIds.TextStructuredExtraction.Value,
                reviewed.CapabilityId);
            Assert.Equal("test", reviewed.ProviderId);
            Assert.Equal("test", reviewed.EngineName);
            Assert.True(reviewed.RequiresReview);
            Assert.Single(reviewed.SourceExcerpts);
        }
        finally
        {
            File.Delete(databasePath);
            if (Directory.Exists(contentPath))
                Directory.Delete(contentPath, true);
        }
    }

    [Fact]
    public async Task RunClassifyAsync_PromotionRollsBackWhenLaterReviewConflicts()
    {
        var databasePath = Path.GetTempFileName();
        var contentPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-literature-batch-promote-{Guid.NewGuid():N}");

        try
        {
            var sourceId =
                new MedicalLiteratureSourceId("source-batch-promote");
            var artifactId = new ArtifactId("artifact-batch-promote");
            var requirementId =
                new RequirementId("requirement-batch-promote");

            var literature =
                new SqliteMedicalLiteratureRepository(databasePath);
            await literature.InitializeAsync();
            await literature.AddMedicalLiteratureSourceAsync(
                new MedicalLiteratureSource
                {
                    Id = sourceId,
                    Title = "Batch promoted article",
                    Authors = "Test Author",
                    Publication = "Test Journal",
                    PeerReviewed = true
                });

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = artifactId,
                    Name = "batch-promoted-study.txt",
                    ArtifactType = "file",
                    Metadata = new Dictionary<string, object>
                    {
                        [ArtifactMetadataKeys.FileExtension] = ".txt"
                    }
                });

            await literature.AddMedicalLiteratureSourceArtifactAsync(
                new MedicalLiteratureSourceArtifact
                {
                    MedicalLiteratureSourceId = sourceId,
                    ArtifactId = artifactId
                });

            var regulatory = new SqliteRegulatoryRepository(databasePath);
            var authority = new RegulatoryAuthority
            {
                Id = new("authority-batch-promote"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Test authority"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new("provision-batch-promote"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };
            await regulatory.AddRegulatoryProvisionAsync(provision);
            await regulatory.AddRequirementAsync(
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provision.Id,
                    Description = "Candidate requirement."
                });

            var contentStore = new EMF.Persistence.Storage
                .FileSystemArtifactContentStore(contentPath);
            await contentStore.WriteAsync(
                artifactId,
                System.Text.Encoding.UTF8.GetBytes(
                    "PTSD was associated with OSA."));

            var reviewedUtc =
                new DateTimeOffset(
                    2026, 9, 12, 16, 0, 0, TimeSpan.Zero);
            await literature.AddReviewedClassificationAsync(
                new ReviewedMedicalLiteratureClassification
                {
                    Association = new RequirementMedicalLiterature
                    {
                        RequirementId = requirementId,
                        MedicalLiteratureSourceId = sourceId,
                        GuidanceRole = EvidenceGuidanceRoles.Corroborates,
                        Description = "Corroborates the candidate requirement."
                    },
                    ArtifactId = artifactId,
                    PromotedBy = "existing-promotion",
                    PromotedUtc = reviewedUtc.AddMinutes(1),
                    ReviewedBy = "existing-reviewer@example.test",
                    ReviewedUtc = reviewedUtc,
                    IntelligenceOutput = "{}",
                    CapabilityId = "TextStructuredExtraction",
                    ProviderId = "test",
                    CorrelationId = "existing-correlation",
                    EngineName = "test",
                    StartedUtc = reviewedUtc.AddMinutes(-2),
                    CompletedUtc = reviewedUtc.AddMinutes(-1),
                    RequiresReview = true,
                    Warnings = [],
                    SourceExcerpts =
                    [
                        new MedicalLiteratureSourceExcerpt
                        {
                            ArtifactId = artifactId,
                            Text = "PTSD was associated with OSA.",
                            StartOffset = 0,
                            Length = 29
                        }
                    ]
                });

            using var output = new StringWriter();
            var exitCode =
                await MedicalLiteratureConsoleCommand.RunClassifyAsync(
                    databasePath,
                    sourceId,
                    artifactId,
                    [requirementId],
                    () => Task.FromResult(MultiRuntime(requirementId)),
                    contentStore,
                    output,
                    promote: true,
                    reviewedBy: "new-reviewer@example.test");

            Assert.Equal(1, exitCode);

            var reviewed =
                await literature.GetReviewedClassificationsAsync(
                    requirementId);
            var existing = Assert.Single(reviewed);
            Assert.Equal(
                EvidenceGuidanceRoles.Corroborates,
                existing.Association.GuidanceRole);
            Assert.Equal(
                "existing-correlation",
                existing.CorrelationId);

            var accepted =
                await literature.GetRequirementMedicalLiteratureAsync(
                    requirementId);
            var acceptedLink = Assert.Single(accepted);
            Assert.Equal(
                EvidenceGuidanceRoles.Corroborates,
                acceptedLink.GuidanceRole);
        }
        finally
        {
            File.Delete(databasePath);
            if (Directory.Exists(contentPath))
                Directory.Delete(contentPath, true);
        }
    }

    [Fact]
    public async Task RunAsync_LiteratureClassifyPromoteRequiresReviewer()
    {
        var databasePath = Path.GetTempFileName();
        var previous = Environment.GetEnvironmentVariable("EMF_REVIEWED_BY");

        try
        {
            Environment.SetEnvironmentVariable("EMF_REVIEWED_BY", null);

            var exitCode = await VeteransConsoleCommand.RunAsync(
                [
                    "evidence", "literature", "classify", "--promote",
                    databasePath, "source-1", "artifact-1", "requirement-1"
                ],
                () => Task.FromResult(
                    Runtime(new RequirementId("requirement-1"))),
                () => null);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMF_REVIEWED_BY", previous);
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task RunAsync_LiteratureClassifyRejectsMissingDatabase()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"missing-literature-{Guid.NewGuid():N}.db");

        var exitCode = await VeteransConsoleCommand.RunAsync(
            [
                "evidence", "literature", "classify", path,
                "source-1", "artifact-1", "requirement-1"
            ],
            () => Task.FromResult(Runtime(new RequirementId("requirement-1"))),
            () => null);

        Assert.Equal(2, exitCode);
    }

    private static TextSummarizationConsoleRuntime Runtime(
        RequirementId requirementId) =>
        new()
        {
            TextSummarizationCapabilityExecutor = new FakeSummarizationExecutor(),
            TextStructuredExtractionCapabilityExecutor =
                new FakeStructuredExtractionExecutor(requirementId),
            SubjectId = "console-test",
            ClassificationId = new ProtectionClassificationId("confidential"),
            AuditDatabasePath = "test-audit.db"
        };

    private static TextSummarizationConsoleRuntime MultiRuntime(
        RequirementId requirementId) =>
        new()
        {
            TextSummarizationCapabilityExecutor =
                new FakeSummarizationExecutor(),
            TextStructuredExtractionCapabilityExecutor =
                new FakeMultiStructuredExtractionExecutor(requirementId),
            SubjectId = "console-test",
            ClassificationId =
                new ProtectionClassificationId("confidential"),
            AuditDatabasePath = "test-audit.db"
        };

    private sealed class FakeStructuredExtractionExecutor(
        RequirementId requirementId) :
        IIntelligenceCapabilityExecutor<TextStructuredExtractionRequest, string>
    {
        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Output = $$"""
                    {
                      "classifications": [{
                        "requirementId": "{{requirementId.Value}}",
                        "guidanceRole": "SupportsRequirement",
                        "description": "Supports the candidate requirement.",
                        "sourceExcerpts": [{
                          "text": "PTSD was associated with OSA.",
                          "startOffset": 0,
                          "length": 29
                        }]
                      }]
                    }
                    """,
                RequiresReview = true,
                Metadata = Metadata(capabilityId, context),
                SourceArtifactIds = context.InputArtifactIds.ToArray()
            });
    }

    private sealed class FakeMultiStructuredExtractionExecutor(
        RequirementId requirementId) :
        IIntelligenceCapabilityExecutor<TextStructuredExtractionRequest, string>
    {
        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Output = $$"""
                    {
                      "classifications": [{
                        "requirementId": "{{requirementId.Value}}",
                        "guidanceRole": "SupportsRequirement",
                        "description": "Supports the candidate requirement.",
                        "sourceExcerpts": [{
                          "text": "PTSD was associated with OSA.",
                          "startOffset": 0,
                          "length": 29
                        }]
                      }, {
                        "requirementId": "{{requirementId.Value}}",
                        "guidanceRole": "Corroborates",
                        "description": "Corroborates the candidate requirement.",
                        "sourceExcerpts": [{
                          "text": "PTSD was associated with OSA.",
                          "startOffset": 0,
                          "length": 29
                        }]
                      }]
                    }
                    """,
                RequiresReview = true,
                Metadata = Metadata(capabilityId, context),
                SourceArtifactIds = context.InputArtifactIds.ToArray()
            });
    }

    private sealed class FakeSummarizationExecutor :
        IIntelligenceCapabilityExecutor<TextSummarizationRequest, string>
    {
        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextSummarizationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Summarization should not execute.");
    }

    private static IntelligenceExecutionMetadata Metadata(
        IntelligenceCapabilityId capabilityId,
        IntelligenceExecutionContext context) =>
        new()
        {
            CapabilityId = capabilityId,
            ProviderId = new IntelligenceProviderId("test"),
            CorrelationId = context.CorrelationId,
            EngineName = "test",
            StartedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };
}
