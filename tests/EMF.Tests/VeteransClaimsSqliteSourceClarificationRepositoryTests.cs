using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteSourceClarificationRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsSourceClarificationsChronologically()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddAsync(
                Clarification(
                    "clarification-later",
                    new DateOnly(2025, 7, 8),
                    2100,
                    SourceClarificationCategories.InternalConflict,
                    "CPAP 12 cmH2O with higher reported pressure values.",
                    "Source contains an apparent internal inconsistency; no correction has been inferred."));

            await repository.AddAsync(
                Clarification(
                    "clarification-earlier",
                    new DateOnly(2024, 1, 25),
                    1600,
                    SourceClarificationCategories.ImpossibleMagnitude,
                    "8200 pounds",
                    "The source record states ‘8200 pounds.’ The Veteran reports the intended value is 82 pounds. Original source text is preserved.",
                    reviewerMatchText: "8200 pounds",
                    reviewerReplacementText: "82 pounds"));

            var stored =
                await repository.GetAsync(
                    new ClaimIssueId("issue-osa"));

            Assert.Equal(2, stored.Count);
            Assert.Equal("clarification-earlier", stored[0].Id.Value);
            Assert.Equal("8200 pounds", stored[0].OriginalText);
            Assert.Equal(
                SourceClarificationCategories.ImpossibleMagnitude,
                stored[0].Category);
            Assert.Equal("8200 pounds", stored[0].ReviewerMatchText);
            Assert.Equal("82 pounds", stored[0].ReviewerReplacementText);
            Assert.Equal("clarification-later", stored[1].Id.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_UpdatesReviewerCorrection()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);
            var clarification =
                Clarification(
                    "clarification-1",
                    new DateOnly(2024, 1, 25),
                    1600,
                    SourceClarificationCategories.ImpossibleMagnitude,
                    "After surgery he lost about 8200 pounds.",
                    "Veteran reports the intended value is 82 pounds.");

            await repository.AddAsync(clarification);
            await repository.SetReviewerCorrectionAsync(
                clarification.Id,
                "8200 pounds",
                "82 pounds");

            var stored =
                Assert.Single(
                    await repository.GetAsync(
                        new ClaimIssueId("issue-osa")));

            Assert.Equal("8200 pounds", stored.ReviewerMatchText);
            Assert.Equal("82 pounds", stored.ReviewerReplacementText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RejectsUnsupportedCategory()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.AddAsync(
                    Clarification(
                        "clarification-invalid",
                        new DateOnly(2024, 1, 25),
                        1600,
                        "Guess",
                        "8200 pounds",
                        "Do not infer corrections.")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<SqliteSourceClarificationRepository> CreateAsync(
        string path)
    {
        await new VeteransClaimsSqliteSchema(path)
            .InitializeAsync();

        var veteran =
            new Veteran
            {
                Id = new VeteranId("veteran-osa")
            };

        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(veteran);

        var claim =
            new Claim
            {
                Id = new ClaimId("claim-osa"),
                VeteranId = veteran.Id
            };

        await new SqliteClaimRepository(path)
            .AddClaimAsync(claim);

        await new SqliteClaimIssueRepository(path)
            .AddClaimIssueAsync(
                new ClaimIssue
                {
                    Id = new ClaimIssueId("issue-osa"),
                    ClaimId = claim.Id,
                    ClaimIssueType = ClaimIssueTypes.ServiceConnection
                });

        return new SqliteSourceClarificationRepository(path);
    }

    private static SourceClarification Clarification(
        string id,
        DateOnly evidenceDate,
        int sourcePage,
        string category,
        string originalText,
        string clarification,
        string? reviewerMatchText = null,
        string? reviewerReplacementText = null) =>
        new()
        {
            Id = new SourceClarificationId(id),
            ClaimIssueId = new ClaimIssueId("issue-osa"),
            SourceArtifactId = new ArtifactId("blue-button-001"),
            EvidenceDate = evidenceDate,
            SourceStartPage = sourcePage,
            SourceEndPage = sourcePage,
            RecordTitle = "VA Sleep Medicine Note",
            Category = category,
            OriginalText = originalText,
            Clarification = clarification,
            ReviewerMatchText = reviewerMatchText,
            ReviewerReplacementText = reviewerReplacementText
        };
}
