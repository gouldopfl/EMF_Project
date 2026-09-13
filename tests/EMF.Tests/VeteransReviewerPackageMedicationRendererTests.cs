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
    public void Render_ShowsMedicationAndEarliestDocumentedRelease()
    {
        var veteranId = new VeteranId("veteran-1");

        var current = new MedicationRecord
        {
            Id = new MedicationRecordId("med-1"),
            VeteranId = veteranId,
            SourceArtifactId = new ArtifactId("blue-button-current"),
            RecordDate = new DateOnly(2026, 8, 4),
            SourcePage = 429,
            MedicationName = "Trazodone HCl",
            Strength = "100MG",
            Directions = "THREE AT BEDTIME",
            Indication = "FOR INSOMNIA",
            Status = MedicationStatuses.Active
        };

        var release = new MedicationHistoryEvent
        {
            Id = new MedicationHistoryEventId("history-1"),
            VeteranId = veteranId,
            SourceArtifactId = new ArtifactId("blue-button-history"),
            EventDate = new DateOnly(2022, 12, 29),
            SourcePage = 2448,
            MedicationName = "Trazodone HCl",
            EventType = MedicationHistoryEventTypes.LastReleased,
            PrescriptionNumber = "rx-private-123"
        };

        var text = RenderText(
            new VeteransReviewerMedication
            {
                CurrentMedication = current,
                EarliestDocumentedRelease = release
            });

        Assert.Contains("Trazodone HCl", text);
        Assert.Contains("Current status: Active", text);
        Assert.Contains(
            "Earliest documented VA release: December 29, 2022",
            text);
        Assert.DoesNotContain("rx-private-123", text);
        Assert.DoesNotContain("blue-button-history", text);
        Assert.DoesNotContain("blue-button-current", text);
    }

    [Fact]
    public void Render_OmitsReleaseLineWhenHistoryIsAbsent()
    {
        var current = new MedicationRecord
        {
            Id = new MedicationRecordId("med-2"),
            VeteranId = new VeteranId("veteran-1"),
            SourceArtifactId = new ArtifactId("blue-button"),
            RecordDate = new DateOnly(2026, 8, 4),
            SourcePage = 429,
            MedicationName = "Sertraline HCl",
            Status = MedicationStatuses.Active
        };

        var text = RenderText(
            new VeteransReviewerMedication
            {
                CurrentMedication = current
            });

        Assert.Contains("Sertraline HCl", text);
        Assert.DoesNotContain(
            "Earliest documented VA release:",
            text);
    }

    private static string RenderText(
        VeteransReviewerMedication medication)
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
            CurrentPrescribedMedications = [medication]
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
