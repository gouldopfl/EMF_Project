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
using EMF.Persistence.Repositories;
using EMF.Security.Auditing.Models;
using EMF.Security.Persistence.Sqlite.Auditing;

namespace EMF.Tests;

public sealed class VeteransBoundedEvidenceReviewConsoleServiceTests
{
    [Fact]
    public async Task ReviewAsync_PromotesAuditedGroundedReceiptWithoutIntelligenceCall()
    {
        var databasePath = Path.GetTempFileName();
        var auditPath = Path.GetTempFileName();
        var receiptPath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath).InitializeAsync();
            var requirement = await AddRequirementAsync(databasePath);
            var source = new ArtifactId("blue-button-review");
            var bounded = new ArtifactId("bounded-review");
            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = source,
                    Name = "VA Blue Button Report",
                    ArtifactType = "pdf"
                });
            await evidence.AddArtifactAsync(
                CreateBoundedArtifact(bounded, requirement.Id));
            await AddLineageAsync(evidence, source, bounded);

            const string text =
                "The claimed condition is less likely than not proximately due to coronary artery disease.";
            var store = new FakeContentStore();
            store.Add(bounded, text);

            const string correlation =
                "veterans-bounded-interpret-review-test";
            await AddAuditAsync(auditPath, correlation);

            var receipt =
                new
                {
                    artifactId = bounded.Value,
                    requirementId = requirement.Id.Value,
                    correlationId = correlation,
                    direction = VeteransBoundedEvidenceDirections.OpposesRequirement,
                    opinionStandard = VeteransMedicalOpinionStandards.LessLikelyThanNot,
                    medicalConclusion = "The condition is less likely than not due to CAD.",
                    rationaleSummary = "The opinion does not establish secondary causation.",
                    requiresReview = true,
                    warnings = Array.Empty<string>(),
                    sourceExcerpts =
                        new[]
                        {
                            new
                            {
                                text,
                                startOffset = 0,
                                length = text.Length
                            }
                        }
                };
            await File.WriteAllTextAsync(
                receiptPath,
                JsonSerializer.Serialize(receipt));

            using var output = new StringWriter();
            var exitCode =
                await VeteransBoundedEvidenceReviewConsoleService.ReviewAsync(
                    databasePath,
                    auditPath,
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    requirement.Id,
                    source,
                    receiptPath,
                    store,
                    "reviewer@example.test",
                    output);

            Assert.Equal(0, exitCode);
            Assert.Contains("Azure Intelligence  : NOT USED", output.ToString());
            Assert.Contains("Status              : PROMOTED", output.ToString());

            var repository =
                new SqliteBoundedEvidenceInterpretationRepository(databasePath);
            var stored = Assert.Single(
                await repository.GetReviewedInterpretationsAsync(bounded));

            Assert.Equal(correlation, stored.CorrelationId);
            Assert.Equal("reviewer@example.test", stored.ReviewedBy);
            Assert.Equal("console-test", stored.PromotedBy);
            Assert.Equal("azure.openai", stored.ProviderId);
            Assert.Equal("gpt-4.1", stored.EngineName);
            Assert.Equal(604, stored.InputTokenCount);
            Assert.Equal(266, stored.OutputTokenCount);
            Assert.Equal(870, stored.TotalTokenCount);
            Assert.Equal(0.0036696m, stored.EstimatedCostUsd);
            Assert.Equal(text, Assert.Single(stored.SourceExcerpts).Text);
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(auditPath);
            File.Delete(receiptPath);
        }
    }

    [Fact]
    public async Task ReviewAsync_RejectsReceiptExcerptThatDoesNotMatchBoundedArtifact()
    {
        var databasePath = Path.GetTempFileName();
        var auditPath = Path.GetTempFileName();
        var receiptPath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath).InitializeAsync();
            var requirement = await AddRequirementAsync(databasePath);
            var source = new ArtifactId("blue-button-review-bad");
            var bounded = new ArtifactId("bounded-review-bad");
            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = source,
                    Name = "VA Blue Button Report",
                    ArtifactType = "pdf"
                });
            await evidence.AddArtifactAsync(
                CreateBoundedArtifact(bounded, requirement.Id));
            await AddLineageAsync(evidence, source, bounded);

            var store = new FakeContentStore();
            store.Add(bounded, "Exact source text.");

            const string correlation =
                "veterans-bounded-interpret-review-bad";
            await AddAuditAsync(auditPath, correlation);

            await File.WriteAllTextAsync(
                receiptPath,
                JsonSerializer.Serialize(
                    new
                    {
                        artifactId = bounded.Value,
                        requirementId = requirement.Id.Value,
                        correlationId = correlation,
                        direction = VeteransBoundedEvidenceDirections.OpposesRequirement,
                        opinionStandard = VeteransMedicalOpinionStandards.LessLikelyThanNot,
                        medicalConclusion = "Conclusion",
                        rationaleSummary = "Rationale",
                        sourceExcerpts =
                            new[]
                            {
                                new
                                {
                                    text = "Changed source text.",
                                    startOffset = 0,
                                    length = 20
                                }
                            }
                    }));

            using var output = new StringWriter();
            var exitCode =
                await VeteransBoundedEvidenceReviewConsoleService.ReviewAsync(
                    databasePath,
                    auditPath,
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    requirement.Id,
                    source,
                    receiptPath,
                    store,
                    "reviewer@example.test",
                    output);

            Assert.Equal(1, exitCode);

            var repository =
                new SqliteBoundedEvidenceInterpretationRepository(databasePath);
            Assert.Empty(
                await repository.GetReviewedInterpretationsAsync(bounded));
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(auditPath);
            File.Delete(receiptPath);
        }
    }

    private static async Task AddAuditAsync(
        string auditPath,
        string correlation)
    {
        var sink = new SqliteSecurityAuditSink(auditPath);
        await sink.InitializeAsync();

        var started = DateTimeOffset.UtcNow.AddMinutes(-2);
        var completed = DateTimeOffset.UtcNow.AddMinutes(-1);

        await sink.WriteAsync(
            new SecurityAuditRecord
            {
                Operation = "IntelligenceCapability.Execute",
                ResourceType = "IntelligenceCapability",
                ResourceId = "text.structured.extract",
                SubjectId = "console-test",
                Destination = "azure.openai",
                Outcome = SecurityAuditOutcome.Succeeded,
                OccurredUtc = completed,
                Facts = new Dictionary<string, string>
                {
                    ["correlationId"] = correlation,
                    ["engineName"] = "gpt-4.1",
                    ["engineVersion"] = "2025-04-14",
                    ["providerOperationId"] = "operation-review-test",
                    ["startedUtc"] = started.ToString("O"),
                    ["completedUtc"] = completed.ToString("O"),
                    ["inputTokenCount"] = "604",
                    ["outputTokenCount"] = "266",
                    ["totalTokenCount"] = "870",
                    ["estimatedCostUsd"] = "0.0036696"
                }
            });
    }

    private static async Task<Requirement> AddRequirementAsync(
        string databasePath)
    {
        var repository = new SqliteRegulatoryRepository(databasePath);
        await repository.InitializeAsync();

        var authority = new RegulatoryAuthority
        {
            Id = new RegulatoryAuthorityId("authority-review"),
            AuthorityType = "Regulation",
            Citation = "38 CFR",
            Title = "Veterans Affairs"
        };
        await repository.AddRegulatoryAuthorityAsync(authority);

        var provision = new RegulatoryProvision
        {
            Id = new RegulatoryProvisionId("provision-review"),
            RegulatoryAuthorityId = authority.Id,
            ProvisionType = RegulatoryProvisionTypes.Requirement,
            Citation = "38 CFR 3.310(a)"
        };
        await repository.AddRegulatoryProvisionAsync(provision);

        var requirement = new Requirement
        {
            Id = new RequirementId("req-review"),
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
                [VeteransArtifactMetadataKeys.SourceStartPage] = "1",
                [VeteransArtifactMetadataKeys.SourceEndPage] = "1",
                [VeteransArtifactMetadataKeys.SourceStartLine] = "1",
                [VeteransArtifactMetadataKeys.SourceEndLine] = "1",
                [VeteransArtifactMetadataKeys.EvidenceDate] = "2026-09-14",
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

    private sealed class FakeContentStore : IArtifactContentStore
    {
        private readonly Dictionary<ArtifactId, byte[]> _content = new();

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
}
