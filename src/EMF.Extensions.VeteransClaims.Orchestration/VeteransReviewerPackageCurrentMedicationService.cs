using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageCurrentMedicationService
{
    private readonly IClaimIssueRepository _issues;
    private readonly IClaimRepository _claims;
    private readonly CurrentMedicationLedgerService _currentMedications;

    public VeteransReviewerPackageCurrentMedicationService(
        IClaimIssueRepository issues,
        IClaimRepository claims,
        CurrentMedicationLedgerService currentMedications)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(currentMedications);

        _issues = issues;
        _claims = claims;
        _currentMedications = currentMedications;
    }

    public async Task<IReadOnlyList<MedicationLedgerEntry>> GetAsync(
        EvidencePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

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
            await _currentMedications.GetAsync(
                claim.VeteranId,
                cancellationToken);

        return snapshot?.Entries ?? [];
    }
}
