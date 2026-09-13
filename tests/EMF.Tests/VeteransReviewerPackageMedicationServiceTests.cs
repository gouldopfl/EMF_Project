using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageMedicationServiceTests
{
    [Fact]
    public async Task GetAsync_NoPersistedBasisReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var result = await CreateService(path).GetAsync(
                Package(new ClaimIssueId("issue-1"), null));
            Assert.Empty(result);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task GetAsync_ReturnsRelevantCurrentMedication()
    {
        var path = Path.GetTempFileName();
        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var seeded = await SeedAsync(path, "1");
            var connections = new SqliteServiceConnectionRepository(path);
            await connections.AddBasisPrescribedMedicationAsync(
                new ServiceConnectionBasisPrescribedMedication
                {
                    ServiceConnectionBasisId = seeded.BasisId,
                    MedicationName = "Trazodone"
                });
            await new SqliteMedicationRepository(path)
                .AddMedicationRecordAsync(
                    Record("med-1", seeded.VeteranId, "Trazodone"));

            var result = await CreateService(path).GetAsync(
                Package(seeded.IssueId, seeded.BasisId));

            Assert.Equal("Trazodone", Assert.Single(result).CurrentMedication.MedicationName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task GetAsync_IncludesEarliestDocumentedRelease()
    {
        var path = Path.GetTempFileName();
        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var seeded = await SeedAsync(path, "1");

            var connections =
                new SqliteServiceConnectionRepository(path);

            await connections.AddBasisPrescribedMedicationAsync(
                new ServiceConnectionBasisPrescribedMedication
                {
                    ServiceConnectionBasisId = seeded.BasisId,
                    MedicationName = "Trazodone"
                });

            var medications =
                new SqliteMedicationRepository(path);

            await medications.AddMedicationRecordAsync(
                Record(
                    "med-current",
                    seeded.VeteranId,
                    "Trazodone"));

            await medications.AddMedicationHistoryEventAsync(
                new MedicationHistoryEvent
                {
                    Id =
                        new MedicationHistoryEventId(
                            "history-1"),
                    VeteranId = seeded.VeteranId,
                    SourceArtifactId =
                        new ArtifactId("blue-button"),
                    EventDate =
                        new DateOnly(2022, 12, 29),
                    SourcePage = 2448,
                    MedicationName = "Trazodone",
                    EventType =
                        MedicationHistoryEventTypes.LastReleased
                });

            var result =
                await CreateService(path).GetAsync(
                    Package(
                        seeded.IssueId,
                        seeded.BasisId));

            var medication = Assert.Single(result);

            Assert.Equal(
                "Trazodone",
                medication.CurrentMedication.MedicationName);

            Assert.NotNull(
                medication.EarliestDocumentedRelease);

            Assert.Equal(
                new DateOnly(2022, 12, 29),
                medication.EarliestDocumentedRelease!.EventDate);

            Assert.Equal(
                2448,
                medication.EarliestDocumentedRelease.SourcePage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_ExcludesMedicationFromDifferentBasis()
    {
        var path = Path.GetTempFileName();
        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var seeded = await SeedAsync(path, "1");
            var connections = new SqliteServiceConnectionRepository(path);

            var cadBasis = new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-cad"),
                ClaimIssueId = seeded.IssueId,
                ServiceConnectionTheoryId =
                    new ServiceConnectionTheoryId("theory-1")
            };
            await connections.AddServiceConnectionBasisAsync(cadBasis);

            await connections.AddBasisPrescribedMedicationAsync(
                new ServiceConnectionBasisPrescribedMedication
                {
                    ServiceConnectionBasisId = seeded.BasisId,
                    MedicationName = "Trazodone"
                });
            await connections.AddBasisPrescribedMedicationAsync(
                new ServiceConnectionBasisPrescribedMedication
                {
                    ServiceConnectionBasisId = cadBasis.Id,
                    MedicationName = "Atorvastatin"
                });

            var medications = new SqliteMedicationRepository(path);
            await medications.AddMedicationRecordAsync(
                Record("med-1", seeded.VeteranId, "Trazodone"));
            await medications.AddMedicationRecordAsync(
                Record("med-2", seeded.VeteranId, "Atorvastatin"));

            var result = await CreateService(path).GetAsync(
                Package(seeded.IssueId, seeded.BasisId));

            Assert.Equal("Trazodone", Assert.Single(result).CurrentMedication.MedicationName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task GetAsync_RejectsBasisFromAnotherClaimIssue()
    {
        var path = Path.GetTempFileName();
        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var first = await SeedAsync(path, "1");
            var second = await SeedAsync(path, "2");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateService(path).GetAsync(
                    Package(first.IssueId, second.BasisId)));

            Assert.Equal(
                "Reviewer package service-connection basis lineage mismatch.",
                ex.Message);
        }
        finally { File.Delete(path); }
    }

    private static VeteransReviewerPackageMedicationService CreateService(
        string path) =>
        new(
            new SqliteClaimIssueRepository(path),
            new SqliteClaimRepository(path),
            new SqliteServiceConnectionRepository(path),
            new CurrentMedicationService(
                new SqliteMedicationRepository(path)),
            new MedicationHistorySummaryService(
                new SqliteMedicationRepository(path)));

    private static EvidencePackage Package(
        ClaimIssueId issueId,
        ServiceConnectionBasisId? basisId) =>
        new()
        {
            Id = new EvidencePackageId("package-1"),
            ClaimIssueId = issueId,
            Purpose = "Medical review",
            ReviewerRole = "MedicalProfessional",
            ServiceConnectionBasisId = basisId
        };

    private static MedicationRecord Record(
        string id,
        VeteranId veteranId,
        string name) =>
        new()
        {
            Id = new MedicationRecordId(id),
            VeteranId = veteranId,
            SourceArtifactId = new ArtifactId("blue-button"),
            RecordDate = new DateOnly(2026, 8, 4),
            SourcePage = 1,
            MedicationName = name,
            Status = MedicationStatuses.Active
        };

    private static async Task<(
        VeteranId VeteranId,
        ClaimIssueId IssueId,
        ServiceConnectionBasisId BasisId)> SeedAsync(
            string path,
            string suffix)
    {
        var veteran = new Veteran { Id = new VeteranId($"veteran-{suffix}") };
        await new SqliteVeteranRepository(path).AddVeteranAsync(veteran);

        var claim = new Claim
        {
            Id = new ClaimId($"claim-{suffix}"),
            VeteranId = veteran.Id
        };
        await new SqliteClaimRepository(path).AddClaimAsync(claim);

        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId($"issue-{suffix}"),
            ClaimId = claim.Id,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };
        await new SqliteClaimIssueRepository(path).AddClaimIssueAsync(issue);

        var connections = new SqliteServiceConnectionRepository(path);
        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId($"theory-{suffix}"),
            ClaimIssueId = issue.Id,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };
        await connections.AddServiceConnectionTheoryAsync(theory);

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId($"basis-{suffix}"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId = theory.Id
        };
        await connections.AddServiceConnectionBasisAsync(basis);

        return (veteran.Id, issue.Id, basis.Id);
    }
}
