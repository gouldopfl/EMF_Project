using System.Text;
using System.Text.Json;
using EMF.ConsoleApplication;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Persistence.Repositories;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransBoundedEvidenceInterpretationConsoleCommandTests
{
    [Fact]
    public async Task RunBoundedEvidenceInterpretAsync_PrintsGroundedInterpretationAndTelemetry()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var requirement =
                await AddRequirementAsync(databasePath);

            var source = new ArtifactId("blue-button-001");
            var bounded = new ArtifactId("bounded-001");
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();
            await repository.AddArtifactAsync(
                new Artifact
                {
                    Id = source,
                    Name = "VA Blue Button Report",
                    ArtifactType = "pdf"
                });
            await repository.AddArtifactAsync(
                CreateBoundedArtifact(bounded, requirement.Id));
            await AddLineageAsync(repository, source, bounded);

            const string text =
                "The claimed condition is less likely than not proximately due to coronary artery disease.";

            var contentStore = new FakeContentStore();
            contentStore.Add(bounded, text);

            var executor = new GroundedFakeExecutor(requirement.Id);
            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunBoundedEvidenceInterpretAsync(
                    databasePath,
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    requirement.Id,
                    source,
                    () => Task.FromResult(Runtime(executor)),
                    contentStore,
                    output);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Equal(1, executor.CallCount);
            Assert.Equal(1, contentStore.ReadCount);
            Assert.Contains("Mode                : BOUNDED INTERPRET", rendered);
            Assert.Contains("Bounded Evidence    : 1", rendered);
            Assert.Contains("Status              : INTERPRETED", rendered);
            Assert.Contains("Direction           : OpposesRequirement", rendered);
            Assert.Contains("Opinion Standard    : LessLikelyThanNot", rendered);
            Assert.Contains("Requires Review     : True", rendered);
            Assert.Contains("Provider            : development.fake", rendered);
            Assert.Contains("Input Tokens        : 120", rendered);
            Assert.Contains("Output Tokens       : 30", rendered);
            Assert.Contains("Total Tokens        : 150", rendered);
            Assert.Contains("Estimated Cost USD  : 0.00021", rendered);
            Assert.Contains("Interpreted         : 1", rendered);
            Assert.Contains("Failed              : 0", rendered);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task RunBoundedEvidenceInterpretAsync_TargetsOneOfTwoSelectedArtifacts()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var requirement =
                await AddRequirementAsync(databasePath);

            var source = new ArtifactId("blue-button-001");
            var first = new ArtifactId("bounded-001");
            var second = new ArtifactId("bounded-002");
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();
            await repository.AddArtifactAsync(
                new Artifact
                {
                    Id = source,
                    Name = "VA Blue Button Report",
                    ArtifactType = "pdf"
                });
            await repository.AddArtifactAsync(
                CreateBoundedArtifact(first, requirement.Id));
            await repository.AddArtifactAsync(
                CreateBoundedArtifact(second, requirement.Id));
            await AddLineageAsync(repository, source, first);
            await AddLineageAsync(repository, source, second);

            var contentStore = new FakeContentStore();
            contentStore.Add(first, "First bounded opinion.");
            contentStore.Add(
                second,
                "The claimed condition is less likely than not proximately due to coronary artery disease.");

            var executor = new GroundedFakeExecutor(requirement.Id);
            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunBoundedEvidenceInterpretAsync(
                    databasePath,
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    requirement.Id,
                    source,
                    () => Task.FromResult(Runtime(executor)),
                    contentStore,
                    output,
                    second);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Equal(1, executor.CallCount);
            Assert.Equal(1, contentStore.ReadCount);
            Assert.Contains($"Target Artifact     : {second.Value}", rendered);
            Assert.Contains("Bounded Evidence    : 1", rendered);
            Assert.Contains($"Artifact ID         : {second.Value}", rendered);
            Assert.DoesNotContain($"Artifact ID         : {first.Value}", rendered);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task RunBoundedEvidenceInterpretAsync_NoSelectedEvidenceMakesNoIntelligenceCall()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var requirement =
                await AddRequirementAsync(databasePath);

            var source = new ArtifactId("blue-button-001");
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();
            await repository.AddArtifactAsync(
                new Artifact
                {
                    Id = source,
                    Name = "VA Blue Button Report",
                    ArtifactType = "pdf"
                });

            var executor = new GroundedFakeExecutor(requirement.Id);
            var contentStore = new FakeContentStore();
            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand.RunBoundedEvidenceInterpretAsync(
                    databasePath,
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    requirement.Id,
                    source,
                    () => Task.FromResult(Runtime(executor)),
                    contentStore,
                    output);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, executor.CallCount);
            Assert.Equal(0, contentStore.ReadCount);
            Assert.Contains("Bounded Evidence    : 0", output.ToString());
            Assert.Contains("Total               : 0", output.ToString());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task<Requirement> AddRequirementAsync(
        string databasePath)
    {
        var repository = new SqliteRegulatoryRepository(databasePath);
        await repository.InitializeAsync();

        var authority = new RegulatoryAuthority
        {
            Id = new RegulatoryAuthorityId("authority-38-cfr"),
            AuthorityType = "Regulation",
            Citation = "38 CFR",
            Title = "Veterans Affairs"
        };

        await repository.AddRegulatoryAuthorityAsync(authority);

        var provision = new RegulatoryProvision
        {
            Id = new RegulatoryProvisionId("provision-3-310-a"),
            RegulatoryAuthorityId = authority.Id,
            ProvisionType = RegulatoryProvisionTypes.Requirement,
            Citation = "38 CFR 3.310(a)"
        };

        await repository.AddRegulatoryProvisionAsync(provision);

        var requirement = new Requirement
        {
            Id = new RequirementId("req-causation"),
            RegulatoryProvisionId = provision.Id,
            Description = "secondary causal nexus"
        };

        await repository.AddRequirementAsync(requirement);
        return requirement;
    }

    private static Artifact CreateBoundedArtifact(
        ArtifactId id,
        RequirementId requirementId) =>
        new()
        {
            Id = id,
            Name = "bounded-evidence.txt",
            ArtifactType =
                VeteransBoundedEvidenceSelectionService
                    .BoundedEvidenceArtifactType,
            Metadata = new Dictionary<string, object>
            {
                [VeteransArtifactMetadataKeys.ClaimIssueId] = "issue-osa",
                [VeteransArtifactMetadataKeys.ServiceConnectionBasisId] =
                    "basis-osa-secondary",
                [VeteransArtifactMetadataKeys.RequirementIds] =
                    new[] { requirementId.Value },
                [VeteransArtifactMetadataKeys.SourceStartPage] = "3021",
                [VeteransArtifactMetadataKeys.SourceEndPage] = "3042",
                [VeteransArtifactMetadataKeys.SourceStartLine] = "645",
                [VeteransArtifactMetadataKeys.SourceEndLine] = "668",
                [VeteransArtifactMetadataKeys.EvidenceDate] = "2021-09-29",
                [VeteransArtifactMetadataKeys.EvidenceTitle] =
                    "C&P PROGRESS NOTE"
            }
        };

    private static async Task AddLineageAsync(
        SqliteEvidenceRepository repository,
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

    private static TextSummarizationConsoleRuntime Runtime(
        GroundedFakeExecutor executor) =>
        new()
        {
            TextSummarizationCapabilityExecutor =
                new UnusedSummarizationExecutor(),
            TextStructuredExtractionCapabilityExecutor = executor,
            SubjectId = "console-test",
            ClassificationId =
                new ProtectionClassificationId("confidential"),
            AuditDatabasePath = "test-audit.db"
        };

    private sealed class FakeContentStore : IArtifactContentStore
    {
        private readonly Dictionary<ArtifactId, byte[]> _content = new();

        public int ReadCount { get; private set; }

        public void Add(ArtifactId artifactId, string text) =>
            _content[artifactId] = Encoding.UTF8.GetBytes(text);

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            _content[artifactId] = content.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            _content.TryGetValue(artifactId, out var content);
            return Task.FromResult(content);
        }

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            _content.Remove(artifactId);
            return Task.CompletedTask;
        }
    }

    private sealed class GroundedFakeExecutor :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string>
    {
        private readonly RequirementId _requirementId;

        public GroundedFakeExecutor(RequirementId requirementId)
        {
            _requirementId = requirementId;
        }

        public int CallCount { get; private set; }

        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            var output = JsonSerializer.Serialize(
                new
                {
                    requirementId = _requirementId.Value,
                    direction =
                        VeteransBoundedEvidenceDirections
                            .OpposesRequirement,
                    opinionStandard =
                        VeteransMedicalOpinionStandards
                            .LessLikelyThanNot,
                    medicalConclusion =
                        "The opinion weighs against secondary causation.",
                    rationaleSummary =
                        "The examiner did not establish a secondary nexus.",
                    sourceExcerpts = new[]
                    {
                        new
                        {
                            text = request.Text,
                            startOffset = 0,
                            length = request.Text.Length
                        }
                    }
                });

            return Task.FromResult(
                new IntelligenceCapabilityResult<string>
                {
                    Success = true,
                    Output = output,
                    RequiresReview = true,
                    SourceArtifactIds =
                        context.InputArtifactIds.ToArray(),
                    Metadata = new IntelligenceExecutionMetadata
                    {
                        CapabilityId = capabilityId,
                        ProviderId =
                            new IntelligenceProviderId("development.fake"),
                        CorrelationId = context.CorrelationId,
                        EngineName = "fake-structured-extractor",
                        InputTokenCount = 120,
                        OutputTokenCount = 30,
                        TotalTokenCount = 150,
                        InputCostUsdPerMillionTokens = 1m,
                        OutputCostUsdPerMillionTokens = 3m,
                        EstimatedCostUsd = 0.00021m,
                        StartedUtc = DateTimeOffset.UtcNow,
                        CompletedUtc = DateTimeOffset.UtcNow
                    }
                });
        }
    }

    private sealed class UnusedSummarizationExecutor :
        IIntelligenceCapabilityExecutor<TextSummarizationRequest, string>
    {
        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextSummarizationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Summarization should not be used by bounded interpretation.");
    }
}
