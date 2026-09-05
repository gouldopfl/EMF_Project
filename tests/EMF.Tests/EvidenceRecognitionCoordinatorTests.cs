using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EvidenceRecognitionCoordinatorTests
{
    [Fact]
    public async Task RecognizeAsync_MatchesGapArtifacts()
    {
        var gapId = new EvidenceGapId("gap-001");
        var requirementId = new RequirementId("req-001");

        var gaps = new FakeGapRepository
        {
            Gap = new EvidenceGap
            {
                Id = gapId,
                ClaimIssueId = new ClaimIssueId("issue-001"),
                RequirementId = requirementId,
                Description = "Missing evidence."
            },
            Artifacts =
            [
                new EvidenceGapArtifact
                {
                    EvidenceGapId = gapId,
                    ArtifactId = new ArtifactId("artifact-001"),
                    Role = "primary"
                }
            ]
        };

        var terms =
            new InMemoryEvidenceRecognitionTermRepository();

        await terms.AddEvidenceRecognitionTermAsync(
            new EvidenceRecognitionTerm
            {
                Id = new EvidenceRecognitionTermId("term-001"),
                RequirementId = requirementId,
                Term = "instability",
                TermType = EvidenceRecognitionTermTypes.Keyword,
                RecognitionRole =
                    EvidenceRecognitionRoles.SeverityCriterion,
                EvidenceClassification =
                    EvidenceClassifications.MedicalEvidence,
                AuthoritySource = "38 CFR"
            });

        var coordinator =
            new EvidenceRecognitionCoordinator(
                gaps,
                new FakeTextExtractor(
                    "Veteran has chronic ankle instability."),
                terms);

        var result =
            await coordinator.RecognizeAsync(gapId);

        var match = Assert.Single(result.Matches);
        Assert.Equal("term-001", match.TermId.Value);
        Assert.Equal("instability", match.Term);
        Assert.Equal(
            EvidenceClassifications.MedicalEvidence,
            match.EvidenceClassification);

        var link =
            Assert.Single(result.MatchArtifacts);

        Assert.Equal(match.TermId, link.RecognitionTermId);
        Assert.Equal("artifact-001", link.ArtifactId.Value);
        Assert.Equal("primary", link.Role);
    }

    [Fact]
    public async Task RecognizeAsync_RejectsWrongReturnedGapIdentity()
    {
        var requested = new EvidenceGapId("gap-001");

        var gaps = new FakeGapRepository
        {
            Gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-other"),
                ClaimIssueId = new ClaimIssueId("issue-001"),
                RequirementId = new RequirementId("req-001"),
                Description = "Missing evidence."
            }
        };

        var coordinator =
            new EvidenceRecognitionCoordinator(
                gaps,
                new FakeTextExtractor("text"),
                new InMemoryEvidenceRecognitionTermRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RecognizeAsync(requested));
    }

    [Fact]
    public async Task RecognizeAsync_DeduplicatesTermAcrossArtifacts()
    {
        var gapId = new EvidenceGapId("gap-002");
        var requirementId = new RequirementId("req-002");

        var gaps = new FakeGapRepository
        {
            Gap = new EvidenceGap
            {
                Id = gapId,
                ClaimIssueId = new ClaimIssueId("issue-002"),
                RequirementId = requirementId,
                Description = "Missing evidence."
            },
            Artifacts =
            [
                new EvidenceGapArtifact
                {
                    EvidenceGapId = gapId,
                    ArtifactId = new ArtifactId("artifact-001"),
                    Role = "primary"
                },
                new EvidenceGapArtifact
                {
                    EvidenceGapId = gapId,
                    ArtifactId = new ArtifactId("artifact-002"),
                    Role = "supporting"
                }
            ]
        };

        var terms =
            new InMemoryEvidenceRecognitionTermRepository();

        await terms.AddEvidenceRecognitionTermAsync(
            new EvidenceRecognitionTerm
            {
                Id = new EvidenceRecognitionTermId("term-002"),
                RequirementId = requirementId,
                Term = "instability",
                TermType = EvidenceRecognitionTermTypes.Keyword,
                RecognitionRole =
                    EvidenceRecognitionRoles.SeverityCriterion,
                AuthoritySource = "38 CFR"
            });

        var coordinator =
            new EvidenceRecognitionCoordinator(
                gaps,
                new FakeTextExtractor(
                    "Veteran has ankle instability."),
                terms);

        var result =
            await coordinator.RecognizeAsync(gapId);

        Assert.Single(result.Matches);
    }


    private sealed class FakeGapRepository :
        EMF.Extensions.VeteransClaims.Contracts.IEvidenceGapRepository
    {
        public EvidenceGap? Gap { get; set; }

        public IReadOnlyList<EvidenceGapArtifact> Artifacts
        { get; set; } = Array.Empty<EvidenceGapArtifact>();

        public Task AddEvidenceGapAsync(
            EvidenceGap gap,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Gap);

        public Task<IReadOnlyList<EvidenceGapArtifact>>
            GetEvidenceGapArtifactsAsync(
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(Artifacts);

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTextExtractor :
        IArtifactTextExtractor
    {
        private readonly string? _text;

        public FakeTextExtractor(string? text)
        {
            _text = text;
        }

        public Task<string?> ExtractTextAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_text);
    }
}
