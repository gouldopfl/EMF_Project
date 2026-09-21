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

        Assert.Contains("Current Medication Use — Reconciled", text);
        Assert.Contains("traZODone (traZODone 100 mg tablet)", text);
        Assert.Contains("Strength: 100 mg", text);
        Assert.Contains("TAKE THREE TABLETS ORALLY AT BEDTIME FOR INSOMNIA", text);
        Assert.Contains("VA prescription status: Active", text);
        Assert.Contains(
            "Current use: Confirmed during medication reconciliation",
            text);
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
        Assert.Contains("VA prescription status: Refill in process", text);
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
                Entries = [current]
            };

        var text = RenderText(current, [progression]);

        Assert.Contains("Relevant Medications for Medical Opinion", text);
        Assert.Contains("Isosorbide Mononitrate", text);
        Assert.DoesNotContain("Discontinued", text);
        Assert.Contains("Current Medication Use — Reconciled", text);
        Assert.Contains("VA prescription status: Refill in process", text);

        Assert.True(
            text.IndexOf(
                "Current Medication Use — Reconciled",
                StringComparison.Ordinal) <
            text.IndexOf(
                "Relevant Medications for Medical Opinion",
                StringComparison.Ordinal));
        Assert.Contains(
            "Historical non-current prescription states remain preserved in the underlying VA medication ledger",
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
                "August 21, 2026 — Refill in process — 30 mg/24 hour",
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
