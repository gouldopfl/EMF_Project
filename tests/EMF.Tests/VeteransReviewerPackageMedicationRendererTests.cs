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

    private static string RenderText(
        MedicationLedgerEntry medication)
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
