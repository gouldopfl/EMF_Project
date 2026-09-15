using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageMedicationClinicalContextServiceTests
{
    [Fact]
    public async Task GetAsync_PreservesBasisFilterBeforeProjectingClinicalContext()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var veteranId = new VeteranId("veteran-1");
            var claimId = new ClaimId("claim-1");
            var issueId = new ClaimIssueId("issue-1");
            var theoryId = new ServiceConnectionTheoryId("theory-1");
            var basisId = new ServiceConnectionBasisId("basis-mental-health");

            await new SqliteVeteranRepository(path)
                .AddVeteranAsync(
                    new Veteran
                    {
                        Id = veteranId
                    });

            await new SqliteClaimRepository(path)
                .AddClaimAsync(
                    new Claim
                    {
                        Id = claimId,
                        VeteranId = veteranId
                    });

            await new SqliteClaimIssueRepository(path)
                .AddClaimIssueAsync(
                    new ClaimIssue
                    {
                        Id = issueId,
                        ClaimId = claimId,
                        ClaimIssueType = ClaimIssueTypes.ServiceConnection
                    });

            var connections = new SqliteServiceConnectionRepository(path);
            await connections.AddServiceConnectionTheoryAsync(
                new ServiceConnectionTheory
                {
                    Id = theoryId,
                    ClaimIssueId = issueId,
                    TheoryType = ServiceConnectionTheoryTypes.Secondary
                });
            await connections.AddServiceConnectionBasisAsync(
                new ServiceConnectionBasis
                {
                    Id = basisId,
                    ClaimIssueId = issueId,
                    ServiceConnectionTheoryId = theoryId
                });
            await connections.AddBasisPrescribedMedicationAsync(
                new ServiceConnectionBasisPrescribedMedication
                {
                    ServiceConnectionBasisId = basisId,
                    MedicationName = "Sertraline HCl"
                });

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = new ArtifactId("blue-button"),
                    Name = "VA-Blue-Button-report.pdf",
                    ArtifactType = "application/pdf"
                });

            var medications = new SqliteMedicationRepository(path);
            await medications.InitializeAsync();

            await medications.AddMedicationLedgerAsync(
                new MedicationLedger
                {
                    Id = new MedicationLedgerId("ledger-1"),
                    VeteranId = veteranId,
                    SourceArtifactId = new ArtifactId("blue-button"),
                    ReportDate = new DateOnly(2026, 9, 9),
                    SourceStartPage = 3911,
                    SourceEndPage = 4023,
                    ReportedEntryCount = 2,
                    ParsedEntryCount = 2,
                    IsComplete = true
                },
                [
                    Entry(
                        1,
                        "RX-SERTRALINE",
                        "SERTRALINE HCL 50MG TAB",
                        "50MG"),
                    Entry(
                        2,
                        "12620234",
                        "ISOSORBIDE MONONITRATE 60MG SA TAB",
                        "60MG")
                ]);

            await medications.AddMedicationClinicalContextAsync(
                Context(
                    veteranId,
                    "context-sertraline",
                    "RX-SERTRALINE",
                    "SERTRALINE HCL 50MG TAB",
                    "Sertraline clinical context."));

            await medications.AddMedicationClinicalContextAsync(
                Context(
                    veteranId,
                    "context-isosorbide",
                    "12620234",
                    "ISOSORBIDE MONONITRATE 60MG SA TAB",
                    "Isosorbide context must not leak into the mental-health basis."));

            var package =
                new EvidencePackage
                {
                    Id = new EvidencePackageId("package-1"),
                    ClaimIssueId = issueId,
                    Purpose = "Medical review",
                    ReviewerRole = "MedicalProfessional",
                    ServiceConnectionBasisId = basisId
                };

            var progressions =
                await new VeteransReviewerPackageMedicationProgressionService(
                        new SqliteClaimIssueRepository(path),
                        new SqliteClaimRepository(path),
                        connections,
                        medications)
                    .GetAsync(package);

            var progression = Assert.Single(progressions);
            Assert.Equal("Sertraline HCl", progression.MedicationName);

            var projected =
                await new VeteransReviewerPackageMedicationClinicalContextService(
                        new SqliteClaimIssueRepository(path),
                        new SqliteClaimRepository(path),
                        medications,
                        evidence)
                    .GetAsync(
                        package,
                        progressions);

            var context = Assert.Single(projected);
            Assert.Equal("RX-SERTRALINE", context.PrescriptionNumber);
            Assert.Contains("Sertraline", context.Summary);
            Assert.DoesNotContain(
                projected,
                item => item.Summary.Contains(
                    "Isosorbide",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MedicationClinicalContext Context(
        VeteranId veteranId,
        string id,
        string prescriptionNumber,
        string medicationName,
        string summary) =>
        new()
        {
            Id = new MedicationClinicalContextId(id),
            VeteranId = veteranId,
            SourceArtifactId = new ArtifactId("blue-button"),
            EventDate = new DateOnly(2025, 8, 12),
            SourceStartPage = 948,
            SourceEndPage = 949,
            MedicationName = medicationName,
            PrescriptionNumber = prescriptionNumber,
            ContextType = MedicationClinicalContextTypes.ClinicalEffect,
            RecordTitle = "PC Nursing Outpatient Telephone Note",
            Summary = summary
        };

    private static MedicationLedgerEntry Entry(
        int ordinal,
        string prescriptionNumber,
        string medicationName,
        string strength) =>
        new()
        {
            Id = new MedicationLedgerEntryId($"entry-{ordinal}"),
            MedicationLedgerId = new MedicationLedgerId("ledger-1"),
            EntryOrdinal = ordinal,
            SourceStartPage = 3910 + ordinal,
            SourceEndPage = 3910 + ordinal,
            MedicationName = medicationName,
            Strength = strength,
            Status = "active",
            PrescriptionNumber = prescriptionNumber,
            PrescribedDate = new DateOnly(2025, 1, ordinal),
            Directions = "TAKE ONE TABLET ORALLY EVERY DAY"
        };
}
