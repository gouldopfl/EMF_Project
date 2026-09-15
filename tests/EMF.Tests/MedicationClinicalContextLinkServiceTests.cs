using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class MedicationClinicalContextLinkServiceTests
{
    [Fact]
    public async Task GetAsync_LinksClinicalContextByExplicitPrescriptionNumber()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddMedicationClinicalContextAsync(
                Context(
                    "context-isosorbide-60",
                    "12620234",
                    "Isosorbide Mononitrate",
                    new DateOnly(2025, 8, 5),
                    "VA clinical record documents dizziness, loss of balance, low blood pressure, feeling miserable, and increased walker use after the dose was increased to 60 mg."));

            await repository.AddMedicationClinicalContextAsync(
                Context(
                    "context-other-rx",
                    "RX-OTHER",
                    "Unrelated Medication",
                    new DateOnly(2025, 8, 6),
                    "Unrelated context."));

            var entries = new[]
            {
                Entry(1, "RX-30-OLD", "ISOSORBIDE MONONITRATE 30MG SA TAB", "30MG"),
                Entry(2, "12620234", "ISOSORBIDE MONONITRATE 60MG SA TAB", "60MG"),
                Entry(3, "3211-50014120", "isosorbide mononitrate (isosorbide mononitrate ER 30 mg/24 hour tablet)", "30 mg/24 hour")
            };

            var links =
                await new MedicationClinicalContextLinkService(repository)
                    .GetAsync(
                        new VeteranId("veteran-001"),
                        entries);

            var link = Assert.Single(links);
            Assert.Equal("12620234", link.Medication.PrescriptionNumber);
            Assert.Equal("60MG", link.Medication.Strength);
            Assert.Equal(
                MedicationClinicalContextTypes.ClinicalEffect,
                link.Context.ContextType);
            Assert.Contains("dizziness", link.Context.Summary);
            Assert.Contains("low blood pressure", link.Context.Summary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_DoesNotInferLinkForUnmatchedPrescription()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);

            await repository.AddMedicationClinicalContextAsync(
                Context(
                    "context-isosorbide",
                    "RX-NOT-IN-PROGRESSION",
                    "Isosorbide Mononitrate",
                    new DateOnly(2025, 8, 5),
                    "Documented clinical effect."));

            var links =
                await new MedicationClinicalContextLinkService(repository)
                    .GetAsync(
                        new VeteranId("veteran-001"),
                        [
                            Entry(
                                1,
                                "12620234",
                                "ISOSORBIDE MONONITRATE 60MG SA TAB",
                                "60MG")
                        ]);

            Assert.Empty(links);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetAsync_RejectsAmbiguousDuplicatePrescriptionNumbers()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = await CreateAsync(path);
            var service = new MedicationClinicalContextLinkService(repository);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.GetAsync(
                    new VeteranId("veteran-001"),
                    [
                        Entry(1, "RX-1", "Medication A", "10MG"),
                        Entry(2, "RX-1", "Medication B", "20MG")
                    ]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<SqliteMedicationRepository> CreateAsync(
        string path)
    {
        await new VeteransClaimsSqliteSchema(path).InitializeAsync();
        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(
                new Veteran
                {
                    Id = new VeteranId("veteran-001")
                });

        return new SqliteMedicationRepository(path);
    }

    private static MedicationClinicalContext Context(
        string id,
        string prescriptionNumber,
        string medicationName,
        DateOnly eventDate,
        string summary) =>
        new()
        {
            Id = new MedicationClinicalContextId(id),
            VeteranId = new VeteranId("veteran-001"),
            SourceArtifactId = new ArtifactId("blue-button-note"),
            EventDate = eventDate,
            SourceStartPage = 1140,
            SourceEndPage = 1141,
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
            Status = "discontinued",
            PrescriptionNumber = prescriptionNumber,
            PrescribedDate = new DateOnly(2025, 6, 2)
        };
}
