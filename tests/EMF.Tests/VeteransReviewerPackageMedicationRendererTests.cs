using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageMedicationRendererTests
{
    [Theory]
    [InlineData(3911, 3912)]
    [InlineData(3925, 3926)]
    public void Render_SuppressesSourcePagesOnlyInPhysicianAttribution(int first, int last)
    {
        var entry = new MedicationLedgerEntry
        {
            Id = new("entry-lineage"), MedicationLedgerId = new("ledger-lineage"), EntryOrdinal = 1,
            SourceStartPage = first, SourceEndPage = last, MedicationName = "Example medication",
            Strength = "20 mg", Status = "active", Directions = "TAKE ONE TABLET DAILY",
            Indication = "Documented indication", PrescriptionNumber = "RX-123"
        };
        var attribution = $"Example VA Facility — VA medication report dated September 9, 2026 — source pages {first}–{last}; prescription RX-123.";
        var progression = new VeteransReviewerMedicationProgression
        {
            MedicationName = "Example medication", Entries = [entry],
            EntrySources = new Dictionary<MedicationLedgerEntryId, string> { [entry.Id] = attribution }
        };
        var text = RenderText(entry, [progression]);
        Assert.DoesNotContain("source pages", text);
        Assert.DoesNotContain($"{first}–{last}", text);
        foreach (var retained in new[] { "Example VA Facility", "September 9, 2026", "RX-123", "20 mg", "Active", "TAKE ONE TABLET DAILY", "Documented indication" })
            Assert.Contains(retained, text);
        Assert.Equal(first, entry.SourceStartPage);
        Assert.Equal(last, entry.SourceEndPage);
        Assert.Equal(attribution, progression.EntrySources[entry.Id]);
        Assert.Equal(new MedicationLedgerId("ledger-lineage"), entry.MedicationLedgerId);
    }

    [Fact]
    public void Render_DoesNotSurfaceStandaloneCurrentUseReconciliation()
    {
        var text = RenderText(
            new MedicationLedgerEntry
            {
                Id = new MedicationLedgerEntryId("entry-1"),
                MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                EntryOrdinal = 1,
                SourceStartPage = 3922,
                SourceEndPage = 3922,
                MedicationName = "traZODone (traZODone 100 mg tablet)",
                Strength = "100 mg",
                Status = "active",
                Directions = "TAKE THREE TABLETS ORALLY AT BEDTIME FOR INSOMNIA",
                Indication = "None recorded"
            });

        Assert.DoesNotContain("Medication Use Reconciliation", text);
        Assert.DoesNotContain("Current use:", text);
        Assert.DoesNotContain("traZODone (traZODone 100 mg tablet)", text);
    }

    [Fact]
    public void Render_HumanizesRefillInProcessStatus()
    {
        var medication =
            new MedicationLedgerEntry
            {
                Id = new MedicationLedgerEntryId("entry-2"),
                MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                EntryOrdinal = 2,
                SourceStartPage = 3923,
                SourceEndPage = 3923,
                MedicationName = "isosorbide mononitrate",
                Strength = "30 mg/24 hour",
                Status = "refillinprocess",
                Directions = "TAKE ONE TABLET ORALLY EVERY DAY WITH BREAKFAST FOR PREVENTING CHEST PAIN"
            };

        var progression =
            new VeteransReviewerMedicationProgression
            {
                MedicationName = "Isosorbide Mononitrate",
                Entries = [medication]
            };

        var text = RenderText(medication, [progression]);

        Assert.Contains("Relevant Medications for Medical Opinion", text);
        Assert.Contains("Isosorbide Mononitrate", text);
        Assert.Contains("VA ledger status: Refill in process", text);
        Assert.DoesNotContain("Medication Use Reconciliation", text);
    }

    [Fact]
    public void Render_KeepsCurrentUseReconciliationInternalWhenRelevantProgressionRenders()
    {
        var current =
            new MedicationLedgerEntry
            {
                Id = new MedicationLedgerEntryId("entry-current"),
                MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                EntryOrdinal = 3,
                SourceStartPage = 3923,
                SourceEndPage = 3923,
                MedicationName = "isosorbide mononitrate (isosorbide mononitrate ER 30 mg/24 hour tablet)",
                Strength = "30 mg/24 hour",
                Status = "refillinprocess",
                Directions = "TAKE ONE TABLET ORALLY EVERY DAY WITH BREAKFAST FOR PREVENTING CHEST PAIN"
            };

        var progression =
            new VeteransReviewerMedicationProgression
            {
                MedicationName = "Isosorbide Mononitrate",
                Entries = [current]
            };

        var text = RenderText(current, [progression]);

        Assert.Contains("Relevant Medications for Medical Opinion", text);
        Assert.Contains("Isosorbide Mononitrate", text);
        Assert.DoesNotContain("Discontinued", text);
        Assert.DoesNotContain("Medication Use Reconciliation", text);
        Assert.Contains("VA ledger status: Refill in process", text);
        Assert.DoesNotContain("Current use:", text);
        Assert.DoesNotContain("Medication Use Reconciliation", text);
        Assert.Contains(
            "Dated changes in dose, directions and recorded status are retained.",
            text);
    }


    [Fact]
    public void Render_GroupsProgressionsByServiceConnectedMedicationBasis()
    {
        var current =
            new MedicationLedgerEntry
            {
                Id = new MedicationLedgerEntryId("entry-current"),
                MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                EntryOrdinal = 3,
                SourceStartPage = 3923,
                SourceEndPage = 3923,
                MedicationName = "isosorbide mononitrate",
                Strength = "30 mg/24 hour",
                Status = "active",
                Directions = "TAKE ONE TABLET ORALLY EVERY DAY"
            };

        var progressions =
            new[]
            {
                new VeteransReviewerMedicationProgression
                {
                    ServiceConnectionBasisId =
                        new ServiceConnectionBasisId("basis-cad"),
                    ServiceConnectionBasisReviewerLabel =
                        "Secondary to medications used for service-connected coronary artery disease",
                    MedicationName = "Atorvastatin",
                    Entries = [current]
                },
                new VeteransReviewerMedicationProgression
                {
                    ServiceConnectionBasisId =
                        new ServiceConnectionBasisId("basis-mental-health"),
                    ServiceConnectionBasisReviewerLabel =
                        "Secondary to medications used for service-connected PTSD / Anxiety / Major Depression",
                    MedicationName = "Trazodone HCl",
                    Entries = [current]
                }
            };

        var text = RenderText(current, progressions);

        const string cadHeading =
            "Medications for service-connected coronary artery disease";
        const string mentalHealthHeading =
            "Medications for service-connected PTSD / Anxiety / Major Depression";

        Assert.Contains(cadHeading, text);
        Assert.Contains(mentalHealthHeading, text);
        Assert.Contains("Atorvastatin", text);
        Assert.Contains("Trazodone HCl", text);

        var cadIndex = text.IndexOf(cadHeading, StringComparison.Ordinal);
        var atorvastatinIndex = text.IndexOf("Atorvastatin", StringComparison.Ordinal);
        var mentalHealthIndex = text.IndexOf(mentalHealthHeading, StringComparison.Ordinal);
        var trazodoneIndex = text.IndexOf("Trazodone HCl", StringComparison.Ordinal);

        Assert.True(cadIndex >= 0);
        Assert.True(atorvastatinIndex > cadIndex);
        Assert.True(mentalHealthIndex > atorvastatinIndex);
        Assert.True(trazodoneIndex > mentalHealthIndex);

        var bytes = RenderBytes(current, progressions);
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);

        var medicationNameParagraphs =
            document.MainDocumentPart!
                .Document!
                .Body!
                .Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Where(paragraph =>
                    paragraph.InnerText == "Atorvastatin" ||
                    paragraph.InnerText == "Trazodone HCl")
                .ToArray();

        Assert.Equal(2, medicationNameParagraphs.Length);
        Assert.All(
            medicationNameParagraphs,
            paragraph =>
                Assert.Contains(
                    paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Bold>(),
                    bold => bold.Val?.Value != false));
    }

    [Fact]
    public void Render_AttachesClinicalContextOnlyToExplicitCurrentPrescriptionWithoutInternalPages()
    {
        var current =
            new MedicationLedgerEntry
            {
                Id = new MedicationLedgerEntryId("entry-30"),
                MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                EntryOrdinal = 2,
                SourceStartPage = 3923,
                SourceEndPage = 3923,
                MedicationName = "isosorbide mononitrate",
                Strength = "30 mg/24 hour",
                Status = "refillinprocess",
                PrescriptionNumber = "3211-50014120",
                PrescribedDate = new DateOnly(2026, 8, 21),
                Directions = "TAKE ONE TABLET ORALLY EVERY DAY"
            };

        var progression =
            new VeteransReviewerMedicationProgression
            {
                MedicationName = "Isosorbide Mononitrate",
                Entries = [current]
            };

        var context =
            new VeteransReviewerMedicationClinicalContext
            {
                MedicationName = current.MedicationName,
                ContextType = MedicationClinicalContextTypes.ClinicalEffect,
                PrescriptionNumber = "3211-50014120",
                SourceLocator =
                    "VA Blue Button Report — Cardiology Follow-up — August 21, 2026",
                Summary =
                    "The prescription remained part of the documented antianginal regimen."
            };

        var text =
            RenderText(
                current,
                [progression],
                [context]);

        var currentIndex =
            text.IndexOf(
                "Prescribed August 21, 2026 — VA ledger status: Refill in process — 30 mg/24 hour",
                StringComparison.Ordinal);
        var contextIndex =
            text.IndexOf(
                "Documented clinical context:",
                StringComparison.Ordinal);

        Assert.True(currentIndex >= 0);
        Assert.True(contextIndex > currentIndex);
        Assert.Contains(
            "Source: VA Blue Button Report — Cardiology Follow-up — August 21, 2026",
            text);
        Assert.DoesNotContain("3211-50014120", text);
    }

    [Fact]
    public void Render_AttributedIndicationDoesNotReplaceVaDirectionsOrImplyCurrentUse()
    {
        var entry = new MedicationLedgerEntry
        {
            Id = new("entry"), MedicationLedgerId = new("ledger"), EntryOrdinal = 1,
            SourceStartPage = 2, SourceEndPage = 2, MedicationName = "Examplemed",
            Status = "discontinued", Directions = "TAKE DAILY FOR MOOD. Refills: 3.",
            RefillsLeft = 2, PrescribedDate = new(2025, 1, 1)
        };
        var text = RenderText(entry, [new VeteransReviewerMedicationProgression
        {
            MedicationName = "Examplemed", Entries = [entry],
            EntrySources = new Dictionary<MedicationLedgerEntryId, string> { [entry.Id] = "Example VA Clinic — source page 2" },
            IndicationReconciliation = new()
            {
                Id = "statement", VeteranId = new("veteran"), MedicationName = "Examplemed",
                ReconciliationDate = new(2026, 9, 24), Indication = "anxiety",
                Source = "Veteran statement — Robin Example"
            }
        }]);
        Assert.Contains("Indication reconciliation — Veteran statement — Robin Example, September 24, 2026: anxiety", text);
        Assert.Contains("VA ledger directions: TAKE DAILY FOR MOOD.", text);
        Assert.Contains("Prescribed January 1, 2025 — VA ledger status: Discontinued", text);
        Assert.Contains("Refills:\u00a03.\u00a0Refills\u00a0left:\u00a02", text);
        Assert.Contains("Source: Example VA Clinic", text);
        Assert.DoesNotContain("source page", text);
        Assert.Contains("prescription date is not a discontinuation date", text);
        Assert.DoesNotContain("TAKE DAILY FOR ANXIETY", text);
    }

    private static string RenderText(
        MedicationLedgerEntry medication,
        IReadOnlyList<VeteransReviewerMedicationProgression>? progressions = null,
        IReadOnlyList<VeteransReviewerMedicationClinicalContext>? clinicalContexts = null)
    {
        var bytes =
            RenderBytes(
                medication,
                progressions,
                clinicalContexts);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        return document.MainDocumentPart!
            .Document!.Body!.InnerText;
    }

    private static byte[] RenderBytes(
        MedicationLedgerEntry medication,
        IReadOnlyList<VeteransReviewerMedicationProgression>? progressions = null,
        IReadOnlyList<VeteransReviewerMedicationClinicalContext>? clinicalContexts = null)
    {
        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
            {
                Package = new EvidencePackage
                {
                    Id = new EvidencePackageId("package-1"),
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Purpose = "Physician reviewer package",
                    ReviewerRole = "MedicalProfessional"
                },
                Artifacts = []
            },
            Artifacts = [],
            MedicationProgressions = progressions ?? [],
            MedicationClinicalContexts = clinicalContexts ?? [],
            CurrentMedications = [medication]
        };

        return VeteransReviewerPackageDocxRenderer.Render(details);
    }
}
