using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransBoundedEvidenceInterpretationServiceTests
{
    [Fact]
    public async Task InterpretAsync_InterpretsOnlySelectedBoundedEvidence()
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

        var first = CreateBoundedArtifact("bounded-001", "2021-09-29", "3021", "3042", "645", "668");
        var second = CreateBoundedArtifact("bounded-002", "2025-11-19", "718", "729", "261", "365");

        await repository.AddArtifactAsync(first);
        await repository.AddArtifactAsync(second);
        await AddLineageAsync(repository, source, first.Id);
        await AddLineageAsync(repository, source, second.Id);

        const string firstText =
            "The claimed condition is less likely than not proximately due to coronary artery disease.";
        const string secondText =
            "Secondary nexus not established after thorough review of evidence.";

        var contentStore = new FakeContentStore();
        contentStore.Add(first.Id, firstText);
        contentStore.Add(second.Id, secondText);

        var executor = new GroundedFakeExecutor();
        var service =
            new VeteransBoundedEvidenceInterpretationService(
                new VeteransBoundedEvidenceSelectionService(repository),
                contentStore,
                executor);

        var results =
            await service.InterpretAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                Requirement(),
                source,
                TestContext());

        Assert.Equal(2, results.Count);
        Assert.Equal(2, executor.Requests.Count);
        Assert.Equal(2, contentStore.ReadCount);

        Assert.Equal(first.Id, results[0].Evidence.Artifact.Id);
        Assert.Equal(second.Id, results[1].Evidence.Artifact.Id);

        foreach (var result in results)
        {
            var interpretation =
                Assert.IsType<VeteransBoundedEvidenceInterpretation>(
                    result.Interpretation);

            Assert.Equal(
                VeteransBoundedEvidenceDirections.OpposesRequirement,
                interpretation.Direction);
            Assert.Equal(
                VeteransMedicalOpinionStandards.LessLikelyThanNot,
                interpretation.OpinionStandard);
            Assert.Equal(new RequirementId("req-causation"), interpretation.RequirementId);

            var excerpt = Assert.Single(interpretation.SourceExcerpts);
            Assert.Equal(result.Evidence.Artifact.Id, excerpt.ArtifactId);
            Assert.Equal(0, excerpt.StartOffset);
            Assert.Equal(excerpt.Text.Length, excerpt.Length);
        }

        Assert.All(
            executor.Requests,
            request =>
            {
                Assert.Contains("req-causation", request.Request.Instruction);
                Assert.Contains("secondary causal nexus", request.Request.Instruction);
                Assert.Single(request.Context.InputArtifactIds);
            });

        Assert.Contains(
            executor.Requests,
            call => call.Request.Text == firstText &&
                    call.Context.InputArtifactIds.Contains(first.Id));
        Assert.Contains(
            executor.Requests,
            call => call.Request.Text == secondText &&
                    call.Context.InputArtifactIds.Contains(second.Id));
    }

    [Fact]
    public async Task InterpretAsync_TargetsOnlyRequestedBoundedArtifact()
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

        var first = CreateBoundedArtifact("bounded-001", "2021-09-29", "3021", "3042", "645", "668");
        var second = CreateBoundedArtifact("bounded-002", "2025-11-19", "718", "729", "261", "365");

        await repository.AddArtifactAsync(first);
        await repository.AddArtifactAsync(second);
        await AddLineageAsync(repository, source, first.Id);
        await AddLineageAsync(repository, source, second.Id);

        var contentStore = new FakeContentStore();
        contentStore.Add(first.Id, "First bounded opinion.");
        contentStore.Add(
            second.Id,
            "Secondary nexus not established after thorough review of evidence.");

        var executor = new GroundedFakeExecutor();
        var service =
            new VeteransBoundedEvidenceInterpretationService(
                new VeteransBoundedEvidenceSelectionService(repository),
                contentStore,
                executor);

        var results =
            await service.InterpretAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                Requirement(),
                source,
                TestContext(),
                second.Id);

        var result = Assert.Single(results);
        Assert.Equal(second.Id, result.Evidence.Artifact.Id);
        Assert.Single(executor.Requests);
        Assert.Equal(1, contentStore.ReadCount);
        Assert.Equal(
            second.Id,
            Assert.Single(executor.Requests).Context.InputArtifactIds.Single());
    }

    [Fact]
    public async Task InterpretAsync_DerivesSourceExcerptOffsetsDeterministically()
    {
        var fixture =
            await CreateSingleFixtureAsync(
                "Prefix. Exact quote. Suffix.");
        var executor =
            new ExactExcerptFakeExecutor("Exact quote.");

        var service =
            new VeteransBoundedEvidenceInterpretationService(
                fixture.Selection,
                fixture.ContentStore,
                executor);

        var result =
            Assert.Single(
                await service.InterpretAsync(
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    Requirement(),
                    fixture.Source,
                    TestContext()));

        var interpretation =
            Assert.IsType<VeteransBoundedEvidenceInterpretation>(
                result.Interpretation);
        var excerpt = Assert.Single(interpretation.SourceExcerpts);

        Assert.Equal("Exact quote.", excerpt.Text);
        Assert.Equal(8, excerpt.StartOffset);
        Assert.Equal(12, excerpt.Length);
    }

    [Fact]
    public async Task InterpretAsync_GroundsExcerptAcrossWhitespaceDifferences()
    {
        const string sourceText =
            "Prefix. Medical literature review & pertinent evidence review\n" +
            "does not support claim. Suffix.";
        const string modelExcerpt =
            "Medical literature review & pertinent evidence review " +
            "does not support claim.";

        var fixture = await CreateSingleFixtureAsync(sourceText);
        var executor =
            new ExactExcerptFakeExecutor(modelExcerpt);

        var service =
            new VeteransBoundedEvidenceInterpretationService(
                fixture.Selection,
                fixture.ContentStore,
                executor);

        var result =
            Assert.Single(
                await service.InterpretAsync(
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    Requirement(),
                    fixture.Source,
                    TestContext()));

        var interpretation =
            Assert.IsType<VeteransBoundedEvidenceInterpretation>(
                result.Interpretation);
        var excerpt = Assert.Single(interpretation.SourceExcerpts);

        Assert.Equal(
            "Medical literature review & pertinent evidence review\n" +
            "does not support claim.",
            excerpt.Text);
        Assert.Equal(8, excerpt.StartOffset);
        Assert.Equal(excerpt.Text.Length, excerpt.Length);
    }

    [Fact]
    public async Task InterpretAsync_RejectsAmbiguousWhitespaceNormalizedExcerpt()
    {
        var fixture =
            await CreateSingleFixtureAsync(
                "Exact\nquote. Between. Exact\tquote.");
        var executor =
            new ExactExcerptFakeExecutor("Exact quote.");

        var service =
            new VeteransBoundedEvidenceInterpretationService(
                fixture.Selection,
                fixture.ContentStore,
                executor);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InterpretAsync(
                    new ClaimIssueId("issue-osa"),
                    new ServiceConnectionBasisId("basis-osa-secondary"),
                    Requirement(),
                    fixture.Source,
                    TestContext()));

        Assert.Contains(
            "ambiguous",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InterpretAsync_NoSelectedEvidenceMakesNoIntelligenceCall()
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

        var contentStore = new FakeContentStore();
        var executor = new GroundedFakeExecutor();

        var service =
            new VeteransBoundedEvidenceInterpretationService(
                new VeteransBoundedEvidenceSelectionService(repository),
                contentStore,
                executor);

        var results =
            await service.InterpretAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                Requirement(),
                source,
                TestContext());

        Assert.Empty(results);
        Assert.Empty(executor.Requests);
        Assert.Equal(0, contentStore.ReadCount);
    }

    [Fact]
    public async Task InterpretAsync_RejectsUngroundedSourceExcerpt()
    {
        var fixture = await CreateSingleFixtureAsync("Actual bounded opinion text.");
        var executor = new UngroundedFakeExecutor();

        var service =
            new VeteransBoundedEvidenceInterpretationService(
                fixture.Selection,
                fixture.ContentStore,
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InterpretAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                Requirement(),
                fixture.Source,
                TestContext()));
    }

    [Fact]
    public async Task InterpretAsync_RejectsMismatchedRequirement()
    {
        var fixture = await CreateSingleFixtureAsync("Less likely than not.");
        var executor = new MismatchedRequirementFakeExecutor();

        var service =
            new VeteransBoundedEvidenceInterpretationService(
                fixture.Selection,
                fixture.ContentStore,
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InterpretAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                Requirement(),
                fixture.Source,
                TestContext()));
    }

    [Fact]
    public async Task InterpretAsync_RejectsMissingBoundedContentBeforeIntelligence()
    {
        var repository =
            new TestInfrastructure.InMemoryEvidenceRepository();
        var source = new ArtifactId("blue-button-001");
        var bounded = CreateBoundedArtifact("bounded-001", "2025-11-19", "718", "729", "261", "365");

        await repository.AddArtifactAsync(
            new Artifact
            {
                Id = source,
                Name = "VA Blue Button Report",
                ArtifactType = "pdf"
            });
        await repository.AddArtifactAsync(bounded);
        await AddLineageAsync(repository, source, bounded.Id);

        var contentStore = new FakeContentStore();
        var executor = new GroundedFakeExecutor();

        var service =
            new VeteransBoundedEvidenceInterpretationService(
                new VeteransBoundedEvidenceSelectionService(repository),
                contentStore,
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InterpretAsync(
                new ClaimIssueId("issue-osa"),
                new ServiceConnectionBasisId("basis-osa-secondary"),
                Requirement(),
                source,
                TestContext()));

        Assert.Empty(executor.Requests);
    }

    private static async Task<Fixture> CreateSingleFixtureAsync(string text)
    {
        var repository = new TestInfrastructure.InMemoryEvidenceRepository();
        var source = new ArtifactId("blue-button-001");
        var bounded = CreateBoundedArtifact("bounded-001", "2025-11-19", "718", "729", "261", "365");

        await repository.AddArtifactAsync(
            new Artifact
            {
                Id = source,
                Name = "VA Blue Button Report",
                ArtifactType = "pdf"
            });
        await repository.AddArtifactAsync(bounded);
        await AddLineageAsync(repository, source, bounded.Id);

        var contentStore = new FakeContentStore();
        contentStore.Add(bounded.Id, text);

        return new Fixture(
            source,
            new VeteransBoundedEvidenceSelectionService(repository),
            contentStore);
    }

    private static Artifact CreateBoundedArtifact(
        string id,
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
                VeteransBoundedEvidenceSelectionService.BoundedEvidenceArtifactType,
            Metadata = new Dictionary<string, object>
            {
                [VeteransArtifactMetadataKeys.ClaimIssueId] = "issue-osa",
                [VeteransArtifactMetadataKeys.ServiceConnectionBasisId] = "basis-osa-secondary",
                [VeteransArtifactMetadataKeys.RequirementIds] = new[] { "req-causation" },
                [VeteransArtifactMetadataKeys.SourceStartPage] = startPage,
                [VeteransArtifactMetadataKeys.SourceEndPage] = endPage,
                [VeteransArtifactMetadataKeys.SourceStartLine] = startLine,
                [VeteransArtifactMetadataKeys.SourceEndLine] = endLine,
                [VeteransArtifactMetadataKeys.EvidenceDate] = evidenceDate,
                [VeteransArtifactMetadataKeys.EvidenceTitle] = "C&P PROGRESS NOTE"
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

    private static Requirement Requirement() =>
        new()
        {
            Id = new RequirementId("req-causation"),
            RegulatoryProvisionId = new RegulatoryProvisionId("38-cfr-3.310-a"),
            Description = "secondary causal nexus"
        };

    private static IntelligenceExecutionContext TestContext() =>
        new(
            "security-steward",
            new IntelligenceCorrelationId("bounded-interpret-test"),
            new ProtectionClassificationId("confidential"),
            Array.Empty<ArtifactId>());

    private static IntelligenceExecutionMetadata Metadata(
        IntelligenceExecutionContext context) =>
        new()
        {
            CapabilityId = IntelligenceCapabilityIds.TextStructuredExtraction,
            ProviderId = new IntelligenceProviderId("development.fake"),
            CorrelationId = context.CorrelationId,
            EngineName = "fake",
            StartedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };

    private static IntelligenceCapabilityResult<string> Result(
        IntelligenceExecutionContext context,
        string output) =>
        new()
        {
            Success = true,
            Output = output,
            RequiresReview = true,
            SourceArtifactIds = context.InputArtifactIds.ToArray(),
            Metadata = Metadata(context)
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
        IIntelligenceCapabilityExecutor<TextStructuredExtractionRequest, string>
    {
        public List<(TextStructuredExtractionRequest Request, IntelligenceExecutionContext Context)>
            Requests { get; } = new();

        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((request, context));
            Assert.Equal(IntelligenceCapabilityIds.TextStructuredExtraction, capabilityId);
            Assert.Equal(
                VeteransBoundedEvidenceInterpretationService
                    .MaximumStructuredOutputTokenCount,
                request.MaximumOutputTokenCount);

            Assert.DoesNotContain(
                "startOffset",
                request.JsonSchema,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "length",
                request.JsonSchema,
                StringComparison.OrdinalIgnoreCase);

            var excerpt = request.Text;
            var output = JsonSerializer.Serialize(
                new
                {
                    requirementId = "req-causation",
                    direction = VeteransBoundedEvidenceDirections.OpposesRequirement,
                    opinionStandard = VeteransMedicalOpinionStandards.LessLikelyThanNot,
                    medicalConclusion = "The medical opinion weighs against secondary causation.",
                    rationaleSummary = "The examiner did not establish a secondary nexus.",
                    sourceExcerpts = new[]
                    {
                        new
                        {
                            text = excerpt
                        }
                    }
                });

            return Task.FromResult(Result(context, output));
        }
    }

    private sealed class ExactExcerptFakeExecutor :
        IIntelligenceCapabilityExecutor<TextStructuredExtractionRequest, string>
    {
        private readonly string _excerpt;

        public ExactExcerptFakeExecutor(string excerpt)
        {
            _excerpt = excerpt;
        }

        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            var output = JsonSerializer.Serialize(
                new
                {
                    requirementId = "req-causation",
                    direction = VeteransBoundedEvidenceDirections.OpposesRequirement,
                    opinionStandard = VeteransMedicalOpinionStandards.LessLikelyThanNot,
                    medicalConclusion = "Negative opinion.",
                    rationaleSummary = "Negative rationale.",
                    sourceExcerpts = new[]
                    {
                        new
                        {
                            text = _excerpt
                        }
                    }
                });

            return Task.FromResult(Result(context, output));
        }
    }

    private sealed class UngroundedFakeExecutor :
        IIntelligenceCapabilityExecutor<TextStructuredExtractionRequest, string>
    {
        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            var output = JsonSerializer.Serialize(
                new
                {
                    requirementId = "req-causation",
                    direction = VeteransBoundedEvidenceDirections.OpposesRequirement,
                    opinionStandard = VeteransMedicalOpinionStandards.LessLikelyThanNot,
                    medicalConclusion = "Negative opinion.",
                    rationaleSummary = "Negative rationale.",
                    sourceExcerpts = new[]
                    {
                        new
                        {
                            text = "This text is not in the source."
                        }
                    }
                });

            return Task.FromResult(Result(context, output));
        }
    }

    private sealed class MismatchedRequirementFakeExecutor :
        IIntelligenceCapabilityExecutor<TextStructuredExtractionRequest, string>
    {
        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            var output = JsonSerializer.Serialize(
                new
                {
                    requirementId = "req-aggravation",
                    direction = VeteransBoundedEvidenceDirections.OpposesRequirement,
                    opinionStandard = VeteransMedicalOpinionStandards.LessLikelyThanNot,
                    medicalConclusion = "Negative opinion.",
                    rationaleSummary = "Negative rationale.",
                    sourceExcerpts = new[]
                    {
                        new
                        {
                            text = request.Text
                        }
                    }
                });

            return Task.FromResult(Result(context, output));
        }
    }

    private sealed record Fixture(
        ArtifactId Source,
        VeteransBoundedEvidenceSelectionService Selection,
        FakeContentStore ContentStore);
}
