using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageMedicationRendererTests
{
    [Fact]
    public void Render_ShowsCompleteCurrentMedicationEntry()
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

        Assert.Contains("Current Medication List", text);
        Assert.Contains("traZODone (traZODone 100 mg tablet)", text);
        Assert.Contains("Strength: 100 mg", text);
        Assert.Contains("TAKE THREE TABLETS ORALLY AT BEDTIME FOR INSOMNIA", text);
        Assert.Contains("Current status: Active", text);
        Assert.DoesNotContain("Documented indication: None recorded", text);
        Assert.DoesNotContain("Earliest documented VA release:", text);
    }

    [Fact]
    public void Render_HumanizesRefillInProcessStatus()
    {
        var text = RenderText(
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
            });

        Assert.Contains("isosorbide mononitrate", text);
        Assert.Contains("Current status: Refill in process", text);
    }

    [Fact]
    public void Render_ShowsRelevantProgressionSeparatelyFromCurrentList()
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
                Entries =
                [
                    new MedicationLedgerEntry
                    {
                        Id = new MedicationLedgerEntryId("entry-history-1"),
                        MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                        EntryOrdinal = 1,
                        SourceStartPage = 3990,
                        SourceEndPage = 3990,
                        MedicationName = "ISOSORBIDE MONONITRATE 60MG SA TAB",
                        Strength = "60MG",
                        Status = "discontinued",
                        PrescribedDate = new DateOnly(2025, 6, 2),
                        Directions = "TAKE ONE TABLET ORALLY EVERY DAY"
                    },
                    current
                ]
            };

        var text = RenderText(current, [progression]);

        Assert.Contains("Relevant Medication Progression / History", text);
        Assert.Contains("Isosorbide Mononitrate", text);
        Assert.Contains("June 2, 2025 — Discontinued — 60MG", text);
        Assert.Contains("Current Medication List", text);
        Assert.Contains("Current status: Refill in process", text);
        Assert.Contains(
            "does not infer a clinical reason for a change unless that reason is separately documented",
            text);
    }


    [Fact]
    public void Render_AttachesClinicalContextOnlyToExplicitPrescriptionWithoutInternalPages()
    {
        var sixty =
            new MedicationLedgerEntry
            {
                Id = new MedicationLedgerEntryId("entry-60"),
                MedicationLedgerId = new MedicationLedgerId("ledger-1"),
                EntryOrdinal = 1,
                SourceStartPage = 3943,
                SourceEndPage = 3943,
                MedicationName = "ISOSORBIDE MONONITRATE 60MG SA TAB",
                Strength = "60MG",
                Status = "discontinued",
                PrescriptionNumber = "12620234",
                PrescribedDate = new DateOnly(2025, 6, 2),
                Directions = "TAKE ONE TABLET ORALLY EVERY DAY"
            };

        var thirty =
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
                Entries = [sixty, thirty]
            };

        var context =
            new VeteransReviewerMedicationClinicalContext
            {
                MedicationName = sixty.MedicationName,
                ContextType = MedicationClinicalContextTypes.ClinicalEffect,
                PrescriptionNumber = "12620234",
                SourceLocator =
                    "VA Blue Button Report — PC Nursing Outpatient Telephone Note — August 12, 2025",
                Summary =
                    "The Veteran reported dizziness after the 60 mg increase; the same note records that Cardiology did not agree that isosorbide caused the complaints."
            };

        var text =
            RenderText(
                thirty,
                [progression],
                [context]);

        var sixtyIndex =
            text.IndexOf(
                "June 2, 2025 — Discontinued — 60MG",
                StringComparison.Ordinal);
        var contextIndex =
            text.IndexOf(
                "Documented clinical context:",
                StringComparison.Ordinal);
        var thirtyIndex =
            text.IndexOf(
                "August 21, 2026 — Refill in process — 30 mg/24 hour",
                StringComparison.Ordinal);

        Assert.True(sixtyIndex >= 0);
        Assert.True(contextIndex > sixtyIndex);
        Assert.True(thirtyIndex > contextIndex);
        Assert.Contains(
            "Source: VA Blue Button Report — PC Nursing Outpatient Telephone Note — August 12, 2025",
            text);
        Assert.DoesNotContain("948", text);
        Assert.DoesNotContain("949", text);
        Assert.DoesNotContain("12620234", text);
    }

    private static string RenderText(
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

        var bytes =
            VeteransReviewerPackageDocxRenderer.Render(details);

        using var stream = new MemoryStream(bytes);
        using var document =
            WordprocessingDocument.Open(stream, false);

        return document.MainDocumentPart!
            .Document!.Body!.InnerText;
    }
}
