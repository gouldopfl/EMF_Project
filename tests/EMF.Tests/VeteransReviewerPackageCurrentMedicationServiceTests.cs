using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageCurrentMedicationServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsCompleteCurrentLedgerSnapshotWithoutBasisFiltering()
    {
        var path = Path.GetTempFileName();
        try
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

            var ledger = new MedicationLedger
            {
                Id = new MedicationLedgerId("ledger-1"),
                VeteranId = veteran.Id,
                SourceArtifactId = new ArtifactId("blue-button"),
                ReportDate = new DateOnly(2026, 9, 9),
                SourceStartPage = 3911,
                SourceEndPage = 4023,
                ReportedEntryCount = 3,
                ParsedEntryCount = 3,
                IsComplete = true
            };

            var medications = new SqliteMedicationRepository(path);
            await medications.AddMedicationLedgerAsync(
                ledger,
                [
                    Entry(ledger, 1, "Trazodone", "active"),
                    Entry(ledger, 2, "Isosorbide", "refillinprocess"),
                    Entry(ledger, 3, "Legacy", "transferred")
                ]);

            var result =
                await new VeteransReviewerPackageCurrentMedicationService(
                        new SqliteClaimIssueRepository(path),
                        new SqliteClaimRepository(path),
                        new CurrentMedicationLedgerService(medications))
                    .GetAsync(
                        new EvidencePackage
                        {
                            Id = new EvidencePackageId("package-1"),
                            ClaimIssueId = issue.Id,
                            Purpose = "Medical review",
                            ReviewerRole = "MedicalProfessional"
                        });

            Assert.Collection(
                result,
                entry => Assert.Equal("Isosorbide", entry.MedicationName),
                entry => Assert.Equal("Trazodone", entry.MedicationName));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MedicationLedgerEntry Entry(
        MedicationLedger ledger,
        int ordinal,
        string name,
        string status) =>
        new()
        {
            Id = new MedicationLedgerEntryId($"entry-{ordinal}"),
            MedicationLedgerId = ledger.Id,
            EntryOrdinal = ordinal,
            SourceStartPage = 3911,
            SourceEndPage = 3911,
            MedicationName = name,
            Status = status
        };
}
