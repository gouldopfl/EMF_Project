using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageCurrentMedicationService
{
    private readonly IClaimIssueRepository _issues;
    private readonly IClaimRepository _claims;
    private readonly ReconciledCurrentMedicationLedgerService _currentMedications;
    private readonly VeteransMedicationSourceEvidenceService? _sourceEvidence;

    public VeteransReviewerPackageCurrentMedicationService(
        IClaimIssueRepository issues,
        IClaimRepository claims,
        ReconciledCurrentMedicationLedgerService currentMedications,
        VeteransMedicationSourceEvidenceService? sourceEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(currentMedications);

        _issues = issues;
        _claims = claims;
        _currentMedications = currentMedications;
        _sourceEvidence = sourceEvidence;
    }

    public async Task<IReadOnlyList<MedicationLedgerEntry>> GetAsync(
        VeteransReviewerPackageDetails details,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        var package = details.PackageDetails.Package;
        var scope = new VeteransReviewerPackageEvidenceScope(details);

        var issue =
            await _issues.GetClaimIssueAsync(
                package.ClaimIssueId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package claim issue was not found.");

        if (issue.Id != package.ClaimIssueId)
            throw new InvalidOperationException(
                "Reviewer package claim issue identity mismatch.");

        var claim =
            await _claims.GetClaimAsync(
                issue.ClaimId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package claim was not found.");

        if (claim.Id != issue.ClaimId)
            throw new InvalidOperationException(
                "Reviewer package claim identity mismatch.");

        var snapshot =
            await _currentMedications.GetVerifiedAsync(
                claim.VeteranId,
                cancellationToken,
                scope.ArtifactIds);

        if (snapshot is null) return [];
        var sourceEvidence = _sourceEvidence is null ? null :
            await _sourceEvidence.GetAsync(snapshot.Ledger, snapshot.Entries, cancellationToken);
        return snapshot.Entries.Where(entry => scope.Contains(snapshot.Ledger.SourceArtifactId,
            entry.SourceStartPage, entry.SourceEndPage) && MedicationLedgerSource.IsVaPrescription(entry,
            sourceEvidence?.GetValueOrDefault(entry.Id))).ToArray();
    }
}
