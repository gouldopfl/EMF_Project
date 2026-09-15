using System.Text.Json;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransReviewerMedicationClinicalContextProjectionServiceTests
{
    [Fact]
    public async Task GetAsync_UsesHumanRecordLocatorWithoutInternalProvenance()
    {
        var path = Path.GetTempFileName();

        try
        {
            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = new ArtifactId("internal-blue-button-artifact"),
                    Name = "VA-Blue-Button-report-Michael-Gould-9-9-2026_0506pm.pdf",
                    ArtifactType = "application/pdf"
                });

            var projected =
                Assert.Single(
                    await new VeteransReviewerMedicationClinicalContextProjectionService(
                            evidence)
                        .GetAsync(
                            [
                                new MedicationClinicalContextLink
                                {
                                    Medication =
                                        new MedicationLedgerEntry
                                        {
                                            Id = new MedicationLedgerEntryId("entry-60"),
                                            MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                                            EntryOrdinal = 64,
                                            SourceStartPage = 3943,
                                            SourceEndPage = 3943,
                                            MedicationName = "ISOSORBIDE MONONITRATE 60MG SA TAB",
                                            Strength = "60MG",
                                            Status = "discontinued",
                                            PrescriptionNumber = "12620234"
                                        },
                                    Context =
                                        new MedicationClinicalContext
                                        {
                                            Id = new MedicationClinicalContextId("context-1"),
                                            VeteranId = new VeteranId("veteran-1"),
                                            SourceArtifactId = new ArtifactId("internal-blue-button-artifact"),
                                            EventDate = new DateOnly(2025, 8, 12),
                                            SourceStartPage = 948,
                                            SourceEndPage = 949,
                                            MedicationName = "ISOSORBIDE MONONITRATE 60MG SA TAB",
                                            PrescriptionNumber = "12620234",
                                            ContextType = MedicationClinicalContextTypes.ClinicalEffect,
                                            RecordTitle = "PC Nursing Outpatient Telephone Note",
                                            Summary = "The Veteran reported dizziness after the dose increase; the same note records Cardiology did not agree that isosorbide caused the complaints."
                                        }
                                }
                            ]));

            Assert.Equal(
                "VA Blue Button Report — PC Nursing Outpatient Telephone Note — August 12, 2025",
                projected.SourceLocator);

            var reviewerPayload = JsonSerializer.Serialize(projected);

            Assert.Contains("VA Blue Button Report", reviewerPayload);
            Assert.Contains("PC Nursing Outpatient Telephone Note", reviewerPayload);
            Assert.Contains("August 12, 2025", reviewerPayload);
            Assert.DoesNotContain("internal-blue-button-artifact", reviewerPayload);
            Assert.DoesNotContain("VA-Blue-Button-report-Michael-Gould-9-9-2026_0506pm.pdf", reviewerPayload);
            Assert.DoesNotContain("948", reviewerPayload);
            Assert.DoesNotContain("949", reviewerPayload);
            Assert.DoesNotContain("3943", reviewerPayload);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_RejectsContextWithoutHumanRecordTitle()
    {
        var path = Path.GetTempFileName();

        try
        {
            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(
                new Artifact
                {
                    Id = new ArtifactId("artifact-1"),
                    Name = "clinical-record.pdf",
                    ArtifactType = "application/pdf"
                });

            var service =
                new VeteransReviewerMedicationClinicalContextProjectionService(
                    evidence);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.GetAsync(
                    [
                        new MedicationClinicalContextLink
                        {
                            Medication =
                                new MedicationLedgerEntry
                                {
                                    Id = new MedicationLedgerEntryId("entry-1"),
                                    MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                                    EntryOrdinal = 1,
                                    SourceStartPage = 1,
                                    SourceEndPage = 1,
                                    MedicationName = "Medication",
                                    Status = "active",
                                    PrescriptionNumber = "RX-1"
                                },
                            Context =
                                new MedicationClinicalContext
                                {
                                    Id = new MedicationClinicalContextId("context-1"),
                                    VeteranId = new VeteranId("veteran-1"),
                                    SourceArtifactId = new ArtifactId("artifact-1"),
                                    EventDate = new DateOnly(2025, 8, 12),
                                    SourceStartPage = 10,
                                    SourceEndPage = 10,
                                    MedicationName = "Medication",
                                    PrescriptionNumber = "RX-1",
                                    ContextType = MedicationClinicalContextTypes.ClinicalObservation,
                                    RecordTitle = null,
                                    Summary = "Observation."
                                }
                        }
                    ]));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
