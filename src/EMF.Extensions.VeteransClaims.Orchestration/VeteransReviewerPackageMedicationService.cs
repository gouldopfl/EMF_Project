using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageMedicationService
{
    private readonly IClaimIssueRepository _issues;
    private readonly IClaimRepository _claims;
    private readonly IServiceConnectionRepository _connections;
    private readonly CurrentMedicationService _currentMedications;
    private readonly MedicationHistorySummaryService _history;

    public VeteransReviewerPackageMedicationService(
        IClaimIssueRepository issues,
        IClaimRepository claims,
        IServiceConnectionRepository connections,
        CurrentMedicationService currentMedications,
        MedicationHistorySummaryService history)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(currentMedications);
        ArgumentNullException.ThrowIfNull(history);

        _issues = issues;
        _claims = claims;
        _connections = connections;
        _currentMedications = currentMedications;
        _history = history;
    }

    public async Task<IReadOnlyList<VeteransReviewerMedication>> GetAsync(
        EvidencePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.ServiceConnectionBasisId is null)
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

        var basis =
            await _connections.GetServiceConnectionBasisAsync(
                package.ServiceConnectionBasisId.Value,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package service-connection basis was not found.");

        if (basis.Id != package.ServiceConnectionBasisId.Value ||
            basis.ClaimIssueId != package.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Reviewer package service-connection basis lineage mismatch.");
        }

        var medicationNames =
            await _connections.GetPrescribedMedicationNamesAsync(
                basis.Id,
                cancellationToken);

        if (medicationNames.Count == 0)
            return [];

        var claim =
            await _claims.GetClaimAsync(
                issue.ClaimId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package claim was not found.");

        if (claim.Id != issue.ClaimId)
            throw new InvalidOperationException(
                "Reviewer package claim identity mismatch.");

        var relevant =
            medicationNames
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var current =
            await _currentMedications.GetCurrentMedicationsAsync(
                claim.VeteranId,
                cancellationToken);

        var reviewerMedications =
            new List<VeteransReviewerMedication>();

        foreach (var medication in
            current.Where(
                medication =>
                    relevant.Contains(
                        medication.MedicationName.Trim())))
        {
            reviewerMedications.Add(
                new VeteransReviewerMedication
                {
                    CurrentMedication = medication,
                    EarliestDocumentedRelease =
                        await _history.GetEarliestDocumentedReleaseAsync(
                            claim.VeteranId,
                            medication.MedicationName,
                            cancellationToken)
                });
        }

        return reviewerMedications;
    }
}
