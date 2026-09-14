using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteBoundedEvidenceInterpretationRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsReviewedInterpretationAndRequiresSupersession()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteBoundedEvidenceInterpretationRepository(path);
            await repository.InitializeAsync();

            var reviewedUtc =
                new DateTimeOffset(
                    2026, 9, 14, 8, 15, 0, TimeSpan.Zero);

            var original = Create(
                "bounded-correlation-1",
                reviewedUtc,
                "Initial reviewed conclusion.",
                604,
                266,
                870,
                0.0036696m);

            await repository.AddReviewedInterpretationAsync(original);

            var byRequirement =
                Assert.Single(
                    await repository.GetReviewedInterpretationsAsync(
                        original.RequirementId));
            var byArtifact =
                Assert.Single(
                    await repository.GetReviewedInterpretationsAsync(
                        original.ArtifactId));

            Assert.Equal(original.CorrelationId, byRequirement.CorrelationId);
            Assert.Equal(byRequirement.CorrelationId, byArtifact.CorrelationId);
            Assert.Equal(original.ClaimIssueId, byRequirement.ClaimIssueId);
            Assert.Equal(
                original.ServiceConnectionBasisId,
                byRequirement.ServiceConnectionBasisId);
            Assert.Equal(original.RequirementId, byRequirement.RequirementId);
            Assert.Equal(original.ArtifactId, byRequirement.ArtifactId);
            Assert.Equal(original.Direction, byRequirement.Direction);
            Assert.Equal(
                original.OpinionStandard,
                byRequirement.OpinionStandard);
            Assert.Equal(
                original.MedicalConclusion,
                byRequirement.MedicalConclusion);
            Assert.Equal(
                original.RationaleSummary,
                byRequirement.RationaleSummary);
            Assert.Equal(original.ReviewedBy, byRequirement.ReviewedBy);
            Assert.Equal(original.ReviewedUtc, byRequirement.ReviewedUtc);
            Assert.Equal(original.PromotedBy, byRequirement.PromotedBy);
            Assert.Equal(original.PromotedUtc, byRequirement.PromotedUtc);
            Assert.Equal(original.CapabilityId, byRequirement.CapabilityId);
            Assert.Equal(original.ProviderId, byRequirement.ProviderId);
            Assert.Equal(original.EngineName, byRequirement.EngineName);
            Assert.Equal(original.EngineVersion, byRequirement.EngineVersion);
            Assert.Equal(
                original.ProviderOperationId,
                byRequirement.ProviderOperationId);
            Assert.Equal(
                original.InputTokenCount,
                byRequirement.InputTokenCount);
            Assert.Equal(
                original.OutputTokenCount,
                byRequirement.OutputTokenCount);
            Assert.Equal(
                original.TotalTokenCount,
                byRequirement.TotalTokenCount);
            Assert.Equal(
                original.EstimatedCostUsd,
                byRequirement.EstimatedCostUsd);
            Assert.Equal(original.StartedUtc, byRequirement.StartedUtc);
            Assert.Equal(original.CompletedUtc, byRequirement.CompletedUtc);
            Assert.True(byRequirement.RequiresReview);
            Assert.Equal(original.Warnings, byRequirement.Warnings);

            var excerpt = Assert.Single(byRequirement.SourceExcerpts);
            Assert.Equal(original.ArtifactId, excerpt.ArtifactId);
            Assert.Equal("Exact source segment.", excerpt.Text);
            Assert.Equal(40, excerpt.StartOffset);
            Assert.Equal(21, excerpt.Length);

            var duplicate = Create(
                "bounded-correlation-duplicate",
                reviewedUtc.AddMinutes(5),
                "Duplicate logical decision.",
                1,
                1,
                2,
                0.0001m);

            var duplicateException =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => repository.AddReviewedInterpretationAsync(
                        duplicate));

            Assert.Contains(
                "Explicit supersession is required",
                duplicateException.Message,
                StringComparison.Ordinal);

            var replacement = Create(
                "bounded-correlation-2",
                reviewedUtc.AddMinutes(10),
                "Replacement reviewed conclusion.",
                1547,
                458,
                2005,
                0.0074338m);

            await repository.SupersedeReviewedInterpretationsAsync(
                original.CorrelationId,
                [replacement]);

            var active =
                Assert.Single(
                    await repository.GetReviewedInterpretationsAsync(
                        original.ArtifactId));

            Assert.Equal(replacement.CorrelationId, active.CorrelationId);
            Assert.Equal(
                replacement.MedicalConclusion,
                active.MedicalConclusion);

            await using var connection =
                new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CorrelationId, SupersededByCorrelationId,
                       SupersededUtc
                FROM VeteransClaims_ReviewedBoundedEvidenceInterpretations
                ORDER BY PromotedUtc;
                """;

            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal(original.CorrelationId, reader.GetString(0));
            Assert.Equal(replacement.CorrelationId, reader.GetString(1));
            Assert.False(reader.IsDBNull(2));

            Assert.True(await reader.ReadAsync());
            Assert.Equal(replacement.CorrelationId, reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RejectsReplacementForDifferentLogicalDecision()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteBoundedEvidenceInterpretationRepository(path);
            await repository.InitializeAsync();

            var reviewedUtc =
                new DateTimeOffset(
                    2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
            var original = Create(
                "bounded-logical-1",
                reviewedUtc,
                "Original.",
                10,
                5,
                15,
                0.001m);

            await repository.AddReviewedInterpretationAsync(original);

            var replacement = Create(
                "bounded-logical-2",
                reviewedUtc.AddMinutes(5),
                "Wrong requirement.",
                10,
                5,
                15,
                0.001m,
                requirementId: "requirement-other");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.SupersedeReviewedInterpretationsAsync(
                    original.CorrelationId,
                    [replacement]));

            var active =
                Assert.Single(
                    await repository.GetReviewedInterpretationsAsync(
                        original.RequirementId));
            Assert.Equal(original.CorrelationId, active.CorrelationId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ReviewedBoundedEvidenceInterpretation Create(
        string correlationId,
        DateTimeOffset reviewedUtc,
        string conclusion,
        int inputTokens,
        int outputTokens,
        int totalTokens,
        decimal cost,
        string requirementId = "requirement-3.310-a")
    {
        var artifactId = new ArtifactId("bounded-artifact-001");
        var excerpt = "Exact source segment.";

        return new ReviewedBoundedEvidenceInterpretation
        {
            ClaimIssueId = new ClaimIssueId("issue-osa"),
            ServiceConnectionBasisId =
                new ServiceConnectionBasisId("basis-osa-secondary"),
            RequirementId = new RequirementId(requirementId),
            ArtifactId = artifactId,
            Direction = "OpposesRequirement",
            OpinionStandard = "LessLikelyThanNot",
            MedicalConclusion = conclusion,
            RationaleSummary = "Reviewed rationale.",
            PromotedBy = "review-promotion-test",
            PromotedUtc = reviewedUtc.AddMinutes(1),
            ReviewedBy = "reviewer@example.test",
            ReviewedUtc = reviewedUtc,
            CapabilityId = "text.structured.extract",
            ProviderId = "azure.openai",
            CorrelationId = correlationId,
            EngineName = "emf-gpt-4-1",
            EngineVersion = "gpt-4.1-2025-04-14",
            ProviderOperationId = "operation-1",
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            TotalTokenCount = totalTokens,
            EstimatedCostUsd = cost,
            StartedUtc = reviewedUtc.AddMinutes(-2),
            CompletedUtc = reviewedUtc.AddMinutes(-1),
            RequiresReview = true,
            Warnings = ["human review required"],
            SourceExcerpts =
            [
                new ReviewedBoundedEvidenceSourceExcerpt
                {
                    ArtifactId = artifactId,
                    Text = excerpt,
                    StartOffset = 40,
                    Length = excerpt.Length
                }
            ]
        };
    }
}
