using EMF.Core.Contracts;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageMedicationClinicalContextService
{
    private readonly IClaimIssueRepository _issues;
    private readonly IClaimRepository _claims;
    private readonly IMedicationRepository _medications;
    private readonly IEvidenceRepository _evidence;

    public VeteransReviewerPackageMedicationClinicalContextService(
        IClaimIssueRepository issues,
        IClaimRepository claims,
        IMedicationRepository medications,
        IEvidenceRepository evidence)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(medications);
        ArgumentNullException.ThrowIfNull(evidence);

        _issues = issues;
        _claims = claims;
        _medications = medications;
        _evidence = evidence;
    }

    public async Task<IReadOnlyList<VeteransReviewerMedicationClinicalContext>>
        GetAsync(
            EvidencePackage package,
            IReadOnlyCollection<VeteransReviewerMedicationProgression> progressions,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(progressions);

        if (progressions.Count == 0)
            return [];

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

        var entries =
            progressions
                .SelectMany(progression => progression.Entries)
                .ToArray();

        if (entries.Length == 0)
            return [];

        var links =
            await new MedicationClinicalContextLinkService(_medications)
                .GetAsync(
                    claim.VeteranId,
                    entries,
                    cancellationToken);

        return await new VeteransReviewerMedicationClinicalContextProjectionService(
                _evidence)
            .GetAsync(
                links,
                cancellationToken);
    }
}
