using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Clinical;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteClinicalProgressionRepositoryTests
{
    [Theory]
    [InlineData("SourceEndPage", "99")]
    [InlineData("EventType", "Unsupported")]
    [InlineData("Summary", " ")]
    [InlineData("EventDate", "07/30/2025")]
    public async Task Repository_RejectsMalformedPersistedState(string column, string value)
    {
        var path = Path.GetTempFileName();
        try
        {
            var repository = await CreateAsync(path);
            await repository.AddAsync(Event("event", new(2025, 7, 30), ClinicalProgressionEventTypes.TreatmentProblem, "Factual observation."));
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // Column names are fixed test literals, never user-provided SQL.
            command.CommandText = $"PRAGMA ignore_check_constraints = ON; UPDATE VeteransClaims_ClinicalProgressionEvents SET {column} = $value;";
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync();
            if (column == "EventDate")
                await Assert.ThrowsAsync<FormatException>(() => repository.GetAsync(new("issue-osa")));
            else
                await Assert.ThrowsAnyAsync<ArgumentException>(() => repository.GetAsync(new("issue-osa")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Repository_TiedEventsUseFactsInsteadOfIdsForOrdering()
    {
        var path = Path.GetTempFileName();
        try
        {
            var repository = await CreateAsync(path);
            await repository.AddAsync(Event("a", new(2025, 7, 30), ClinicalProgressionEventTypes.TreatmentProblem, "Zulu observation"));
            await repository.AddAsync(Event("z", new(2025, 7, 30), ClinicalProgressionEventTypes.TreatmentProblem, "Alpha observation"));
            var result = await repository.GetAsync(new("issue-osa"));
            Assert.Equal(["Alpha observation", "Zulu observation"], result.Select(x => x.Summary));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Repository_RoundTripsClinicalProgressionChronologically()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddAsync(
                Event(
                    "event-later",
                    new DateOnly(2025, 7, 30),
                    ClinicalProgressionEventTypes.TreatmentTransition,
                    "Switched from CPAP to ASV."));

            await repository.AddAsync(
                Event(
                    "event-earlier",
                    new DateOnly(2021, 11, 15),
                    ClinicalProgressionEventTypes.TreatmentProblem,
                    "High mask leak was documented despite 100% PAP compliance."));

            var stored =
                await repository.GetAsync(new ClaimIssueId("issue-osa"));

            Assert.Equal(2, stored.Count);
            Assert.Equal("event-earlier", stored[0].Id.Value);
            Assert.Equal(
                ClinicalProgressionEventTypes.TreatmentProblem,
                stored[0].EventType);
            Assert.Equal("event-later", stored[1].Id.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_AllowsNonPagedSourceArtifacts()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            var progressionEvent =
                Event(
                    "event-data",
                    new DateOnly(2026, 9, 7),
                    ClinicalProgressionEventTypes.TreatmentResponse,
                    "Longitudinal PAP data shows sustained use and lower treated AHI.");

            progressionEvent =
                new ClinicalProgressionEvent
                {
                    Id = progressionEvent.Id,
                    ClaimIssueId = progressionEvent.ClaimIssueId,
                    SourceArtifactId = new ArtifactId("pap-data-001"),
                    EventDate = progressionEvent.EventDate,
                    SourceStartPage = null,
                    SourceEndPage = null,
                    RecordTitle = "PAP Therapy Analysis",
                    EventType = progressionEvent.EventType,
                    Summary = progressionEvent.Summary
                };

            await repository.AddAsync(progressionEvent);

            var stored =
                Assert.Single(
                    await repository.GetAsync(
                        new ClaimIssueId("issue-osa")));

            Assert.Null(stored.SourceStartPage);
            Assert.Null(stored.SourceEndPage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Repository_RejectsUnsupportedEventType()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.AddAsync(
                    Event(
                        "event-invalid",
                        new DateOnly(2025, 7, 30),
                        "Guess",
                        "Do not infer a clinical event type.")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<SqliteClinicalProgressionRepository> CreateAsync(
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

        return new SqliteClinicalProgressionRepository(path);
    }

    private static ClinicalProgressionEvent Event(
        string id,
        DateOnly eventDate,
        string eventType,
        string summary) =>
        new()
        {
            Id = new ClinicalProgressionEventId(id),
            ClaimIssueId = new ClaimIssueId("issue-osa"),
            SourceArtifactId = new ArtifactId("clinical-note-001"),
            EventDate = eventDate,
            SourceStartPage = 100,
            SourceEndPage = 102,
            RecordTitle = "Sleep Medicine Note",
            EventType = eventType,
            Summary = summary
        };
}
