using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageAssemblyService
{
    private readonly IEvidencePackageRepository? _snapshotRepository;
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
        VeteransReviewerMedicalOpinionRequestService medicalOpinionRequest,
        IEvidencePackageRepository? snapshotRepository = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(currentMedications);
        ArgumentNullException.ThrowIfNull(medicationProgressions);
        ArgumentNullException.ThrowIfNull(medicationClinicalContexts);
        ArgumentNullException.ThrowIfNull(sourceClarifications);
        ArgumentNullException.ThrowIfNull(clinicalProgression);
        ArgumentNullException.ThrowIfNull(medicalOpinionRequest);

        _snapshotRepository = snapshotRepository;
        _details = details;
        _currentMedications = currentMedications;
        _medicationProgressions = medicationProgressions;
        _medicationClinicalContexts = medicationClinicalContexts;
        _sourceClarifications = sourceClarifications;
        _clinicalProgression = clinicalProgression;
        _medicalOpinionRequest = medicalOpinionRequest;
    }

    public Task<VeteransReviewerPackageDetails?> AssembleAsync(
        EvidencePackageId packageId,
        CancellationToken cancellationToken = default) =>
        AssembleAsync(
            packageId,
            packagePreparedBy: null,
            veteranDisplayName: null,
            cancellationToken);

    public Task<VeteransReviewerPackageDetails?> AssembleAsync(
        EvidencePackageId packageId,
        string? packagePreparedBy,
        CancellationToken cancellationToken = default) =>
        AssembleAsync(
            packageId,
            packagePreparedBy,
            veteranDisplayName: null,
            cancellationToken);

    public async Task<VeteransReviewerPackageDetails?> AssembleAsync(
        EvidencePackageId packageId,
        string? packagePreparedBy,
        string? veteranDisplayName,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotRepository is not null)
        {
            var snapshot = await _snapshotRepository.GetReviewerSnapshotAsync(packageId, cancellationToken);
            if (snapshot is not null)
                return VeteransReviewerPackageSnapshot.Restore(snapshot).Details;
        }
        var details = await _details.GetAsync(packageId, cancellationToken);

        if (details is null)
            return null;

        return await CompleteCurrentAsync(details, packagePreparedBy, veteranDisplayName, cancellationToken);
    }

    public async Task<VeteransReviewerPackageDetails> AssembleCurrentAsync(
        EvidencePackageDetails package, string? packagePreparedBy, string? veteranDisplayName,
        CancellationToken cancellationToken = default) =>
        await CompleteCurrentAsync(await _details.GetCurrentAsync(package, cancellationToken),
            packagePreparedBy, veteranDisplayName, cancellationToken);

    private async Task<VeteransReviewerPackageDetails> CompleteCurrentAsync(
        VeteransReviewerPackageDetails details, string? packagePreparedBy, string? veteranDisplayName,
        CancellationToken cancellationToken)
    {
        using var loadingTiming = EmfPerformanceTiming.MeasureTopLevel(EmfPerformancePhase.EvidenceLoading);
        var package = details.PackageDetails.Package;

        var currentMedications =
            await _currentMedications.GetAsync(details, cancellationToken);

        var medicationProgressions =
            await _medicationProgressions.GetAsync(details, cancellationToken);

        var medicationClinicalContexts =
            await _medicationClinicalContexts.GetAsync(
                details,
                medicationProgressions,
                cancellationToken);

        var sourceClarifications =
            await _sourceClarifications.GetAsync(details, cancellationToken);

        var clinicalProgressionEvents =
            await _clinicalProgression.GetAsync(details, cancellationToken);

        var medicalOpinionRequested =
            await _medicalOpinionRequest.GetAsync(package, cancellationToken);

        var coverScope = await _medicalOpinionRequest.GetCoverScopeAsync(package, cancellationToken);
        return new VeteransReviewerPackageDetails
        {
            ResolvedCover = new(
                string.IsNullOrWhiteSpace(veteranDisplayName) ? null : veteranDisplayName.Trim(),
                coverScope.ClaimType, coverScope.Condition, coverScope.Basis,
                string.IsNullOrWhiteSpace(packagePreparedBy) ? null : packagePreparedBy.Trim(),
                package.ReviewerRole),
            PackageDetails = details.PackageDetails,
            Artifacts = details.Artifacts,
            ArtifactContents = details.ArtifactContents,
            MedicationProgressions = medicationProgressions,
            MedicationClinicalContexts = medicationClinicalContexts,
            SourceClarifications = sourceClarifications,
            ClinicalProgressionEvents = clinicalProgressionEvents,
            CurrentMedications = currentMedications,
            MedicalOpinionRequested = medicalOpinionRequested,
            VeteranDisplayName =
                string.IsNullOrWhiteSpace(veteranDisplayName)
                    ? null
                    : veteranDisplayName.Trim(),
            PackagePreparedBy =
                string.IsNullOrWhiteSpace(packagePreparedBy)
                    ? null
                    : packagePreparedBy.Trim()
        };
    }
}
