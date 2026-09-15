using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageDetails
{
    public required EvidencePackageDetails PackageDetails
    { get; init; }

    public required IReadOnlyList<Artifact> Artifacts
    { get; init; }

    public IReadOnlyList<VeteransReviewerArtifactContent> ArtifactContents
    { get; init; } =
        Array.Empty<VeteransReviewerArtifactContent>();

    public IReadOnlyList<VeteransReviewerMedication> CurrentPrescribedMedications
    { get; init; } =
        Array.Empty<VeteransReviewerMedication>();

    public IReadOnlyList<VeteransReviewerMedicationProgression> MedicationProgressions
    { get; init; } =
        Array.Empty<VeteransReviewerMedicationProgression>();

    public IReadOnlyList<VeteransReviewerMedicationClinicalContext> MedicationClinicalContexts
    { get; init; } =
        Array.Empty<VeteransReviewerMedicationClinicalContext>();

    public IReadOnlyList<MedicationLedgerEntry> CurrentMedications
    { get; init; } =
        Array.Empty<MedicationLedgerEntry>();

    public string? MedicalOpinionRequested { get; init; }
}
