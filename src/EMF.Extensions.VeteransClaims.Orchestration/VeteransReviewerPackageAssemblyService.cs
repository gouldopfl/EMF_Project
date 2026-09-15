using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageAssemblyService
{
    private readonly VeteransReviewerPackageDetailsService _details;
    private readonly VeteransReviewerPackageCurrentMedicationService _currentMedications;
    private readonly VeteransReviewerPackageMedicationProgressionService _medicationProgressions;
    private readonly VeteransReviewerPackageMedicationClinicalContextService _medicationClinicalContexts;
    private readonly VeteransReviewerPackageSourceClarificationService _sourceClarifications;
    private readonly VeteransReviewerPackageClinicalProgressionService _clinicalProgression;
    private readonly VeteransReviewerMedicalOpinionRequestService _medicalOpinionRequest;

    public VeteransReviewerPackageAssemblyService(
        VeteransReviewerPackageDetailsService details,
        VeteransReviewerPackageCurrentMedicationService currentMedications,
        VeteransReviewerPackageMedicationProgressionService medicationProgressions,
        VeteransReviewerPackageMedicationClinicalContextService medicationClinicalContexts,
        VeteransReviewerPackageSourceClarificationService sourceClarifications,
        VeteransReviewerPackageClinicalProgressionService clinicalProgression,
        VeteransReviewerMedicalOpinionRequestService medicalOpinionRequest)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(currentMedications);
        ArgumentNullException.ThrowIfNull(medicationProgressions);
        ArgumentNullException.ThrowIfNull(medicationClinicalContexts);
        ArgumentNullException.ThrowIfNull(sourceClarifications);
        ArgumentNullException.ThrowIfNull(clinicalProgression);
        ArgumentNullException.ThrowIfNull(medicalOpinionRequest);

        _details = details;
        _currentMedications = currentMedications;
        _medicationProgressions = medicationProgressions;
        _medicationClinicalContexts = medicationClinicalContexts;
        _sourceClarifications = sourceClarifications;
        _clinicalProgression = clinicalProgression;
        _medicalOpinionRequest = medicalOpinionRequest;
    }

    public async Task<VeteransReviewerPackageDetails?> AssembleAsync(
        EvidencePackageId packageId,
        CancellationToken cancellationToken = default)
    {
        var details = await _details.GetAsync(packageId, cancellationToken);

        if (details is null)
            return null;

        var package = details.PackageDetails.Package;

        var currentMedications =
            await _currentMedications.GetAsync(package, cancellationToken);

        var medicationProgressions =
            await _medicationProgressions.GetAsync(package, cancellationToken);

        var medicationClinicalContexts =
            await _medicationClinicalContexts.GetAsync(
                package,
                medicationProgressions,
                cancellationToken);

        var sourceClarifications =
            await _sourceClarifications.GetAsync(details, cancellationToken);

        var clinicalProgressionEvents =
            await _clinicalProgression.GetAsync(details, cancellationToken);

        var medicalOpinionRequested =
            await _medicalOpinionRequest.GetAsync(package, cancellationToken);

        return new VeteransReviewerPackageDetails
        {
            PackageDetails = details.PackageDetails,
            Artifacts = details.Artifacts,
            ArtifactContents = details.ArtifactContents,
            MedicationProgressions = medicationProgressions,
            MedicationClinicalContexts = medicationClinicalContexts,
            SourceClarifications = sourceClarifications,
            ClinicalProgressionEvents = clinicalProgressionEvents,
            CurrentMedications = currentMedications,
            MedicalOpinionRequested = medicalOpinionRequested
        };
    }
}
