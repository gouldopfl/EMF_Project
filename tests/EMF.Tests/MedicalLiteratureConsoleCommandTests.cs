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
