using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageMedicationProgressionServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsOnlyBasisRelevantMedicationProgression()
    {
        var path = Path.GetTempFileName();
        try
        {
            var seeded = await SeedAsync(path);
            var connections = new SqliteServiceConnectionRepository(path);
            await AddRelevantAsync(
                connections,
                seeded.BasisId,
                "Isosorbide Mononitrate");

            await AddLedgerAsync(
                path,
                seeded.VeteranId,
                [
                    Entry(1, "ISOSORBIDE MONONITRATE 30MG SA TAB", "30MG", "discontinued", new DateOnly(2024, 1, 1)),
                    Entry(2, "isosorbide mononitrate (isosorbide mononitrate ER 30 mg/24 hour tablet)", "30 mg/24 hour", "refillinprocess", new DateOnly(2026, 8, 21)),
                    Entry(3, "allopurinol (allopurinol 300 mg tablet)", "300 mg", "active", new DateOnly(2026, 8, 21))
                ]);

            var result =
                await CreateService(path).GetAsync(
                    Package(seeded.IssueId, seeded.BasisId));

            var progression = Assert.Single(result);
            Assert.Equal("Isosorbide Mononitrate", progression.MedicationName);
            Assert.Equal(2, progression.Entries.Count);
            Assert.DoesNotContain(
                progression.Entries,
                entry => entry.MedicationName.Contains("allopurinol", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_MatchesLegacyAndModernNamesAcrossSaltAndFormLabels()
    {
        var path = Path.GetTempFileName();
        try
        {
            var seeded = await SeedAsync(path);
            var connections = new SqliteServiceConnectionRepository(path);
            await AddRelevantAsync(
                connections,
                seeded.BasisId,
                "Trazodone HCl");

            await AddLedgerAsync(
                path,
                seeded.VeteranId,
                [
                    Entry(1, "TRAZODONE HCL 100MG TAB", "100MG", "expired", new DateOnly(2024, 3, 1), "TAKE TWO TABLETS ORALLY AT BEDTIME FOR SLEEP"),
                    Entry(2, "traZODone (traZODone 100 mg tablet)", "100 mg", "active", new DateOnly(2026, 8, 22), "See Instructions. TAKE THREE TABLETS ORALLY AT BEDTIME FOR INSOMNIA. Refills: 1.")
                ]);

            var progression =
                Assert.Single(
                    await CreateService(path).GetAsync(
                        Package(seeded.IssueId, seeded.BasisId)));

            Assert.Equal(2, progression.Entries.Count);
            Assert.Equal("TRAZODONE HCL 100MG TAB", progression.Entries[0].MedicationName);
            Assert.StartsWith("traZODone", progression.Entries[1].MedicationName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_KeepsDoseDirectionTransferAndCurrentChangesButDropsRenewalNoise()
    {
        var path = Path.GetTempFileName();
        try
        {
            var seeded = await SeedAsync(path);
            var connections = new SqliteServiceConnectionRepository(path);
            await AddRelevantAsync(
                connections,
                seeded.BasisId,
                "Isosorbide Mononitrate");

            await AddLedgerAsync(
                path,
                seeded.VeteranId,
                [
                    Entry(1, "ISOSORBIDE MONONITRATE 30MG SA TAB", "30MG", "discontinued", new DateOnly(2024, 1, 1), "TAKE ONE TABLET ORALLY EVERY DAY"),
                    Entry(2, "ISOSORBIDE MONONITRATE 30MG SA TAB", "30MG", "expired", new DateOnly(2024, 6, 1), "TAKE ONE TABLET ORALLY EVERY DAY"),
                    Entry(3, "ISOSORBIDE MONONITRATE 60MG SA TAB", "60MG", "discontinued", new DateOnly(2025, 6, 2), "TAKE ONE TABLET ORALLY EVERY DAY"),
                    Entry(4, "ISOSORBIDE MONONITRATE 30MG SA TAB", "30MG", "transferred", new DateOnly(2026, 8, 20), "TAKE ONE TABLET ORALLY EVERY DAY"),
                    Entry(5, "isosorbide mononitrate (isosorbide mononitrate ER 30 mg/24 hour tablet)", "30 mg/24 hour", "refillinprocess", new DateOnly(2026, 8, 21), "See Instructions. TAKE ONE TABLET ORALLY EVERY DAY. Refills: 3.")
                ]);

            var progression =
                Assert.Single(
                    await CreateService(path).GetAsync(
                        Package(seeded.IssueId, seeded.BasisId)));

            Assert.Collection(
                progression.Entries,
                entry => Assert.Equal(1, entry.EntryOrdinal),
                entry => Assert.Equal(3, entry.EntryOrdinal),
                entry => Assert.Equal(4, entry.EntryOrdinal),
                entry => Assert.Equal(5, entry.EntryOrdinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_KeepsTerminalDiscontinuationWhenTherapyDidNotChange()
    {
        var path = Path.GetTempFileName();
        try
        {
            var seeded = await SeedAsync(path);
            var connections = new SqliteServiceConnectionRepository(path);
            await AddRelevantAsync(connections, seeded.BasisId, "Sertraline HCl");

            await AddLedgerAsync(
                path,
                seeded.VeteranId,
                [
                    Entry(1, "SERTRALINE HCL 50MG TAB", "50MG", "expired", new DateOnly(2024, 1, 1), "TAKE ONE TABLET ORALLY EVERY MORNING"),
                    Entry(2, "SERTRALINE HCL 50MG TAB", "50MG", "discontinued", new DateOnly(2025, 1, 1), "TAKE ONE TABLET ORALLY EVERY MORNING")
                ]);

            var progression =
                Assert.Single(
                    await CreateService(path).GetAsync(
                        Package(seeded.IssueId, seeded.BasisId)));

            Assert.Equal(new[] { 1, 2 }, progression.Entries.Select(entry => entry.EntryOrdinal).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_NoPersistedBasisReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var result =
                await CreateService(path).GetAsync(
                    Package(new ClaimIssueId("issue-1"), null));
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static VeteransReviewerPackageMedicationProgressionService CreateService(
        string path) =>
        new(
            new SqliteClaimIssueRepository(path),
            new SqliteClaimRepository(path),
            new SqliteServiceConnectionRepository(path),
            new SqliteMedicationRepository(path));

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

    private static async Task AddRelevantAsync(
        SqliteServiceConnectionRepository connections,
        ServiceConnectionBasisId basisId,
        string name) =>
        await connections.AddBasisPrescribedMedicationAsync(
            new ServiceConnectionBasisPrescribedMedication
            {
                ServiceConnectionBasisId = basisId,
                MedicationName = name
            });

    private static MedicationLedgerEntry Entry(
        int ordinal,
        string name,
        string strength,
        string status,
        DateOnly prescribed,
        string directions = "TAKE ONE TABLET ORALLY EVERY DAY") =>
        new()
        {
            Id = new MedicationLedgerEntryId($"entry-{ordinal}"),
            MedicationLedgerId = new MedicationLedgerId("ledger-1"),
            EntryOrdinal = ordinal,
            SourceStartPage = 3910 + ordinal,
            SourceEndPage = 3910 + ordinal,
            MedicationName = name,
            Strength = strength,
            Status = status,
            PrescriptionNumber = $"RX-{ordinal}",
            PrescribedDate = prescribed,
            Directions = directions
        };

    private static async Task AddLedgerAsync(
        string path,
        VeteranId veteranId,
        IReadOnlyCollection<MedicationLedgerEntry> entries)
    {
        var ledger = new MedicationLedger
        {
            Id = new MedicationLedgerId("ledger-1"),
            VeteranId = veteranId,
            SourceArtifactId = new ArtifactId("blue-button"),
            ReportDate = new DateOnly(2026, 9, 9),
            SourceStartPage = 3911,
            SourceEndPage = 4023,
            ReportedEntryCount = entries.Count,
            ParsedEntryCount = entries.Count,
            IsComplete = true
        };

        await new SqliteMedicationRepository(path)
            .AddMedicationLedgerAsync(ledger, entries);
    }

    private static async Task<(
        VeteranId VeteranId,
        ClaimIssueId IssueId,
        ServiceConnectionBasisId BasisId)> SeedAsync(string path)
    {
        await new VeteransClaimsSqliteSchema(path).InitializeAsync();

        var veteran = new Veteran { Id = new VeteranId("veteran-1") };
        await new SqliteVeteranRepository(path).AddVeteranAsync(veteran);

        var claim = new Claim
        {
            Id = new ClaimId("claim-1"),
            VeteranId = veteran.Id
        };
        await new SqliteClaimRepository(path).AddClaimAsync(claim);

        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-1"),
            ClaimId = claim.Id,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };
        await new SqliteClaimIssueRepository(path).AddClaimIssueAsync(issue);

        var connections = new SqliteServiceConnectionRepository(path);
        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-1"),
            ClaimIssueId = issue.Id,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };
        await connections.AddServiceConnectionTheoryAsync(theory);

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-1"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId = theory.Id
        };
        await connections.AddServiceConnectionBasisAsync(basis);

        return (veteran.Id, issue.Id, basis.Id);
    }
}
